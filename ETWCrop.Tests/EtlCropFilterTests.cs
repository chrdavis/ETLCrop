using ETWCrop;
using Xunit;

namespace ETWCrop.Tests;

public class EtlCropFilterTests
{
    private static EtlCropOptions Window(double start, double stop, bool keepMetadata = true) => new()
    {
        StartTimeRelativeMSec = start,
        StopTimeRelativeMSec = stop,
        KeepMetadataEvents = keepMetadata,
    };

    [Theory]
    [InlineData(100)]
    [InlineData(250.5)]
    [InlineData(500)]
    public void ShouldInclude_TimeWithinWindow_ReturnsTrue(double time)
    {
        Assert.True(EtlCropFilter.ShouldInclude(time, isMetadataEvent: false, Window(100, 500)));
    }

    [Theory]
    [InlineData(99.999)]
    [InlineData(0)]
    [InlineData(500.001)]
    [InlineData(10_000)]
    public void ShouldInclude_TimeOutsideWindow_ReturnsFalse(double time)
    {
        Assert.False(EtlCropFilter.ShouldInclude(time, isMetadataEvent: false, Window(100, 500)));
    }

    [Fact]
    public void ShouldInclude_BoundariesAreInclusive()
    {
        EtlCropOptions options = Window(100, 500);
        Assert.True(EtlCropFilter.ShouldInclude(100, isMetadataEvent: false, options));
        Assert.True(EtlCropFilter.ShouldInclude(500, isMetadataEvent: false, options));
    }

    [Fact]
    public void ShouldInclude_MetadataOutsideWindow_KeptWhenEnabled()
    {
        EtlCropOptions options = Window(100, 500, keepMetadata: true);
        Assert.True(EtlCropFilter.ShouldInclude(0, isMetadataEvent: true, options));
        Assert.True(EtlCropFilter.ShouldInclude(9_000, isMetadataEvent: true, options));
    }

    [Fact]
    public void ShouldInclude_MetadataOutsideWindow_DroppedWhenDisabled()
    {
        EtlCropOptions options = Window(100, 500, keepMetadata: false);
        Assert.False(EtlCropFilter.ShouldInclude(0, isMetadataEvent: true, options));
        Assert.False(EtlCropFilter.ShouldInclude(9_000, isMetadataEvent: true, options));
    }

    [Fact]
    public void ShouldInclude_MetadataInsideWindow_AlwaysKept()
    {
        Assert.True(EtlCropFilter.ShouldInclude(250, isMetadataEvent: true, Window(100, 500, keepMetadata: false)));
        Assert.True(EtlCropFilter.ShouldInclude(250, isMetadataEvent: true, Window(100, 500, keepMetadata: true)));
    }

    [Fact]
    public void ShouldInclude_OpenEndedStop_KeepsLateEvents()
    {
        EtlCropOptions options = Window(100, double.PositiveInfinity);
        Assert.True(EtlCropFilter.ShouldInclude(1_000_000, isMetadataEvent: false, options));
        Assert.False(EtlCropFilter.ShouldInclude(50, isMetadataEvent: false, options));
    }

    [Fact]
    public void ShouldInclude_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => EtlCropFilter.ShouldInclude(0, isMetadataEvent: false, options: null!));
    }
}
