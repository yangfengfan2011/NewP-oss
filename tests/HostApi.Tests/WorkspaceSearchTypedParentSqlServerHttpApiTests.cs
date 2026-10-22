using System.Net;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceSearchSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "同一真实Link改为合法动态父类型声明后，roots、children、path、search都核对父子类型")]
    public async Task DynamicParentType_CannotUseAnEqualFolderIdToForgeTheDeclaredSelfReference()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.RemoveAll<LinkQueryCatalog>(); services.AddSingleton(provider =>
            {
                var plan = provider.GetRequiredService<CompiledPersistencePlan>();
                var source = plan.Models.Single(model => model.LogicalKey == "WorkspaceFolderContent");
                var metadata = source.LinkQuery!;
                var typed = metadata with
                {
                    Parent = metadata.Parent with { FixedModelKey = null, TypeField = "ChildModelKey" },
                    Child = metadata.Child with { FixedModelKey = "WorkspaceFolder", TypeField = null },
                    Endpoints = metadata.Endpoints.Where(pair => pair.Key == "WorkspaceFolder").ToDictionary(),
                    Columns = metadata.Columns.Select(column => column with { Mappings = column.Mappings.Where(pair => pair.Key == "WorkspaceFolder").ToDictionary() }).ToArray(),
                };
                var model = source with { LinkQuery = typed, LinkedTypes = source.LinkedTypes.Where(pair => pair.Parent == "WorkspaceFolder" && pair.Child == "WorkspaceFolder").ToArray() };
                var treePlan = plan with { Models = plan.Models.Select(item => item.LogicalKey == model.LogicalKey ? model : item).ToArray() };
                return new LinkQueryCatalog(treePlan, provider.GetRequiredService<IStandardCrudResourceRegistry>(), provider.GetServices<DataScopeResource>(), provider.GetRequiredService<CanonicalModulePlan>());
            });
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access"); var root = await Initialize(admin, entry);
        var a = await Create(admin, fixture, root, "父类型命中"); var b = await Create(admin, fixture, a, "父类型后代");
        Assert.Equal(2, (await Search(admin, entry, "父类型", 1, 20)).GetProperty("total").GetInt64());
        using (var valid = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Get, TreePathUrl(b))) Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>(); var lid = a.LinkIds[0];
            var relation = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").SingleAsync(row => EF.Property<string>(row, "LID") == lid);
            relation["ChildModelKey"] = "Part"; await db.SaveChangesAsync();
        }
        var roots = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/trees/workspace.structure/roots?scope=currentUser");
        Assert.False(Assert.Single(roots.EnumerateArray()).GetProperty("hasChildren").GetBoolean());
        using (var forged = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Get, TreePathUrl(b))) Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
        var children = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/trees/workspace.structure/children?scope=currentUser&nodeKey=" + Uri.EscapeDataString(ICP.Platform.Crud.Runtime.LinkTreeNodeKey.Encode(new(root.SpaceId, root.RootFolderId, []))));
        Assert.Empty(children.EnumerateArray());
        Assert.Equal(0, (await Search(admin, entry, "父类型", 1, 20)).GetProperty("total").GetInt64());
    }
}
