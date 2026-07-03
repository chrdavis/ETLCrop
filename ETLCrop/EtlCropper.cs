using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace ETWCrop;

/// <summary>
/// Crops an existing ETL trace file to a time window, producing a new, valid ETL file.
/// </summary>
/// <remarks>
/// <para>
/// Cropping is implemented with <see cref="ETWReloggerTraceEventSource"/>, which re-logs the raw
/// events from the input trace into a new trace. Because the raw events (including the kernel
/// stack-walk events that back CPU and memory stacks) are copied verbatim, stacks remain valid in
/// the output as long as the event each stack refers to is also copied.
/// </para>
/// <para>
/// Reading and re-logging an existing ETL file does not require administrative privileges;
/// elevation is only needed to control live ETW sessions.
/// </para>
/// </remarks>
public sealed class EtlCropper
{
    private const int ProgressInterval = 50_000;

    // HRESULT_FROM_WIN32(ERROR_CANCELLED): the native relogger raises this from Process() when it is
    // stopped via StopProcessing(), which is how a cancellation surfaces from the COM layer.
    private const int ErrorCancelledHResult = unchecked((int)0x800704C7);

    // Trace-setup rundown that WPA's CPU-sample stack association needs even when the crop window
    // starts after these events occur (they are emitted once at the very start of the trace):
    // the kernel EventTrace header group and the SystemConfig (machine configuration) group.
    private static readonly Guid EventTraceTaskGuid = new("68fdd900-4a3e-11d1-84f4-0000f80464e3");
    private static readonly Guid SystemConfigTaskGuid = new("01853a65-418f-4f36-aefc-dc0f1d2fd235");

