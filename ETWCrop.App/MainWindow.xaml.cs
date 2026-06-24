using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using ETWCrop;
using Microsoft.Win32;

namespace ETWCrop.App;

/// <summary>
/// Interaction logic for MainWindow.xaml.
/// </summary>
public partial class MainWindow : Window
{
    private const string EtlFilter = "ETL traces (*.etl)|*.etl|All files (*.*)|*.*";

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

        var progress = new Progress<EtlCropProgress>(p =>
            SetStatus($"Read {p.EventsRead:N0} events, wrote {p.EventsWritten:N0} ...", isError: false));

        SetBusy(true);
        try
        {
            var cropper = new EtlCropper();
            EtlCropResult result = await Task.Run(() => cropper.Crop(input, output, options, progress));

            SetStatus(
                $"Done. Wrote {result.EventsWritten:N0} of {result.EventsRead:N0} events " +
                $"({result.EventsDropped:N0} dropped, {result.MetadataEventsKept:N0} metadata kept) " +
                $"in {result.Elapsed:hh\\:mm\\:ss\\.fff}.{Environment.NewLine}Output: {result.OutputPath}",
                isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed: {ex.Message}", isError: true);
        }
        finally
        {
            SetBusy(false);
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

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void SetBusy(bool busy)
    {
        CropButton.IsEnabled = !busy;
        InputPathBox.IsEnabled = !busy;
        OutputPathBox.IsEnabled = !busy;
        StartBox.IsEnabled = !busy;
        StopBox.IsEnabled = !busy;
        KeepMetadataCheck.IsEnabled = !busy;
        Progress.IsIndeterminate = busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError ? Brushes.Firebrick : Brushes.Black;
    }
}
