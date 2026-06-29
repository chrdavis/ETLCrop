namespace ETWCrop;

/// <summary>
/// Summary information about a completed crop operation.
/// </summary>
public sealed class EtlCropResult
{
    /// <summary>The input ETL file that was read.</summary>
    public required string InputPath { get; init; }

    /// <summary>The output ETL file that was written.</summary>
    public required string OutputPath { get; init; }

    /// <summary>Total number of events read from the input trace.</summary>
    public long EventsRead { get; init; }

    /// <summary>Number of events written to the cropped output trace.</summary>
    public long EventsWritten { get; init; }

    /// <summary>
    /// Number of metadata events (process/thread/image) that were preserved outside the
    /// requested time window in order to keep stacks valid.
    /// </summary>
    public long MetadataEventsKept { get; init; }

    /// <summary>
    /// Number of kept rundown events that were re-timed to the stop time so that the cropped
    /// trace's end matches the requested window (see <see cref="EtlCropOptions.ClampKeptEventsToWindow"/>).
    /// </summary>
    public long EventsRetimed { get; init; }

    /// <summary>Wall-clock time taken to perform the crop.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>The start of the applied time window, in milliseconds relative to session start.</summary>
    public double StartTimeRelativeMSec { get; init; }

    /// <summary>The end of the applied time window, in milliseconds relative to session start.</summary>
    public double StopTimeRelativeMSec { get; init; }

    /// <summary>
    /// Number of DPC/ISR events in a rebased output whose embedded "initial time" was left
    /// inconsistent with the shifted event time. This is a post-crop safety check: any non-zero
    /// value indicates a rebased trace that Windows Performance Analyzer would reject with a
    /// catastrophic failure (0x8000FFFF), and is expected to be zero for a correct crop. It is always
    /// zero when rebasing is disabled.
    /// </summary>
    public int EmbeddedTimestampAnomalies { get; init; }

    /// <summary>Number of events that were dropped because they fell outside the time window.</summary>
    public long EventsDropped => EventsRead - EventsWritten;
}
