using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Modules.Organization.Crud;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class PersonUpdateRecorderTests
{
    [Fact]
    public async Task Register_RecordsExactlyTheEightEditableBusinessFields()
    {
        var sink = new Sink();
        var recorder = new PersonUpdateRecorder(sink);
        var before = new Dictionary<string, object?> { ["EID"] = "person", ["ENAME"] = "旧姓名" };
        var after = new Dictionary<string, object?>
        {
            ["EID"] = "person", ["ENAME"] = "新姓名", ["BirthDate"] = new DateOnly(2000, 2, 29),
            ["CompanyId"] = "company", ["DepartmentId"] = "department", ["PositionId"] = "position",
            ["EmploymentStatus"] = "Active", ["EmploymentStartedAt"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ["EmploymentEndedAt"] = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            ["PersonNo"] = "ignored", ["IsEnabled"] = true, ["AccountEID"] = "ignored", ["FutureField"] = "ignored",
            ["CreatedBy"] = "actor", ["UpdatedBy"] = "actor", ["CreatedAt"] = DateTime.UtcNow, ["UpdatedAt"] = DateTime.UtcNow
        };
        await recorder.RegisterAsync(before, after);
        var change = Assert.Single(sink.Changes);
        Assert.Equal("person.updated", change.EventType);
        Assert.Equal("organization.person", change.Resource);
        Assert.Equal("person", change.TargetId);
        Assert.Equal(new[] { "ENAME", "BirthDate", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "EmploymentStartedAt", "EmploymentEndedAt" }, change.Fields.Select(f => f.Field));
        Assert.Equal(new BusinessAuditValue("text", "旧姓名"), change.Fields[0].Before);
        Assert.Equal(new BusinessAuditValue("calendarDate", null), change.Fields[1].Before);
        Assert.Equal(new BusinessAuditValue("calendarDate", "2000-02-29"), change.Fields[1].After);
        Assert.Equal(new BusinessAuditValue("instant", "2026-01-01T00:00:00.0000000Z"), change.Fields[6].After);
    }

    [Fact]
    public async Task Register_OnlyAuditStampsOrExcludedFieldsChange_DoesNotRegister()
    {
        var sink = new Sink();
        var before = new Dictionary<string, object?> { ["EID"] = "person", ["ENAME"] = "姓名", ["UpdatedBy"] = "old" };
        var after = new Dictionary<string, object?>(before)
        {
            ["UpdatedBy"] = "new", ["UpdatedAt"] = DateTime.UtcNow, ["IsEnabled"] = false, ["PersonNo"] = "other"
        };
        await new PersonUpdateRecorder(sink).RegisterAsync(before, after);
        Assert.Empty(sink.Changes);
    }

    [Fact]
    public async Task Register_FailurePropagatesToTheTransactionOwner()
    {
        var sink = new Sink { Failure = new InvalidOperationException("registration failed") };
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await new PersonUpdateRecorder(sink).RegisterAsync(
            new Dictionary<string, object?> { ["EID"] = "person", ["ENAME"] = "old" },
            new Dictionary<string, object?> { ["EID"] = "person", ["ENAME"] = "new" }));
        Assert.Same(sink.Failure, exception);
    }

    private sealed class Sink : IBusinessAuditRecorder
    {
        internal List<BusinessAuditChange> Changes { get; } = [];
        internal Exception? Failure { get; init; }
        public ValueTask RegisterAsync(BusinessAuditChange change, CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;
            Changes.Add(change);
            return ValueTask.CompletedTask;
        }
    }
}
