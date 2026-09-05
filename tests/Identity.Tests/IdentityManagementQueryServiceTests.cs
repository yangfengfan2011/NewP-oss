using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Presentation;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using Xunit;

namespace ICP.Platform.Identity.Tests;

public sealed class IdentityManagementQueryServiceTests
{
    [Fact]
    public async Task SearchAsync_MapsAccountFiltersAndPagingToPlatformQuery()
    {
        var executor = new RecordingPlatformQueryExecutor(new PlatformQueryResult(
            Items:
            [
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["EID"] = "User_2",
                    ["ECODE"] = "alice",
                    ["ENAME"] = "Alice",
                    ["Status"] = "Disabled",
                    ["Revision"] = 7L,
                    ["IsLocalAdministrator"] = false,
                    ["IsPersonnelBound"] = true,
                    ["PersonnelDisableHold"] = false,
                    ["CreatedBy"] = "User_1",
                    ["CreatedAt"] = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                    ["UpdatedBy"] = "User_1",
                    ["UpdatedAt"] = new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc)
                }
            ],
            Total: 3,
            Page: 2,
            PageSize: 1,
            SortBy: "ENAME",
            SortDirection: "desc",
            ResponseFields: [],
            RequestedDisplayFields: []));
        var service = IdentityCrudQueryTestFactory.Create(
            new InMemoryIdentityRepository(),
            executor,
            new TestTemporalDisplayProjectionExecutor());

        var result = await service.SearchAsync(new IdentityManagementAccountSearch(
            Query: "ali",
            Status: IdentityAccountStatus.Disabled,
            IsPersonnelBound: true,
            IsLocalAdministrator: false,
            Page: 2,
            PageSize: 1,
            SortBy: "ENAME",
            SortDirection: "desc"));

        Assert.Equal(3, result.TotalCount);
        Assert.Equal("User_2", Assert.Single(result.Items).AccountEid);
        Assert.Equal(IdentityAccountStatus.Disabled, result.Items[0].Status);
        Assert.Equal(2, executor.Request?.Page);
        Assert.Equal(1, executor.Request?.PageSize);
        Assert.Equal("ENAME", executor.Request?.SortBy);
        Assert.Equal("desc", executor.Request?.SortDirection);
        Assert.Contains(executor.Request!.Conditions!, condition =>
            condition.Field == "ECODE"
            && condition.Operator == "contains"
            && Equals(condition.Value, "ali")
            && condition.GroupKey == "keyword"
            && condition.GroupOperator == "or");
        Assert.Contains(executor.Request.Conditions!, condition =>
            condition.Field == "ENAME"
            && condition.GroupKey == "keyword"
            && condition.GroupOperator == "or");
        Assert.Contains(executor.Request.Conditions!, condition =>
            condition.Field == "Status"
            && condition.Operator == "="
            && Equals(condition.Value, "Disabled"));
        Assert.Contains(executor.Request.Conditions!, condition =>
            condition.Field == "IsPersonnelBound"
            && Equals(condition.Value, true));
        Assert.Contains(executor.Request.Conditions!, condition =>
            condition.Field == "IsLocalAdministrator"
            && Equals(condition.Value, false));
    }

    [Fact]
    public async Task SearchAsync_ExcludesDeletedByDefaultAndUsesStableAccountCodeSort()
    {
        var executor = new RecordingPlatformQueryExecutor(new PlatformQueryResult(
            [],
            0,
            1,
            20,
            "ECODE",
            "asc",
            [],
            []));
        var service = IdentityCrudQueryTestFactory.Create(
            new InMemoryIdentityRepository(),
            executor,
            new TestTemporalDisplayProjectionExecutor());

        await service.SearchAsync(new IdentityManagementAccountSearch(null, null, null, null, 1, 20));

        Assert.Null(executor.Request?.SortBy);
        Assert.Null(executor.Request?.SortDirection);
        var statusConditions = executor.Request!.Conditions!
            .Where(condition => condition.Field == "Status")
            .ToArray();
        Assert.Equal(2, statusConditions.Length);
        Assert.Equal(["Enabled", "Disabled"], statusConditions.Select(condition => condition.Value));
        Assert.All(statusConditions, condition =>
        {
            Assert.Equal("status", condition.GroupKey);
            Assert.Equal("or", condition.GroupOperator);
        });
        Assert.Equal("identity.account", executor.Descriptor?.ResourceKey);
        Assert.Equal("Identity/IdentityAccount", executor.Descriptor?.ModelName);
        Assert.Equal("ECODE", executor.Descriptor?.DefaultSortField);
        Assert.Equal("EID", executor.Descriptor?.IdField);
    }

    [Fact]
    public async Task FindAccountAsync_DoesNotLoadSecurityEvents()
    {
        var repository = new InMemoryIdentityRepository();
        repository.Accounts.Add(new ICP.Platform.Modules.Identity.Models.IdentityAccount
        {
            EID = "User_2",
            ECODE = "alice",
            NormalizedECODE = "ALICE",
            ENAME = "Alice",
            Status = IdentityAccountStatus.Deleted,
            Revision = 4,
            CreatedBy = "User_1",
            CreatedAt = DateTime.UtcNow,
            UpdatedBy = "User_1",
            UpdatedAt = DateTime.UtcNow
        });
        var service = IdentityCrudQueryTestFactory.Create(
            repository,
            new UnusedPlatformQueryExecutor(),
            new TestTemporalDisplayProjectionExecutor());

        var account = await service.FindAccountAsync("User_2");

        Assert.Equal(IdentityAccountStatus.Deleted, account?.Status);
        Assert.Equal(0, repository.SecurityEventSearchCount);
    }

    [Theory]
    [InlineData("2030/1/2 08:04:05")]
    [InlineData(null)]
    public async Task SearchSecurityEventsAsync_ConsumesTypedPlanAndKeepsRawDisplayPair(string? displayValue)
    {
        var occurredAt = new DateTime(2030, 1, 2, 0, 4, 5, DateTimeKind.Utc);
        var repository = new InMemoryIdentityRepository();
        repository.Accounts.Add(new IdentityAccount
        {
            EID = "User_2",
            ECODE = "alice",
            NormalizedECODE = "ALICE",
            ENAME = "Alice",
            Status = IdentityAccountStatus.Enabled
        });
        repository.SecurityEvents.Add(new IdentitySecurityEvent
        {
            EID = "Event_1",
            TargetAccountEID = "User_2",
            EventType = "AccountCreated",
            Reason = "created",
            OccurredAt = occurredAt,
            TraceId = "trace-1",
            ActorKind = IdentitySecurityActorKind.System
        });
        var executor = new RecordingTemporalDisplayProjectionExecutor(displayValue);
        var service = IdentityCrudQueryTestFactory.Create(
            repository,
            new UnusedPlatformQueryExecutor(),
            executor);

        var result = await service.SearchSecurityEventsAsync("User_2", 1, 20);

        var item = Assert.Single(result!.Items);
        Assert.Equal(occurredAt, item.OccurredAt);
        Assert.Equal(displayValue, item.OccurredAtDisplayValue);
        Assert.Equal(occurredAt, executor.LastRawValue);
        Assert.Equal("OccurredAt", executor.LastPlan?.SourceField);
        Assert.Equal("OccurredAt_DisplayValue", executor.LastPlan?.CompanionField);
        Assert.Equal(TemporalDisplayFormat.DateTime, executor.LastPlan?.Format);
    }

    private sealed class RecordingPlatformQueryExecutor(PlatformQueryResult result) : IPlatformQueryExecutor
    {
        public PlatformQueryDescriptor? Descriptor { get; private set; }

        public PlatformQueryRequest? Request { get; private set; }

        public Task<PlatformQueryResult> QueryAsync(
            PlatformQueryDescriptor descriptor,
            PlatformQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            Descriptor = descriptor;
            Request = request;
            return Task.FromResult(result with { ResponseFields = result.Items.FirstOrDefault()?.Keys.ToArray() ?? [] });
        }
    }

    private sealed class RecordingTemporalDisplayProjectionExecutor(string? displayValue)
        : ITemporalDisplayProjectionExecutor
    {
        public TemporalFieldDisplayProjectionPlan? LastPlan { get; private set; }

        public DateTime? LastRawValue { get; private set; }

        public TemporalDisplayProjectionResult Project(
            TemporalFieldDisplayProjectionPlan plan,
            DateTime rawValue)
        {
            LastPlan = plan;
            LastRawValue = rawValue;
            return new TemporalDisplayProjectionResult(rawValue, displayValue);
        }
    }
}
