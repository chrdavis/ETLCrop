using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ETLCrop.App;

/// <summary>
/// A horizontal slider with two thumbs that select a lower and upper value within
/// <c>[0, Maximum]</c>. WPF has no built-in range slider, so this lightweight control provides one
/// for choosing a crop window's start and stop times.
/// </summary>
public partial class RangeSlider : UserControl
{
    private const double ThumbWidth = 12.0;

    public RangeSlider()
    {
        InitializeComponent();
    }

    /// <summary>Raised whenever <see cref="LowerValue"/> or <see cref="UpperValue"/> changes.</summary>
    public event EventHandler? RangeChanged;

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(RangeSlider),
        new PropertyMetadata(0.0, OnRangePropertyChanged));

    public static readonly DependencyProperty LowerValueProperty = DependencyProperty.Register(
        nameof(LowerValue), typeof(double), typeof(RangeSlider),
        new PropertyMetadata(0.0, OnRangePropertyChanged));

    public static readonly DependencyProperty UpperValueProperty = DependencyProperty.Register(
        nameof(UpperValue), typeof(double), typeof(RangeSlider),
        new PropertyMetadata(0.0, OnRangePropertyChanged));

    /// <summary>Gets or sets the inclusive maximum of the range (the minimum is always zero).</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Gets or sets the lower (start) value, clamped to <c>[0, UpperValue]</c>.</summary>
    public double LowerValue
    {
        get => (double)GetValue(LowerValueProperty);
        set => SetValue(LowerValueProperty, value);
    }

    /// <summary>Gets or sets the upper (stop) value, clamped to <c>[LowerValue, Maximum]</c>.</summary>
    public double UpperValue
    {
        get => (double)GetValue(UpperValueProperty);
        set => SetValue(UpperValueProperty, value);
    }

    private static void OnRangePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slider = (RangeSlider)d;

        double max = Math.Max(0, slider.Maximum);

        // Clamp each value into [0, max]. The lower<=upper relationship is intentionally NOT enforced
        // here by dragging the sibling thumb: the two values are driven independently (each by its own
        // text box), so coercing one when the other changes would leave a text box out of sync with
        // its thumb while the user is still typing. Ordering is enforced while dragging a thumb (see
        // the DragDelta handlers), and an invalid window is rejected by EtlCropOptions.Validate()
        // before a crop runs.
        double lower = Math.Clamp(slider.LowerValue, 0, max);
        if (lower != slider.LowerValue)
        {
            slider.LowerValue = lower;
            return; // Re-enters and finishes via the LowerValue change.
        }

        double upper = Math.Clamp(slider.UpperValue, 0, max);
        if (upper != slider.UpperValue)
        {
            slider.UpperValue = upper;
            return; // Re-enters and finishes via the UpperValue change.
        }

        slider.UpdateThumbPositions();

        if (e.Property != MaximumProperty)
        {
            slider.RangeChanged?.Invoke(slider, EventArgs.Empty);
        }
    }

    private void RangeSlider_Loaded(object sender, RoutedEventArgs e) => UpdateThumbPositions();

    private void TrackCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateThumbPositions();

    private double TrackWidth => Math.Max(0, TrackCanvas.ActualWidth - ThumbWidth);

    private double ValueToPosition(double value)
    {
        double max = Math.Max(0, Maximum);
        double fraction = max > 0 ? Math.Clamp(value / max, 0, 1) : 0;
        return fraction * TrackWidth;
    }

    private double PositionToValue(double position)
    {
        double max = Math.Max(0, Maximum);
        double width = TrackWidth;
        double fraction = width > 0 ? Math.Clamp(position / width, 0, 1) : 0;
        return fraction * max;
    }

    private void UpdateThumbPositions()
    {
        if (TrackCanvas is null)
        {
            return;
        }

        Canvas.SetLeft(LowerThumb, ValueToPosition(LowerValue));
        Canvas.SetLeft(UpperThumb, ValueToPosition(UpperValue));

        double left = ValueToPosition(LowerValue) + (ThumbWidth / 2);
        double right = ValueToPosition(UpperValue) + (ThumbWidth / 2);
        Canvas.SetLeft(SelectedRange, left);
        SelectedRange.Width = Math.Max(0, right - left);
    }

    private void LowerThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double position = ValueToPosition(LowerValue) + e.HorizontalChange;
        LowerValue = Math.Min(PositionToValue(position), UpperValue);
    }

    private void UpperThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double position = ValueToPosition(UpperValue) + e.HorizontalChange;
        UpperValue = Math.Max(PositionToValue(position), LowerValue);
    }
}
