using ETWCrop;
using Xunit;

namespace ETWCrop.Tests;

public class EtlCropOptionsTests
{
    [Fact]
    public void Defaults_AreWholeTraceWithMetadata()
    {
        var options = new EtlCropOptions();

        Assert.Equal(0, options.StartTimeRelativeMSec);
        Assert.Equal(double.PositiveInfinity, options.StopTimeRelativeMSec);
        Assert.True(options.KeepMetadataEvents);
    }

    [Fact]
    public void Validate_DefaultOptions_DoesNotThrow()
    {
        var options = new EtlCropOptions();
        options.Validate();
    }

    [Fact]
    public void Validate_ValidWindow_DoesNotThrow()
    {
        var options = new EtlCropOptions { StartTimeRelativeMSec = 100, StopTimeRelativeMSec = 500 };
        options.Validate();
    }

    [Fact]
    public void Validate_StartGreaterThanStop_Throws()
    {
        var options = new EtlCropOptions { StartTimeRelativeMSec = 500, StopTimeRelativeMSec = 100 };
        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void Validate_NegativeStart_Throws()
    {
        var options = new EtlCropOptions { StartTimeRelativeMSec = -1 };
        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void Validate_NaNStart_Throws()
    {
        var options = new EtlCropOptions { StartTimeRelativeMSec = double.NaN };
        Assert.Throws<ArgumentException>(options.Validate);
    }

    [Fact]
    public void Validate_NaNStop_Throws()
    {
        var options = new EtlCropOptions { StopTimeRelativeMSec = double.NaN };
        Assert.Throws<ArgumentException>(options.Validate);
    }
}
