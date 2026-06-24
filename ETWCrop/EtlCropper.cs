using System.Diagnostics;
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
        Stopwatch stopwatch = Stopwatch.StartNew();

        using (var relogger = new ETWReloggerTraceEventSource(fullInput, fullOutput))
        {
            // Registering the kernel parser ensures stack/process/thread/image events are surfaced
            // as their strongly-typed forms to AllEvents so the filter below can recognise them.
            var kernel = new KernelTraceEventParser(relogger);
            RegisterKernelTemplates(kernel);

            relogger.AllEvents += data =>
            {
                eventsRead++;

                bool isMetadata = data is ProcessTraceData or ThreadTraceData or ImageLoadTraceData;
                double effective = GetEffectiveTimeRelativeMSec(data);
                bool inWindow = effective >= options.StartTimeRelativeMSec
                    && effective <= options.StopTimeRelativeMSec;

                if (EtlCropFilter.ShouldInclude(effective, isMetadata, options))
                {
                    relogger.WriteEvent(data);
                    eventsWritten++;

                    if (isMetadata && !inWindow)
                    {
                        metadataKept++;
                    }
                }

                if (progress is not null && eventsRead % ProgressInterval == 0)
                {
                    progress.Report(new EtlCropProgress(eventsRead, eventsWritten));
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    relogger.StopProcessing();
                }
            };

            relogger.Process();
        }

        stopwatch.Stop();
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new EtlCropProgress(eventsRead, eventsWritten));

        return new EtlCropResult
        {
            InputPath = fullInput,
            OutputPath = fullOutput,
            EventsRead = eventsRead,
            EventsWritten = eventsWritten,
            MetadataEventsKept = metadataKept,
            Elapsed = stopwatch.Elapsed,
            StartTimeRelativeMSec = options.StartTimeRelativeMSec,
            StopTimeRelativeMSec = options.StopTimeRelativeMSec,
        };
    }

    /// <summary>
    /// Gets the time (relative to the session start, in milliseconds) that should be used when
    /// deciding whether to keep an event.
    /// </summary>
    private static double GetEffectiveTimeRelativeMSec(TraceEvent data)
    {
        if (data is StackWalkStackTraceData stack)
        {
            // A stack-walk event is logged slightly after the event it describes. Use the owning
            // event's timestamp so a stack is kept exactly when its event is kept.
            return stack.EventTimeStampRelativeMSec;
        }

        return data.TimeStampRelativeMSec;
    }

    /// <summary>
    /// Subscribes empty handlers so the kernel event templates are registered with the dispatcher.
    /// The events are still inspected (and written) through AllEvents; these handlers only ensure
    /// the strongly-typed event objects are produced.
    /// </summary>
    private static void RegisterKernelTemplates(KernelTraceEventParser kernel)
    {
        kernel.StackWalkStack += static _ => { };

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
    }
}
