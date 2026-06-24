using System.Globalization;
using ETWCrop;
using ETWCrop.Cli;

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

    var progress = new SynchronousProgress<EtlCropProgress>(p =>
        Console.Write($"\r  read {p.EventsRead:N0} events, wrote {p.EventsWritten:N0} ...   "));

    EtlCropResult result = cropper.Crop(parsed.InputPath!, parsed.OutputPath!, options, progress);

    Console.WriteLine();
    Console.WriteLine("Done.");
    Console.WriteLine($"  events read    : {result.EventsRead:N0}");
    Console.WriteLine($"  events written : {result.EventsWritten:N0}");
    Console.WriteLine($"  events dropped : {result.EventsDropped:N0}");
    Console.WriteLine($"  metadata kept outside window : {result.MetadataEventsKept:N0}");
    Console.WriteLine($"  elapsed        : {result.Elapsed:hh\\:mm\\:ss\\.fff}");
    Console.WriteLine($"  output         : {result.OutputPath}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}

static string FormatTime(double ms) =>
    double.IsPositiveInfinity(ms) ? "end" : string.Create(CultureInfo.InvariantCulture, $"{ms:N3} ms");

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
    Console.WriteLine("  -h, --help            Show this help.");
    Console.WriteLine();
    Console.WriteLine("Times are milliseconds relative to the start of the trace, matching PerfView's timeline.");
}
