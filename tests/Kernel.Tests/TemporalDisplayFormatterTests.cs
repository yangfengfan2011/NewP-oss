using ICP.Platform.Presentation;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class TemporalDisplayFormatterTests
{
    [Theory]
    [InlineData("zh-CN", "Asia/Taipei", "2030/1/2 08:04:05")]
    [InlineData("zh-CN", "America/New_York", "2030/1/1 19:04:05")]
    [InlineData("en-US", "Asia/Taipei", "1/2/2030 8:04:05 AM")]
    [InlineData("en-US", "America/New_York", "1/1/2030 7:04:05 PM")]
    public void FormatInstant_UsesRequestLocaleAndSessionTimeZone(
        string locale,
        string timeZoneId,
        string expected)
    {
        var accessor = new FixedPresentationContextAccessor(
            new RequestPresentationContext(locale, timeZoneId));
        var formatter = new TemporalDisplayFormatter(accessor);

        var result = formatter.FormatInstant(
            new DateTime(2030, 1, 2, 0, 4, 5, DateTimeKind.Utc));

        Assert.Equal(expected, result);
    }

    private sealed class FixedPresentationContextAccessor(RequestPresentationContext context)
        : IRequestPresentationContextAccessor
    {
        public RequestPresentationContext Current => context;
    }
}
