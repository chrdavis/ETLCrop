namespace ETWCrop;

/// <summary>
/// Progress information reported while a crop operation runs.
/// </summary>
/// <param name="EventsRead">Number of events read from the input trace so far.</param>
/// <param name="EventsWritten">Number of events written to the output trace so far.</param>
/// <param name="CurrentRelativeMSec">
/// Timestamp of the most recently read event, in milliseconds relative to the trace start. Because
/// events are processed in time order, this advances monotonically and tracks how far through the
/// trace the crop has progressed.
/// </param>
/// <param name="TotalDurationMSec">
/// Total length of the input trace, in milliseconds, or zero when it is unknown. When greater than
/// zero, <see cref="PercentComplete"/> reports completion as a percentage.
/// </param>
public readonly record struct EtlCropProgress(
    long EventsRead,
    long EventsWritten,
    double CurrentRelativeMSec,
    double TotalDurationMSec)
{
    /// <summary>
    /// Gets completion as a percentage in the range 0-100 based on the current event time relative
    /// to the total trace duration, or <see langword="null"/> when the duration is unknown.
    /// </summary>
    public double? PercentComplete => TotalDurationMSec > 0
        ? Math.Clamp(CurrentRelativeMSec / TotalDurationMSec * 100.0, 0.0, 100.0)
        : null;
}
