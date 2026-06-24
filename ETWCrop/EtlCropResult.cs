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

    /// <summary>Wall-clock time taken to perform the crop.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>The start of the applied time window, in milliseconds relative to session start.</summary>
    public double StartTimeRelativeMSec { get; init; }

    /// <summary>The end of the applied time window, in milliseconds relative to session start.</summary>
    public double StopTimeRelativeMSec { get; init; }

    /// <summary>Number of events that were dropped because they fell outside the time window.</summary>
    public long EventsDropped => EventsRead - EventsWritten;
}
