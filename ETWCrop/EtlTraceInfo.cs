namespace ETWCrop;

/// <summary>
/// Lightweight summary of an ETL trace's timing, read from the trace header without a full pass
/// over its events.
/// </summary>
/// <param name="DurationMSec">
/// Length of the trace, in milliseconds, from the session start to the last event. This is the
/// largest meaningful value for a crop window's stop time.
/// </param>
/// <param name="SessionStartTime">Wall-clock time at which the trace session started.</param>
/// <param name="SessionEndTime">Wall-clock time at which the trace session ended.</param>
/// <param name="EventsLost">Number of events the original capture reported as lost.</param>
public readonly record struct EtlTraceInfo(
    double DurationMSec,
    DateTime SessionStartTime,
    DateTime SessionEndTime,
    int EventsLost)
{
    /// <summary>
    /// Gets the trace length as a friendly <c>hh:mm:ss.fff</c> string.
    /// </summary>
    public string SessionDurationText() =>
        TimeSpan.FromMilliseconds(DurationMSec).ToString(@"hh\:mm\:ss\.fff");
}
