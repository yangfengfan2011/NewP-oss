using System.Text.Json;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Caching;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public sealed class DataScopeRuleSnapshotTests : IDisposable
{
    private const string Role = "authorization.role";
    private const string Lost = "lost.resource";
    private readonly EfPlatformDbContext db = new(new DbContextOptionsBuilder<EfPlatformDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
        .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options, AuthorizationModelDefinitions.All);
    private readonly DataScopeCatalog catalog;

    public DataScopeRuleSnapshotTests()
    {
        var module = new CanonicalModulePlanModule("Authorization", "1.0", "授权", null, "", null, null,
            null, null, "", [], [], [], [], [], new("test", "test", "/"));
        catalog = new DataScopeCatalog(new StandardCrudResourceRegistry([RoleReadResource.Create()]),
            new CanonicalModulePlan("test", "1.0", "", "", [], [module]), db,
            new DataScopeIdentities([new AllAuthenticatedDataScopeIdentities()]));
    }

    // 全量装载逐条填充 builder，提交与修复沿 Apply；两条路径必须得到同一隔离与故障结论。
    public static TheoryData<bool> Paths => new() { false, true };

    // 模块卸载后其规则仍留在库中：完整树只封闭自身资源，其他资源照常判权，删除后错误随最后节点一起清除。
    [Theory]
    [MemberData(nameof(Paths))]
    public void UnregisteredResourceTree_IsIsolatedAndClearedByDeletion(bool streamed)
    {
        var snapshot = Load(streamed,
            [Added(Node("RoleRule", "RoleRule", null, Role)), Added(Node("LostRule", "LostRule", null, Lost)),
             Added(Node("LostChild", "LostRule", "LostRule", Lost))]);

        Assert.Equal("RoleRule", Assert.Single(snapshot.ForResource(Role)).Id);
        var unavailable = Assert.Throws<PlatformCacheUnavailableException>(() => snapshot.ForResource(Lost));
        Assert.Equal("DATA_SCOPE_RESOURCE_UNKNOWN", Assert.IsType<RolePermissionValidationException>(unavailable.InnerException).Code);
        Assert.Equal(3, snapshot.RecordCount);

        var deleted = snapshot.Apply([Deleted("LostChild"), Deleted("LostRule")], catalog, default);
        Assert.Empty(deleted.ForResource(Lost));
        Assert.Equal("RoleRule", Assert.Single(deleted.ForResource(Role)).Id);
        Assert.Equal(1, deleted.RecordCount);
    }

    // 已登记根下混入未登记子节点属于跨资源损坏，两端都失败关闭，但不扩大为整份缓存故障。
    [Theory]
    [MemberData(nameof(Paths))]
    public void UnregisteredChildUnderRegisteredRoot_ClosesBothResourcesOnly(bool streamed)
    {
        var snapshot = Load(streamed,
            [Added(Node("RoleRule", "RoleRule", null, Role)), Added(Node("LostChild", "RoleRule", "RoleRule", Lost))]);

        var registered = Assert.Throws<PlatformCacheUnavailableException>(() => snapshot.ForResource(Role));
        Assert.Equal("DATA_SCOPE_RULE_RESOURCE_RELATION_INVALID", Assert.IsType<InvalidOperationException>(registered.InnerException).Message);
        Assert.Throws<PlatformCacheUnavailableException>(() => snapshot.ForResource(Lost));
    }

    // 孤立主体无法建立任何资源归属，仍使整份结果无法发布。
    [Theory]
    [MemberData(nameof(Paths))]
    public void OrphanSubject_StillFailsTheWholeSnapshot(bool streamed)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Load(streamed,
            [Added(Node("RoleRule", "RoleRule", null, Role)), new(DataScopeRuleCache.SubjectModel, "Orphan", null, new Dictionary<string, object?>
            {
                ["EID"] = "Orphan", ["NodeId"] = "MissingNode", ["SubjectType"] = "all", ["SubjectId"] = null, ["Priority"] = 1L,
                ["ActionsJson"] = "{}", ["MatchOptionsJson"] = null
            })]));
        Assert.Equal("DATA_SCOPE_SUBJECT_NODE_INVALID", error.Message);
    }

    public void Dispose() => db.Dispose();

    private DataScopeRuleSnapshot Load(bool streamed, PlatformRecordChange[] changes)
    {
        if (!streamed) return DataScopeRuleSnapshot.Empty.Apply(changes, catalog, default);
        var builder = new DataScopeRuleSnapshot.Builder(DataScopeRuleSnapshot.Empty);
        foreach (var change in changes) builder.Add(change, default);
        return builder.Build(catalog, default);
    }

    private static PlatformRecordChange Added(Dictionary<string, object?> node) => new(DataScopeRuleCache.NodeModel, (string)node["EID"]!, null, node);

    private static PlatformRecordChange Deleted(string id) => new(DataScopeRuleCache.NodeModel, id, null, null);

    private static Dictionary<string, object?> Node(string id, string root, string? parent, string resource) => new()
    {
        ["EID"] = id, ["RootId"] = root, ["ParentId"] = parent, ["Name"] = id, ["Enabled"] = true, ["ResourceKey"] = resource,
        ["ConditionJson"] = JsonSerializer.Serialize(new DataScopeConditionDto("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement("keep")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
    };
}
