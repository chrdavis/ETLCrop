namespace ETWCrop;

/// <summary>
/// Options that control how an ETL trace file is cropped to a time window.
/// </summary>
/// <remarks>
/// Times are expressed as milliseconds relative to the start of the trace session
/// (the same convention PerfView uses). The default window keeps the whole trace.
/// </remarks>
public sealed class EtlCropOptions
{
    /// <summary>
    /// Inclusive start of the time window, in milliseconds relative to the session start.
    /// Events that occur before this time are dropped (except always-kept metadata events).
    /// </summary>
    public double StartTimeRelativeMSec { get; set; }

    /// <summary>
    /// Inclusive end of the time window, in milliseconds relative to the session start.
    /// Events that occur after this time are dropped (except always-kept metadata events).
    /// Defaults to <see cref="double.PositiveInfinity"/> meaning "until the end of the trace".
    /// </summary>
    public double StopTimeRelativeMSec { get; set; } = double.PositiveInfinity;

    /// <summary>
    /// When <see langword="true"/> (the default), process, thread and image (module) events —
    /// including their rundown (DCStart/DCStop) variants — are always copied to the output
    /// regardless of the time window. These are required so that CPU and memory stacks in the
    /// cropped trace can still be attributed to the correct process/module and symbolicated.
    /// </summary>
    public bool KeepMetadataEvents { get; set; } = true;

    /// <summary>
    /// Validates the option values and throws if they are inconsistent.
    /// </summary>
    /// <exception cref="ArgumentException">The start time is greater than the stop time.</exception>
    public void Validate()
    {
        if (double.IsNaN(StartTimeRelativeMSec) || double.IsNaN(StopTimeRelativeMSec))
        {
            throw new ArgumentException("Start and stop times must be valid numbers.");
        }

        if (StartTimeRelativeMSec < 0)
        {
            throw new ArgumentException("Start time cannot be negative.", nameof(StartTimeRelativeMSec));
        }

        if (StartTimeRelativeMSec > StopTimeRelativeMSec)
        {
            throw new ArgumentException(
                $"Start time ({StartTimeRelativeMSec} ms) cannot be greater than stop time ({StopTimeRelativeMSec} ms).");
        }
    }
}
