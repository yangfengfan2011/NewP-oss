using ICP.Platform.Modeling;
using ICP.Platform.Presentation;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class TemporalInputNormalizerTests
{
    [Fact]
    public void NormalizeCalendarDate_PreservesLeapDayWithoutTimeZoneConversion()
    {
        var normalizer = Create("America/New_York");

        var result = normalizer.Normalize(FieldValueType.CalendarDate, "2028-02-29");

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2028, 2, 29), result.Value);
    }

    [Theory]
    [InlineData("2026-03-08T02:30:00", "TEMPORAL_LOCAL_TIME_INVALID")]
    [InlineData("2026-11-01T01:30:00", "TEMPORAL_LOCAL_TIME_AMBIGUOUS")]
    public void NormalizeInstant_RejectsInvalidOrAmbiguousLocalTime(string input, string expectedCode)
    {
        var normalizer = Create("America/New_York");

        var result = normalizer.Normalize(FieldValueType.Instant, input);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.ErrorCode);
    }

    [Fact]
    public void NormalizeInstant_UsesSessionTimeZoneAndReturnsWholeSecondUtc()
    {
        var normalizer = Create("Asia/Taipei");

        var result = normalizer.Normalize(FieldValueType.Instant, "2030-01-02T08:04:05");

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTime(2030, 1, 2, 0, 4, 5, DateTimeKind.Utc), result.Value);
    }

    [Fact]
    public void NormalizeInstant_UsesDeploymentDefaultWithoutRequestContext()
    {
        var normalizer = new TemporalInputNormalizer(
            new FixedPresentationContextAccessor(null),
            "Asia/Shanghai",
            TimeProvider.System);

        var result = normalizer.Normalize(FieldValueType.Instant, "2030-01-02T08:04:05");

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTime(2030, 1, 2, 0, 4, 5, DateTimeKind.Utc), result.Value);
    }

    private static TemporalInputNormalizer Create(string timeZoneId) =>
        new(
            new FixedPresentationContextAccessor(new RequestPresentationContext("zh-CN", timeZoneId)),
            "Asia/Shanghai",
            TimeProvider.System);

    private sealed class FixedPresentationContextAccessor(RequestPresentationContext? context)
        : IRequestPresentationContextAccessor
    {
        public RequestPresentationContext? Current => context;
    }
}