    /// <summary>
    /// Reads an ETL trace's timing from its header without processing all of its events. This is
    /// fast (milliseconds even for multi-gigabyte traces) and is intended for sizing a crop window
    /// before running the crop.
    /// </summary>
    /// <param name="inputPath">Path to the existing ETL file to inspect.</param>
    /// <returns>The trace's duration and session times.</returns>
    /// <exception cref="FileNotFoundException">The input file does not exist.</exception>
    public static EtlTraceInfo ReadTraceInfo(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"Input ETL file not found: {inputPath}", inputPath);
        }

        // The session times and duration come from the trace header and are populated as soon as the
        // source is opened, so no call to Process() (a full pass) is needed here.
        using var source = new ETWTraceEventSource(Path.GetFullPath(inputPath));
        return new EtlTraceInfo(
            DurationMSec: source.SessionEndTimeRelativeMSec,
            SessionStartTime: source.SessionStartTime,
            SessionEndTime: source.SessionEndTime,
            EventsLost: source.EventsLost);
    }

    /// <summary>
    /// Reads just the trace duration for progress reporting, returning zero if it cannot be read so
    /// the crop still proceeds (progress simply falls back to an indeterminate state).
    /// </summary>
    private static double TryReadDurationMSec(string inputPath)
    {
        try
        {
            using var source = new ETWTraceEventSource(inputPath);
            return source.SessionEndTimeRelativeMSec;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Crops <paramref name="inputPath"/> to the window described by <paramref name="options"/>
    /// and writes the result to <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="inputPath">Path to the existing ETL file to crop.</param>
    /// <param name="outputPath">Path of the cropped ETL file to create. Must differ from the input.</param>
    /// <param name="options">The time window and metadata-handling options.</param>
    /// <param name="progress">Optional receiver for periodic progress updates.</param>
    /// <param name="cancellationToken">Token used to cancel a long-running crop.</param>
    /// <returns>A summary of the crop operation.</returns>
    public EtlCropResult Crop(
        string inputPath,
        string outputPath,
        EtlCropOptions options,
        IProgress<EtlCropProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException($"Input ETL file not found: {inputPath}", inputPath);
        }

        string fullInput = Path.GetFullPath(inputPath);
        string fullOutput = Path.GetFullPath(outputPath);

        if (string.Equals(fullInput, fullOutput, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The output path must be different from the input path.", nameof(outputPath));
        }

        string? outputDirectory = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        long eventsRead = 0;
        long eventsWritten = 0;
        long metadataKept = 0;
        long eventsRetimed = 0;
        double currentRelativeMSec = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();

        // The total trace duration (read cheaply from the header) lets progress consumers report a
        // determinate percentage; the relogger source itself does not expose it until processed.
        double totalDurationMSec = TryReadDurationMSec(fullInput);

        bool rebaseEnabled;
        using (var relogger = new ETWReloggerTraceEventSource(fullInput, fullOutput))
        {
            // Registering the kernel parser ensures stack/process/thread/image events are surfaced
            // as their strongly-typed forms to AllEvents so the filter below can recognise them.
            var kernel = new KernelTraceEventParser(relogger);
            RegisterKernelTemplates(kernel);

            var retimer = new ReloggerRetimer(relogger);

            // Rebase shifts the whole window so it starts at time zero (trimming the leading gap); it
            // also trims the end, so it supersedes the end-only clamp. Only meaningful when there is a
            // positive start offset to remove and the relogger internals needed for rebasing exist.
            rebaseEnabled = options.RebaseToWindowStart
                && options.StartTimeRelativeMSec > 0
                && retimer.CanRebase;
            double rebaseWidthMSec = double.IsPositiveInfinity(options.StopTimeRelativeMSec)
                ? double.PositiveInfinity
                : options.StopTimeRelativeMSec - options.StartTimeRelativeMSec;

            bool clampEnabled = !rebaseEnabled
                && options.ClampKeptEventsToWindow
                && !double.IsPositiveInfinity(options.StopTimeRelativeMSec);

            relogger.AllEvents += data =>
            {
                eventsRead++;

                // Always-kept events: process/thread/image rundown and stack-key definitions (so
                // stacks stay valid and resolvable), plus the one-time trace-setup events at the
                // start of the trace (EventTrace header group, SystemConfig, and the CPU sample
                // interval) which WPA requires to associate sampled-profile stacks.
                bool isMetadata = data is ProcessTraceData or ThreadTraceData or ImageLoadTraceData
                    or StackWalkDefTraceData or SampledProfileIntervalTraceData
                    || data.TaskGuid == EventTraceTaskGuid
                    || data.TaskGuid == SystemConfigTaskGuid;
                double effective = GetEffectiveTimeRelativeMSec(data);
                bool inWindow = effective >= options.StartTimeRelativeMSec
                    && effective <= options.StopTimeRelativeMSec;

                currentRelativeMSec = data.TimeStampRelativeMSec;

                if (EtlCropFilter.ShouldInclude(effective, isMetadata, options))
                {
                    if (rebaseEnabled)
                    {
                        // Shift this event so the window start becomes time zero. Events that embed an
                        // absolute timestamp in their payload also need that value shifted to stay
                        // consistent (otherwise analysers such as WPA reject the trace).
                        bool hasEmbeddedTimestamp = HasEmbeddedAbsoluteTimestamp(data);
                        if (retimer.TryRebaseCurrentEvent(data, options.StartTimeRelativeMSec, rebaseWidthMSec, hasEmbeddedTimestamp))
                        {
                            eventsRetimed++;
                        }
                    }
                    else if (clampEnabled && isMetadata && effective > options.StopTimeRelativeMSec
                        && retimer.TrySetCurrentEventTime(data, options.StopTimeRelativeMSec))
                    {
                        // Pull kept end-of-trace rundown back to the stop time so the cropped trace
                        // ends at the requested window. The output header end follows the last event.
                        eventsRetimed++;
                    }

                    relogger.WriteEvent(data);
                    eventsWritten++;

                    if (isMetadata && !inWindow)
                    {
                        metadataKept++;
                    }
                }

                if (progress is not null && eventsRead % ProgressInterval == 0)
                {
                    progress.Report(new EtlCropProgress(eventsRead, eventsWritten, currentRelativeMSec, totalDurationMSec));
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    relogger.StopProcessing();
                }
            };

            try
            {
                relogger.Process();
            }
            catch (COMException ex)
            {
                // StopProcessing() aborts the native relog by raising ERROR_CANCELLED from Process().
                // Translate that into the standard cancellation exception so callers see a clean cancel
                // rather than a COM failure. A plain (unfiltered) catch is used deliberately: with an
                // exception filter ("when"), the debugger's Just My Code does not treat the catch as a
                // definite handler and still breaks on the COM exception as "unhandled in user code".
                if (ex.HResult == ErrorCancelledHResult && cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                throw;
            }
        }

        stopwatch.Stop();
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new EtlCropProgress(eventsRead, eventsWritten, currentRelativeMSec, totalDurationMSec));

        // Safety net: a rebased trace must keep its embedded absolute timestamps consistent with the
        // shifted event times, or Windows Performance Analyzer aborts with a catastrophic failure.
        // Re-read the (trimmed, small) output and confirm no DPC/ISR event was left inconsistent so a
        // bad rebased crop - for example one produced by a stale build - is reported, not shipped
        // silently. Verification is best-effort and never fails the crop.
        int embeddedTimestampAnomalies = rebaseEnabled
            ? CountInconsistentEmbeddedTimestamps(fullOutput)
            : 0;

        return new EtlCropResult
        {
            InputPath = fullInput,
            OutputPath = fullOutput,
            EventsRead = eventsRead,
            EventsWritten = eventsWritten,
            MetadataEventsKept = metadataKept,
            EventsRetimed = eventsRetimed,
            Elapsed = stopwatch.Elapsed,
            StartTimeRelativeMSec = options.StartTimeRelativeMSec,
            StopTimeRelativeMSec = options.StopTimeRelativeMSec,
            EmbeddedTimestampAnomalies = embeddedTimestampAnomalies,
        };
    }

    /// <summary>
    /// Gets the time (relative to the session start, in milliseconds) that should be used when
    /// deciding whether to keep an event.
    /// </summary>
    private static double GetEffectiveTimeRelativeMSec(TraceEvent data)
    {
        // A stack-walk event is logged just after the event it describes. Use the owning event's
        // timestamp so a stack - whether a full inline stack or a compressed stack-key reference -
        // is kept exactly when its event is kept. (Stack-key definitions are kept unconditionally
        // and are handled by the metadata path, not here.)
        return data switch
        {
            StackWalkStackTraceData stack => stack.EventTimeStampRelativeMSec,
            StackWalkRefTraceData stackRef => stackRef.EventTimeStampRelativeMSec,
            _ => data.TimeStampRelativeMSec,
        };
    }

    /// <summary>
    /// Gets a value indicating whether <paramref name="data"/> embeds an absolute QPC timestamp in
    /// the first eight bytes of its payload (in addition to the event's own timestamp). Such events
    /// must have that embedded value shifted when the trace is rebased, or the two timestamps will
    /// disagree and analysers such as WPA will reject the trace.
    /// </summary>
    private static bool HasEmbeddedAbsoluteTimestamp(TraceEvent data) => data switch
    {
        // Stack-walk events embed the timestamp of the event they describe.
        StackWalkStackTraceData or StackWalkRefTraceData => true,
        // DPC/ISR embed the "initial time" the routine started; the memory hard fault embeds the
        // time the fault began; the last-branch sample embeds its capture time.
        DPCTraceData or ISRTraceData or MemoryHardFaultTraceData or LastBranchRecordTraceData => true,
        // Registry operations embed their start time, but only from version 2 of the event onward.
        RegistryTraceData registry => registry.Version >= 2,
        _ => false,
    };

    // A DPC/ISR's elapsed time (event time minus its embedded initial time) is a real hardware
    // interval: never negative and, in practice, well under a second. A value outside this range in a
    // rebased trace means the embedded initial time was not shifted with the event - the exact
    // inconsistency that makes WPA abort - so we treat it as an anomaly.
    private const double MinPlausibleElapsedMSec = -1.0;
    private const double MaxPlausibleElapsedMSec = 1000.0;

    /// <summary>
    /// Re-reads a rebased output trace and counts DPC/ISR events whose embedded "initial time" is no
    /// longer consistent with the (shifted) event time. A non-zero count means the rebased trace is
    /// the kind that Windows Performance Analyzer rejects with a catastrophic failure (0x8000FFFF).
    /// This is a best-effort safety net; any failure to read simply reports zero.
    /// </summary>
    private static int CountInconsistentEmbeddedTimestamps(string tracePath)
    {
        try
        {
            int anomalies = 0;
            using var source = new ETWTraceEventSource(tracePath);
            var kernel = source.Kernel;
            void Check(double elapsedMSec)
            {
                if (elapsedMSec < MinPlausibleElapsedMSec || elapsedMSec > MaxPlausibleElapsedMSec)
                {
                    anomalies++;
                }
            }

            kernel.PerfInfoDPC += d => Check(d.ElapsedTimeMSec);
            kernel.PerfInfoThreadedDPC += d => Check(d.ElapsedTimeMSec);
            kernel.PerfInfoTimerDPC += d => Check(d.ElapsedTimeMSec);
            kernel.PerfInfoISR += d => Check(d.ElapsedTimeMSec);
            source.Process();
            return anomalies;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Subscribes empty handlers so the kernel event templates are registered with the dispatcher.
    /// The events are still inspected (and written) through AllEvents; these handlers only ensure
    /// the strongly-typed event objects are produced.
    /// </summary>
    private static void RegisterKernelTemplates(KernelTraceEventParser kernel)
    {
        // Full inline stacks.
        kernel.StackWalkStack += static _ => { };

        // Compressed (cached) stacks: key references travel with their sample, while the key
        // definitions/rundown map each stack key to its frames. Both must be recognised so the
        // references can be time-filtered and the definitions kept unconditionally.
        kernel.StackWalkStackKeyKernel += static _ => { };
        kernel.StackWalkStackKeyUser += static _ => { };
        kernel.StackWalkKeyDelete += static _ => { };
        kernel.StackWalkKeyRundown += static _ => { };

        kernel.ProcessStart += static _ => { };
        kernel.ProcessStop += static _ => { };
        kernel.ProcessDCStart += static _ => { };
        kernel.ProcessDCStop += static _ => { };

        kernel.ThreadStart += static _ => { };
        kernel.ThreadStop += static _ => { };
        kernel.ThreadDCStart += static _ => { };
        kernel.ThreadDCStop += static _ => { };

        kernel.ImageLoad += static _ => { };
        kernel.ImageUnload += static _ => { };
        kernel.ImageDCStart += static _ => { };
        kernel.ImageDCStop += static _ => { };

        // CPU sampling configuration (sample interval / collection markers), emitted at the trace
        // start; recognised so they can be preserved for WPA's sampled-profile stack association.
        kernel.PerfInfoSetInterval += static _ => { };
        kernel.PerfInfoCollectionStart += static _ => { };
        kernel.PerfInfoCollectionEnd += static _ => { };

        // Events that embed an absolute QPC timestamp in their payload (separate from the event's own
        // timestamp). These must be recognised so that, when rebasing, the embedded value is shifted
        // along with the event - otherwise the two disagree and analysers such as WPA reject the
        // trace. DPC/ISR carry an "initial time", memory hard faults and registry operations carry the
        // time the operation began, and last-branch samples carry a capture time.
        kernel.PerfInfoDPC += static _ => { };
        kernel.PerfInfoThreadedDPC += static _ => { };
        kernel.PerfInfoTimerDPC += static _ => { };
        kernel.PerfInfoISR += static _ => { };
        kernel.MemoryHardFault += static _ => { };
        kernel.LastBranchRecordingSample += static _ => { };

        // Registry events share one payload type across many opcodes; register them in bulk so every
        // registry operation is surfaced strongly-typed.
        kernel.AddCallbackForEvents<RegistryTraceData>(static _ => { });
    }
}
