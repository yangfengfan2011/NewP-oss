using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Presentation;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class TemporalDisplayProjectionExecutorTests
{
    [Theory]
    [InlineData(TemporalDisplayFormat.Date, "zh-CN", "Asia/Taipei", "2030/1/2")]
    [InlineData(TemporalDisplayFormat.Date, "zh-CN", "America/New_York", "2030/1/1")]
    [InlineData(TemporalDisplayFormat.Date, "en-US", "Asia/Taipei", "1/2/2030")]
    [InlineData(TemporalDisplayFormat.Date, "en-US", "America/New_York", "1/1/2030")]
    [InlineData(TemporalDisplayFormat.DateTime, "zh-CN", "Asia/Taipei", "2030/1/2 08:04:05")]
    [InlineData(TemporalDisplayFormat.DateTime, "zh-CN", "America/New_York", "2030/1/1 19:04:05")]
    [InlineData(TemporalDisplayFormat.DateTime, "en-US", "Asia/Taipei", "1/2/2030 8:04:05 AM")]
    [InlineData(TemporalDisplayFormat.DateTime, "en-US", "America/New_York", "1/1/2030 7:04:05 PM")]
    public void Project_UsesTypedFormatRequestLocaleAndSessionTimeZone(
        TemporalDisplayFormat format,
        string locale,
        string timeZoneId,
        string expected)
    {
        var accessor = new FixedPresentationContextAccessor(
            new RequestPresentationContext(locale, timeZoneId));
        var executor = new TemporalDisplayProjectionExecutor(
            accessor,
            new TemporalDisplayFormatter());
        var instant = new DateTime(2030, 1, 2, 0, 4, 5, DateTimeKind.Utc);

        var result = executor.Project(
            new TemporalFieldDisplayProjectionPlan("OccurredAt", "OccurredAt_DisplayValue", format),
            instant);

        Assert.Equal(instant, result.RawValue);
        Assert.Equal(expected, result.DisplayValue);
    }

    [Fact]
    public void Project_WhenPresentationContextIsUnavailable_PreservesRawWithNullDisplay()
    {
        var executor = new TemporalDisplayProjectionExecutor(
            new FixedPresentationContextAccessor(null),
            new TemporalDisplayFormatter());
        var instant = new DateTime(2030, 1, 2, 0, 4, 5, DateTimeKind.Utc);

        var result = executor.Project(
            new TemporalFieldDisplayProjectionPlan(
                "OccurredAt",
                "OccurredAt_DisplayValue",
                TemporalDisplayFormat.DateTime),
            instant);

        Assert.Equal(instant, result.RawValue);
        Assert.Null(result.DisplayValue);
    }

    [Fact]
    public void Project_WhenRawInstantIsNotUtc_FailsBeforeContextFallback()
    {
        var executor = new TemporalDisplayProjectionExecutor(
            new FixedPresentationContextAccessor(null),
            new TemporalDisplayFormatter());

        var error = Assert.Throws<InvalidOperationException>(() => executor.Project(
            new TemporalFieldDisplayProjectionPlan(
                "OccurredAt",
                "OccurredAt_DisplayValue",
                TemporalDisplayFormat.DateTime),
            new DateTime(2030, 1, 2, 0, 4, 5, DateTimeKind.Unspecified)));

        Assert.Equal("TEMPORAL_INSTANT_UTC_REQUIRED", error.Message);
    }

    private sealed class FixedPresentationContextAccessor(RequestPresentationContext? context)
        : IRequestPresentationContextAccessor
    {
        public RequestPresentationContext? Current => context;
    }
}
