using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Application;
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
        var service = new IdentityManagementQueryService(new InMemoryIdentityRepository(), executor);

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
        var service = new IdentityManagementQueryService(new InMemoryIdentityRepository(), executor);

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
            return Task.FromResult(result);
        }
    }
}
