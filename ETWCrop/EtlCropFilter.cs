namespace ETWCrop;

/// <summary>
/// Pure, side-effect-free decision logic that determines whether a single trace event
/// should be copied into the cropped output trace.
/// </summary>
/// <remarks>
/// This logic is separated from <see cref="EtlCropper"/> (which drives the ETW relogger)
/// so that it can be unit tested without needing a real ETL file or live ETW session.
/// </remarks>
public static class EtlCropFilter
{
    /// <summary>
    /// Determines whether an event should be written to the cropped output trace.
    /// </summary>
    /// <param name="effectiveTimeRelativeMSec">
    /// The time of the event, in milliseconds relative to the session start. For stack-walk
    /// events this should be the timestamp of the event the stack belongs to, so that a stack
    /// is kept exactly when its owning event is kept.
    /// </param>
    /// <param name="isMetadataEvent">
    /// <see langword="true"/> if the event describes a process, thread or image (module) — these
    /// are needed so stacks in the cropped trace can be attributed and symbolicated.
    /// </param>
    /// <param name="options">The crop options describing the time window and metadata behaviour.</param>
    /// <returns><see langword="true"/> if the event should be copied to the output.</returns>
    public static bool ShouldInclude(double effectiveTimeRelativeMSec, bool isMetadataEvent, EtlCropOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Always keep process/thread/image metadata so cropped stacks remain valid, even when
        // these events were emitted (e.g. as end-of-trace rundown) outside the requested window.
        if (isMetadataEvent && options.KeepMetadataEvents)
        {
            return true;
        }

        return effectiveTimeRelativeMSec >= options.StartTimeRelativeMSec
            && effectiveTimeRelativeMSec <= options.StopTimeRelativeMSec;
    }
}
