using System.Globalization;
using ETLCrop;
using ETLCrop.Cli;

CliArguments parsed = CliArguments.Parse(args);

if (parsed.ShowHelp)
{
    PrintHelp();
    return 0;
}

if (parsed.HasErrors)
{
    foreach (string error in parsed.Errors)
    {
        Console.Error.WriteLine($"error: {error}");
    }

    Console.Error.WriteLine();
    PrintHelp();
    return 1;
}

try
{
    var cropper = new EtlCropper();
    EtlCropOptions options = parsed.ToCropOptions();

    Console.WriteLine($"Cropping '{parsed.InputPath}' -> '{parsed.OutputPath}'");
    Console.WriteLine($"  window : {FormatTime(options.StartTimeRelativeMSec)} .. {FormatTime(options.StopTimeRelativeMSec)} (relative to trace start)");
    Console.WriteLine($"  keep process/module metadata : {options.KeepMetadataEvents}");
    Console.WriteLine($"  end trace at stop time       : {options.ClampKeptEventsToWindow}");
    Console.WriteLine($"  shift start to zero (trim)   : {options.RebaseToWindowStart}");

    var progress = new SynchronousProgress<EtlCropProgress>(p =>
        Console.Write($"\r  {FormatPercent(p.PercentComplete)}read {p.EventsRead:N0} events, wrote {p.EventsWritten:N0} ...   "));

    EtlCropResult result = cropper.Crop(parsed.InputPath!, parsed.OutputPath!, options, progress);

    Console.WriteLine();
    Console.WriteLine("Done.");
    Console.WriteLine($"  events read    : {result.EventsRead:N0}");
    Console.WriteLine($"  events written : {result.EventsWritten:N0}");
    Console.WriteLine($"  events dropped : {result.EventsDropped:N0}");
    Console.WriteLine($"  metadata kept outside window : {result.MetadataEventsKept:N0}");
    Console.WriteLine($"  rundown re-timed to stop     : {result.EventsRetimed:N0}");
    Console.WriteLine($"  elapsed        : {result.Elapsed:hh\\:mm\\:ss\\.fff}");
    Console.WriteLine($"  output         : {result.OutputPath}");

    if (result.EmbeddedTimestampAnomalies > 0)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"WARNING: {result.EmbeddedTimestampAnomalies:N0} DPC/ISR events in the rebased output have");
        Console.Error.WriteLine("inconsistent embedded timestamps. Windows Performance Analyzer will reject this");
        Console.Error.WriteLine("trace with a catastrophic failure (0x8000FFFF). This usually means a stale build -");
        Console.Error.WriteLine("rebuild ETLCrop and re-run the crop. If it persists, report this output.");
        return 3;
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}

static string FormatTime(double ms) =>
    double.IsPositiveInfinity(ms) ? "end" : string.Create(CultureInfo.InvariantCulture, $"{ms:N3} ms");

static string FormatPercent(double? percent) =>
    percent is { } p ? string.Create(CultureInfo.InvariantCulture, $"{p,5:N1}%  ") : string.Empty;

static void PrintHelp()
{
    Console.WriteLine("etlcrop - crop an ETL trace file to a start/stop time window.");
    Console.WriteLine();
    Console.WriteLine("USAGE:");
    Console.WriteLine("  etlcrop --input <in.etl> --output <out.etl> [--start <ms>] [--stop <ms>] [--no-metadata]");
    Console.WriteLine("  etlcrop <in.etl> <out.etl> [options]");
    Console.WriteLine();
    Console.WriteLine("OPTIONS:");
    Console.WriteLine("  -i, --input <path>    Source ETL file to crop (required).");
    Console.WriteLine("  -o, --output <path>   Destination ETL file to create (required).");
    Console.WriteLine("  -s, --start <ms>      Window start in milliseconds relative to trace start. Default: 0.");
    Console.WriteLine("  -e, --stop <ms>       Window stop in milliseconds relative to trace start. Default: end of trace.");
    Console.WriteLine("      --keep-metadata   Keep process/thread/module events so stacks stay valid (default).");
    Console.WriteLine("      --no-metadata     Drop process/thread/module events outside the window.");
    Console.WriteLine("      --clamp           End the cropped trace at the stop time by re-timing end rundown (default).");
    Console.WriteLine("      --no-clamp        Leave end-of-trace rundown at its original time (trace keeps original length).");
    Console.WriteLine("      --rebase          Trim leading time by shifting the window to start at zero (WPA-compatible).");
    Console.WriteLine("      --no-rebase       Keep original absolute timestamps with a leading gap (default).");
    Console.WriteLine("  -h, --help            Show this help.");
    Console.WriteLine();
    Console.WriteLine("Times are milliseconds relative to the start of the trace, matching PerfView's timeline.");
}
