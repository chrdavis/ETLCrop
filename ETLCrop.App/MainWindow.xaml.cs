using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ETLCrop;
using Microsoft.Win32;

namespace ETLCrop.App;

/// <summary>
/// Interaction logic for MainWindow.xaml.
/// </summary>
public partial class MainWindow : Window
{
    private const string EtlFilter = "ETL traces (*.etl)|*.etl|All files (*.*)|*.*";

    // Total duration of the currently loaded input trace, in milliseconds, or zero when unknown.
    private double _traceDurationMSec;

    // Guards the two-way Start/Stop slider <-> text-box synchronization against re-entrancy.
    private bool _syncingTimeControls;

    // True while a crop is running, so trace-info loads do not re-enable controls mid-crop.
    private bool _busy;

    // Increments on each input change so a slower, superseded trace-info read can be discarded.
    private int _traceInfoLoadToken;

    // Cancels the in-progress crop; non-null only while a crop is running.
    private CancellationTokenSource? _cropCancellation;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the ETL file to crop",
            Filter = EtlFilter,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            InputPathBox.Text = dialog.FileName;

            if (string.IsNullOrWhiteSpace(OutputPathBox.Text))
            {
                OutputPathBox.Text = SuggestOutputPath(dialog.FileName);
            }

            _ = LoadTraceInfoAsync(dialog.FileName);
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save the cropped ETL as",
            Filter = EtlFilter,
            AddExtension = true,
            DefaultExt = ".etl",
            OverwritePrompt = true,
        };

        if (!string.IsNullOrWhiteSpace(InputPathBox.Text))
        {
            dialog.FileName = Path.GetFileName(SuggestOutputPath(InputPathBox.Text));
        }

        if (dialog.ShowDialog(this) == true)
        {
            OutputPathBox.Text = dialog.FileName;
        }
    }

    private void InputPathBox_LostFocus(object sender, RoutedEventArgs e)
    {
        string path = InputPathBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            _ = LoadTraceInfoAsync(path);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_busy && TryGetDroppedEtlPath(e, out _)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (_busy || !TryGetDroppedEtlPath(e, out string path))
        {
            return;
        }

        e.Handled = true;
        InputPathBox.Text = path;

        if (string.IsNullOrWhiteSpace(OutputPathBox.Text))
        {
            OutputPathBox.Text = SuggestOutputPath(path);
        }

        _ = LoadTraceInfoAsync(path);
    }

    /// <summary>
    /// Extracts a single dropped <c>.etl</c> file path from a drag payload, if present.
    /// </summary>
    private static bool TryGetDroppedEtlPath(DragEventArgs e, out string path)
    {
        path = string.Empty;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files)
        {
            return false;
        }

        string candidate = files[0];
        if (!File.Exists(candidate) ||
            !string.Equals(Path.GetExtension(candidate), ".etl", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    /// <summary>
    /// Reads the input trace's duration off the UI thread and uses it to configure the Start/Stop
    /// sliders and the duration label. Stale loads (when the input changes quickly) are discarded.
    /// </summary>
    private async Task LoadTraceInfoAsync(string path)
    {
        int token = ++_traceInfoLoadToken;
        TraceInfoText.Text = "Reading trace duration...";

        try
        {
            EtlTraceInfo info = await Task.Run(() => EtlCropper.ReadTraceInfo(path));

            if (token != _traceInfoLoadToken)
            {
                return;
            }

            _traceDurationMSec = info.DurationMSec;
            ConfigureTimeRange(info.DurationMSec);

            string lost = info.EventsLost > 0 ? $", {info.EventsLost:N0} events lost in capture" : string.Empty;
            TraceInfoText.Text =
                $"Duration {info.DurationMSec:N0} ms ({info.SessionDurationText()}){lost}. " +
                "Use the slider or boxes to choose a window.";
        }
        catch (Exception ex) when (token == _traceInfoLoadToken)
        {
            _traceDurationMSec = 0;
            ConfigureTimeRange(0);
            TraceInfoText.Text = $"Could not read trace duration: {ex.Message}";
        }
    }

    /// <summary>
    /// Sets the range slider's maximum to the trace duration and enables it only when a duration is
    /// known. Existing Start/Stop text values are reflected onto the slider thumbs.
    /// </summary>
    private void ConfigureTimeRange(double durationMSec)
    {
        _syncingTimeControls = true;
        try
        {
            bool hasDuration = durationMSec > 0;
            TimeRangeSlider.Maximum = hasDuration ? durationMSec : 0;
            SlidersPanel.IsEnabled = hasDuration && !_busy;

            double start = TryParseTime(StartBox.Text, 0, out double s) ? s : 0;
            double stop = TryParseTime(StopBox.Text, durationMSec, out double e) ? e : durationMSec;

            double max = Math.Max(0, durationMSec);
            double lower = Math.Clamp(start, 0, max);
            double upper = double.IsPositiveInfinity(stop) ? max : Math.Clamp(stop, 0, max);

            TimeRangeSlider.LowerValue = lower;
            TimeRangeSlider.UpperValue = upper;
        }
        finally
        {
            _syncingTimeControls = false;
        }
    }

    private void RangeSlider_RangeChanged(object sender, EventArgs e)
    {
        if (_syncingTimeControls)
        {
            return;
        }

        _syncingTimeControls = true;
        try
        {
            StartBox.Text = FormatMSec(TimeRangeSlider.LowerValue);
            StopBox.Text = FormatMSec(TimeRangeSlider.UpperValue);
        }
        finally
        {
            _syncingTimeControls = false;
        }
    }

    private void StartBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingTimeControls || _traceDurationMSec <= 0)
        {
            return;
        }

        if (TryParseTime(StartBox.Text, 0, out double value))
        {
            _syncingTimeControls = true;
            try
            {
                TimeRangeSlider.LowerValue = Math.Clamp(value, 0, _traceDurationMSec);
            }
            finally
            {
                _syncingTimeControls = false;
            }
        }
    }

    private void StopBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingTimeControls || _traceDurationMSec <= 0)
        {
            return;
        }

        if (TryParseTime(StopBox.Text, _traceDurationMSec, out double value) && !double.IsPositiveInfinity(value))
        {
            _syncingTimeControls = true;
            try
            {
                TimeRangeSlider.UpperValue = Math.Clamp(value, 0, _traceDurationMSec);
            }
            finally
            {
                _syncingTimeControls = false;
            }
        }
    }

    private static string FormatMSec(double value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);

    private async void CropButton_Click(object sender, RoutedEventArgs e)
    {
        string input = InputPathBox.Text.Trim();
        string output = OutputPathBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(input))
        {
            SetStatus("Please choose an input ETL file.", isError: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            SetStatus("Please choose an output ETL file.", isError: true);
            return;
        }

        if (!TryParseTime(StartBox.Text, 0, out double startMs))
        {
            SetStatus("Start time must be a number of milliseconds.", isError: true);
            return;
        }

        if (!TryParseTime(StopBox.Text, double.PositiveInfinity, out double stopMs))
        {
            SetStatus("Stop time must be a number of milliseconds.", isError: true);
            return;
        }

        var options = new EtlCropOptions
        {
            StartTimeRelativeMSec = startMs,
            StopTimeRelativeMSec = stopMs,
            KeepMetadataEvents = KeepMetadataCheck.IsChecked == true,
            ClampKeptEventsToWindow = ClampWindowCheck.IsChecked == true,
            RebaseToWindowStart = RebaseCheck.IsChecked == true,
        };

        try
        {
            options.Validate();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
            return;
        }

        if (File.Exists(output))
        {
            MessageBoxResult choice = MessageBox.Show(
                this,
                $"The file already exists and will be overwritten:{Environment.NewLine}{Environment.NewLine}{output}{Environment.NewLine}{Environment.NewLine}Do you want to replace it?",
                "Overwrite existing file?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (choice != MessageBoxResult.Yes)
            {
                SetStatus("Crop cancelled - the existing output file was kept.", isError: false);
                return;
            }
        }

        var progress = new Progress<EtlCropProgress>(p =>
        {
            UpdateProgress(p.PercentComplete);
            SetStatus(
                p.PercentComplete is { } pct
                    ? $"{pct:N1}% - read {p.EventsRead:N0} events, wrote {p.EventsWritten:N0} ..."
                    : $"Read {p.EventsRead:N0} events, wrote {p.EventsWritten:N0} ...",
                isError: false);
        });

        SetBusy(true);
        UpdateProgress(0);
        _cropCancellation = new CancellationTokenSource();
        CancellationToken token = _cropCancellation.Token;
        try
        {
            var cropper = new EtlCropper();
            EtlCropResult result = await Task.Run(() => cropper.Crop(input, output, options, progress, token), token);

            if (result.EmbeddedTimestampAnomalies > 0)
            {
                SetStatus(
                    $"Warning: {result.EmbeddedTimestampAnomalies:N0} DPC/ISR events in the rebased output have " +
                    $"inconsistent embedded timestamps, so Windows Performance Analyzer will reject it " +
                    $"(0x8000FFFF). This usually means a stale build - rebuild ETLCrop and crop again." +
                    $"{Environment.NewLine}Output: {result.OutputPath}",
                    isError: true);
                return;
            }

            SetStatus(
                $"Done. Wrote {result.EventsWritten:N0} of {result.EventsRead:N0} events " +
                $"({result.EventsDropped:N0} dropped, {result.MetadataEventsKept:N0} metadata kept, " +
                $"{result.EventsRetimed:N0} rundown re-timed to stop) " +
                $"in {result.Elapsed:hh\\:mm\\:ss\\.fff}.{Environment.NewLine}Output: {result.OutputPath}",
                isError: false);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Crop cancelled. The partial output file may be incomplete.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed: {ex.Message}", isError: true);
        }
        finally
        {
            _cropCancellation.Dispose();
            _cropCancellation = null;
            SetBusy(false);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cropCancellation is { IsCancellationRequested: false } cancellation)
        {
            cancellation.Cancel();
            CancelButton.IsEnabled = false;
            SetStatus("Cancelling...", isError: false);
        }
    }

    private static string SuggestOutputPath(string inputPath)
    {
        string directory = Path.GetDirectoryName(inputPath) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(inputPath);
        string extension = Path.GetExtension(inputPath);
        return Path.Combine(directory, $"{name}.cropped{extension}");
    }

    private static bool TryParseTime(string? text, double fallback, out double value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = fallback;
            return true;
        }

        // Accept thousands separators so values written by the sliders (for example "2,498") parse
        // back as readily as a plain "2498" typed by hand.
        return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        CropButton.IsEnabled = !busy;
        InputPathBox.IsEnabled = !busy;
        OutputPathBox.IsEnabled = !busy;
        StartBox.IsEnabled = !busy;
        StopBox.IsEnabled = !busy;
        SlidersPanel.IsEnabled = !busy && _traceDurationMSec > 0;
        KeepMetadataCheck.IsEnabled = !busy;
        ClampWindowCheck.IsEnabled = !busy;
        RebaseCheck.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;

        // Swap the Crop button for a Cancel button while a crop runs, so the operation can be
        // stopped without closing the window.
        CropButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = busy;

        if (!busy)
        {
            UpdateProgress(null);
        }
    }

    /// <summary>
    /// Drives the progress bar and its percentage label. A known percentage shows a determinate bar;
    /// a <see langword="null"/> percentage while busy falls back to an indeterminate bar, and clears
    /// the bar when idle.
    /// </summary>
    private void UpdateProgress(double? percent)
    {
        if (percent is { } value)
        {
            Progress.IsIndeterminate = false;
            Progress.Value = value;
            ProgressText.Text = $"{value:N0}%";
        }
        else if (_busy)
        {
            Progress.IsIndeterminate = true;
            ProgressText.Text = string.Empty;
        }
        else
        {
            Progress.IsIndeterminate = false;
            Progress.Value = 0;
            ProgressText.Text = string.Empty;
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        string brushKey = isError ? "ErrorBrush" : "PrimaryForegroundBrush";
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
    }
}
