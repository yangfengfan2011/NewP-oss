using ICP.Platform.Modules.Audit.Contracts;
using Xunit;
namespace ICP.Platform.Modules.Audit.Tests;
public sealed class BusinessChangeTests
{
    [Fact]
    public void TypedValues_PreserveCalendarDateInstantAndExplicitNull()
    {
        Assert.Equal("2024-02-29", BusinessAuditValue.CalendarDate(new DateOnly(2024, 2, 29)).Value);
        Assert.Equal("2026-09-10T01:02:03.0000000Z", BusinessAuditValue.Instant(new DateTime(2026, 9, 10, 1, 2, 3, DateTimeKind.Utc)).Value);
        Assert.Null(BusinessAuditValue.Text(null).Value);
        Assert.Throws<ArgumentException>(() => BusinessAuditValue.Instant(DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified)));
    }
}
