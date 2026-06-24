using System.Globalization;
using ETWCrop;

namespace ETWCrop.Cli;

/// <summary>
/// Parsed command-line arguments for the <c>etlcrop</c> tool.
/// </summary>
/// <remarks>
/// Parsing is implemented here (rather than inline in <c>Program</c>) so it can be unit tested.
/// </remarks>
public sealed class CliArguments
{
    /// <summary>Path to the source ETL file to crop.</summary>
    public string? InputPath { get; set; }

    /// <summary>Path of the cropped ETL file to produce.</summary>
    public string? OutputPath { get; set; }

    /// <summary>Window start, in milliseconds relative to the trace session start.</summary>
    public double StartMSec { get; set; }

    /// <summary>Window stop, in milliseconds relative to the trace session start.</summary>
    public double StopMSec { get; set; } = double.PositiveInfinity;

    /// <summary>Whether process/thread/image metadata is preserved to keep stacks valid.</summary>
    public bool KeepMetadata { get; set; } = true;

    /// <summary>Whether usage/help was requested or no arguments were supplied.</summary>
    public bool ShowHelp { get; set; }

    /// <summary>Accumulated parsing/validation errors.</summary>
    public List<string> Errors { get; } = new();

    /// <summary>Gets a value indicating whether any errors were recorded.</summary>
    public bool HasErrors => Errors.Count > 0;

    /// <summary>Parses raw command-line arguments into a <see cref="CliArguments"/> instance.</summary>
    /// <param name="args">The raw arguments (typically from <c>Main</c>).</param>
    public static CliArguments Parse(string[] args)
    {
        var result = new CliArguments();

        if (args is null || args.Length == 0)
        {
            result.ShowHelp = true;
            return result;
        }

        var positionals = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                case "/?":
                    result.ShowHelp = true;
                    return result;

                case "-i":
                case "--input":
                    result.InputPath = TakeValue(args, ref i, arg, result);
                    break;

                case "-o":
                case "--output":
                    result.OutputPath = TakeValue(args, ref i, arg, result);
                    break;

                case "-s":
                case "--start":
                    result.StartMSec = ParseTime(TakeValue(args, ref i, arg, result), arg, result, 0);
                    break;

                case "-e":
                case "--stop":
                case "--end":
                    result.StopMSec = ParseTime(TakeValue(args, ref i, arg, result), arg, result, double.PositiveInfinity);
                    break;

                case "--keep-metadata":
                    result.KeepMetadata = true;
                    break;

                case "--no-metadata":
                    result.KeepMetadata = false;
                    break;

                default:
                    if (arg.StartsWith('-'))
                    {
                        result.Errors.Add($"Unknown option '{arg}'.");
                    }
                    else
                    {
                        positionals.Add(arg);
                    }

                    break;
            }
        }

        // Positional fallback: first bare token is the input, second is the output.
        if (result.InputPath is null && positionals.Count > 0)
        {
            result.InputPath = positionals[0];
            positionals.RemoveAt(0);
        }

        if (result.OutputPath is null && positionals.Count > 0)
        {
            result.OutputPath = positionals[0];
            positionals.RemoveAt(0);
        }

        foreach (string extra in positionals)
        {
            result.Errors.Add($"Unexpected argument '{extra}'.");
        }

        result.Validate();
        return result;
    }

    /// <summary>Adds errors for any missing or inconsistent values. No-op when help is requested.</summary>
    public void Validate()
    {
        if (ShowHelp)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(InputPath))
        {
            Errors.Add("Missing required input file. Use --input <path>.");
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            Errors.Add("Missing required output file. Use --output <path>.");
        }

        if (StartMSec < 0)
        {
            Errors.Add("--start cannot be negative.");
        }

        if (!double.IsNaN(StartMSec) && !double.IsNaN(StopMSec) && StartMSec > StopMSec)
        {
            Errors.Add($"--start ({StartMSec}) cannot be greater than --stop ({StopMSec}).");
        }
    }

    /// <summary>Projects the parsed arguments onto a library <see cref="EtlCropOptions"/>.</summary>
    public EtlCropOptions ToCropOptions() => new()
    {
        StartTimeRelativeMSec = StartMSec,
        StopTimeRelativeMSec = StopMSec,
        KeepMetadataEvents = KeepMetadata,
    };

    private static string? TakeValue(string[] args, ref int i, string option, CliArguments result)
    {
        if (i + 1 >= args.Length)
        {
            result.Errors.Add($"Option '{option}' requires a value.");
            return null;
        }

        return args[++i];
    }

    private static double ParseTime(string? value, string option, CliArguments result, double fallback)
    {
        if (value is null)
        {
            return fallback;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            return parsed;
        }

        result.Errors.Add($"Option '{option}' requires a numeric value in milliseconds, but got '{value}'.");
        return fallback;
    }
}
