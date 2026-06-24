namespace ETWCrop;

/// <summary>
/// Progress information reported while a crop operation runs.
/// </summary>
/// <param name="EventsRead">Number of events read from the input trace so far.</param>
/// <param name="EventsWritten">Number of events written to the output trace so far.</param>
public readonly record struct EtlCropProgress(long EventsRead, long EventsWritten);
