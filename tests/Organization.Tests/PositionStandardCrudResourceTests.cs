using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class PositionStandardCrudResourceTests
{
    [Fact]
    public void Descriptor_ExposesListCreateAndUpdateWithoutRevisionOrDelete()
    {
        var descriptor = PositionCrudDescriptorFactory.Create();

        Assert.Equal("organization.position", descriptor.ResourceKey);
        Assert.Equal("api/organization/positions", descriptor.Route);
        Assert.Equal(["EID", "ENAME", "Description", "IsEnabled"], descriptor.FieldSet.ListFields);
        Assert.Equal(["ENAME", "Description"], descriptor.FieldSet.CreateFields);
        Assert.Equal(["ENAME", "Description", "IsEnabled"], descriptor.FieldSet.UpdateFields);
        Assert.Null(descriptor.ConcurrencyRevisionField);
        Assert.Equal(StandardCrudDeletePolicy.None, descriptor.DeletePolicy);
        Assert.Equal("ENAME", descriptor.DefaultSortField);
        var nameRequired = Assert.Single(descriptor.RequiredFields);
        Assert.Equal(StandardCrudRequiredFieldScope.CreateAndUpdate, nameRequired.Scope);
        var name = Assert.Single(descriptor.FieldPolicies, static policy => policy.Field == "ENAME");
        Assert.True(name.Trim);
        Assert.Equal(256, name.MaxLength);
        Assert.True(name.UpdateNonEmpty);
        var description = Assert.Single(descriptor.FieldPolicies, static policy => policy.Field == "Description");
        Assert.True(description.Trim);
        Assert.True(description.EmptyAsNull);
        Assert.Equal(256, description.MaxLength);
        var enabled = Assert.Single(descriptor.FieldPolicies, static policy => policy.Field == "IsEnabled");
        Assert.Equal(StandardCrudFieldValueOwner.ServerOnCreate, enabled.ValueOwner);
        Assert.Equal(true, enabled.CreateDefaultValue);
    }

    [Fact]
    public void QueryCustomizer_SearchesNameOrDescriptionAndFiltersStatus()
    {
        var conditions = new PositionCrudQueryCustomizer().BuildConditions(
            PositionCrudDescriptorFactory.Create(),
            new StandardCrudListQuery(1, 20, null, null, new Dictionary<string, string?>
            {
                ["keyword"] = "架构",
                ["isEnabled"] = "true"
            }));

        Assert.Collection(
            conditions,
            condition => Assert.Equal(new StandardCrudQueryCondition("ENAME", "contains", "架构", "keyword", "or"), condition),
            condition => Assert.Equal(new StandardCrudQueryCondition("Description", "contains", "架构", "keyword", "or"), condition),
            condition => Assert.Equal(new StandardCrudQueryCondition("IsEnabled", "=", "true"), condition));
    }

    [Fact]
    public async Task Directory_BatchesDistinctIdsIntoOnePlatformQuery()
    {
        var executor = new RecordingQueryExecutor(
        [
            Row("Position_2", "平台主管", false),
            Row("Position_1", "平台工程师", true)
        ]);
        var directory = new PlatformQueryPositionDirectory(
            executor,
            new StandardCrudResourceRegistry([PositionCrudDescriptorFactory.Create()]));

        var result = await directory.GetByIdsAsync(["Position_1", "Position_2", "Position_1"]);

        Assert.Equal(1, executor.CallCount);
        Assert.NotNull(executor.Request);
        Assert.Equal(2, executor.Request.PageSize);
        Assert.Equal("ENAME", executor.Request.SortBy);
        Assert.Equal(2, Assert.IsAssignableFrom<IReadOnlyList<PlatformQueryCondition>>(executor.Request.Conditions).Count);
        Assert.True(result["Position_1"].IsEnabled);
        Assert.False(result["Position_2"].IsEnabled);
    }

    private static IReadOnlyDictionary<string, object?> Row(string id, string name, bool enabled) =>
        new Dictionary<string, object?>
        {
            ["EID"] = id,
            ["ENAME"] = name,
            ["Description"] = null,
            ["IsEnabled"] = enabled
        };

    private sealed class RecordingQueryExecutor(IReadOnlyList<IReadOnlyDictionary<string, object?>> items)
        : IPlatformQueryExecutor
    {
        public int CallCount { get; private set; }

        public PlatformQueryRequest? Request { get; private set; }

        public Task<PlatformQueryResult> QueryAsync(
            PlatformQueryDescriptor descriptor,
            PlatformQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = descriptor;
            _ = cancellationToken;
            CallCount++;
            Request = request;
            return Task.FromResult(new PlatformQueryResult(
                items,
                items.Count,
                request.Page,
                request.PageSize,
                request.SortBy,
                request.SortDirection,
                request.Fields ?? [],
                []));
        }
    }
}
