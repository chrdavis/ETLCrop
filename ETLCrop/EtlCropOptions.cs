namespace ETLCrop;

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
    /// including their rundown (DCStart/DCStop) variants — stack-key definition/rundown events, and
    /// the one-time trace-setup events at the start of the trace (the kernel EventTrace header group,
    /// SystemConfig records, and the CPU sample interval) are always copied to the output regardless
    /// of the time window. These are required so that CPU and memory stacks in the cropped trace can
    /// still be attributed to the correct process/module, symbolicated, have their compressed stack
    /// keys resolved, and — importantly — be associated with their samples by Windows Performance
    /// Analyzer, whose stack analysis depends on those trace-setup records even when the crop window
    /// begins after them.
    /// </summary>
    public bool KeepMetadataEvents { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/> (the default) and a finite <see cref="StopTimeRelativeMSec"/> is
    /// set, kept rundown events that occur after the stop time (for example the end-of-trace module,
    /// process/thread and stack-key rundown) are re-timed to the stop time before being written.
    /// </summary>
    /// <remarks>
    /// The output ETL header's end time follows the latest event written. Without this, the kept
    /// end-of-trace rundown keeps the cropped trace as long as the original; with it, the cropped
    /// trace ends at the requested stop time while still carrying the rundown needed for valid
    /// stacks and symbols. The trace start time remains the original session start.
    /// </remarks>
    public bool ClampKeptEventsToWindow { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, the cropped trace is shifted so that the requested
    /// <see cref="StartTimeRelativeMSec"/> becomes time zero, removing the empty leading time before
    /// the window. Every kept event's timestamp is moved earlier by the start offset, and any kernel
    /// events that embed an absolute timestamp in their payload (stack walks, DPCs, ISRs, memory hard
    /// faults, registry operations and last-branch samples) have that embedded value shifted to match.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <see langword="false"/>: cropping normally preserves the original absolute
    /// timestamps, so the window keeps its place on the session timeline (with a leading gap that can
    /// simply be zoomed past) and stays correlatable with wall-clock time and other logs. Enable this
    /// only when you specifically want the window to start at time zero.
    /// </para>
    /// <para>
    /// Because both the event timestamps and the embedded absolute timestamps are shifted together,
    /// the rebased trace stays internally consistent and opens correctly in Windows Performance
    /// Analyzer, with CPU and memory stacks intact.
    /// </para>
    /// <para>When enabled it subsumes <see cref="ClampKeptEventsToWindow"/> (it also trims the end).</para>
    /// </remarks>
    public bool RebaseToWindowStart { get; set; }

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
