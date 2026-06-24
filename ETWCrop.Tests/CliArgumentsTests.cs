using ETWCrop;
using ETWCrop.Cli;
using Xunit;

namespace ETWCrop.Tests;

public class CliArgumentsTests
{
    [Fact]
    public void Parse_NoArgs_ShowsHelp()
    {
        CliArguments result = CliArguments.Parse([]);

        Assert.True(result.ShowHelp);
        Assert.False(result.HasErrors);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("/?")]
    public void Parse_HelpFlag_ShowsHelp(string flag)
    {
        CliArguments result = CliArguments.Parse([flag]);

        Assert.True(result.ShowHelp);
    }

    [Fact]
    public void Parse_LongFormInputOutput_IsParsed()
    {
        CliArguments result = CliArguments.Parse(["--input", "in.etl", "--output", "out.etl"]);

        Assert.False(result.HasErrors);
        Assert.Equal("in.etl", result.InputPath);
        Assert.Equal("out.etl", result.OutputPath);
    }

    [Fact]
    public void Parse_ShortFormInputOutput_IsParsed()
    {
        CliArguments result = CliArguments.Parse(["-i", "in.etl", "-o", "out.etl"]);

        Assert.False(result.HasErrors);
        Assert.Equal("in.etl", result.InputPath);
        Assert.Equal("out.etl", result.OutputPath);
    }

    [Fact]
    public void Parse_PositionalInputOutput_IsParsed()
    {
        CliArguments result = CliArguments.Parse(["in.etl", "out.etl"]);

        Assert.False(result.HasErrors);
        Assert.Equal("in.etl", result.InputPath);
        Assert.Equal("out.etl", result.OutputPath);
    }

    [Fact]
    public void Parse_StartAndStop_AreParsed()
    {
        CliArguments result = CliArguments.Parse(
            ["-i", "in.etl", "-o", "out.etl", "--start", "1500.5", "--stop", "9000"]);

        Assert.False(result.HasErrors);
        Assert.Equal(1500.5, result.StartMSec);
        Assert.Equal(9000, result.StopMSec);
    }

    [Fact]
    public void Parse_DefaultWindow_IsWholeTrace()
    {
        CliArguments result = CliArguments.Parse(["in.etl", "out.etl"]);

        Assert.Equal(0, result.StartMSec);
        Assert.Equal(double.PositiveInfinity, result.StopMSec);
        Assert.True(result.KeepMetadata);
    }

    [Fact]
    public void Parse_NoMetadataFlag_DisablesMetadata()
    {
        CliArguments result = CliArguments.Parse(["in.etl", "out.etl", "--no-metadata"]);

        Assert.False(result.HasErrors);
        Assert.False(result.KeepMetadata);
    }

    [Fact]
    public void Parse_MissingInput_ReportsError()
    {
        CliArguments result = CliArguments.Parse(["--output", "out.etl"]);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Contains("input", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_MissingOutput_ReportsError()
    {
        CliArguments result = CliArguments.Parse(["--input", "in.etl"]);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Contains("output", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_NonNumericStart_ReportsError()
    {
        CliArguments result = CliArguments.Parse(["-i", "in.etl", "-o", "out.etl", "--start", "soon"]);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Contains("numeric", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_StartGreaterThanStop_ReportsError()
    {
        CliArguments result = CliArguments.Parse(["-i", "in.etl", "-o", "out.etl", "--start", "900", "--stop", "100"]);

        Assert.True(result.HasErrors);
    }

    [Fact]
    public void Parse_UnknownOption_ReportsError()
    {
        CliArguments result = CliArguments.Parse(["-i", "in.etl", "-o", "out.etl", "--frobnicate"]);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Contains("Unknown option", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_OptionWithoutValue_ReportsError()
    {
        CliArguments result = CliArguments.Parse(["--input"]);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Contains("requires a value", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ToCropOptions_MapsAllValues()
    {
        CliArguments result = CliArguments.Parse(
            ["-i", "in.etl", "-o", "out.etl", "--start", "100", "--stop", "500", "--no-metadata"]);

        EtlCropOptions options = result.ToCropOptions();

        Assert.Equal(100, options.StartTimeRelativeMSec);
        Assert.Equal(500, options.StopTimeRelativeMSec);
        Assert.False(options.KeepMetadataEvents);
    }
}
