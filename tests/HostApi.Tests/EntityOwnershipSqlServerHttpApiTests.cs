using System.Net;
using System.Text.Json;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class EntityOwnershipSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实Folder沿公共字段创建归属，保留独立归属及审计并拒绝普通输入伪造")]
    public async Task Folder_CreatesTrustedOwnership_OrdinarySavePreservesIndependentOwnerAndAudit()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var initialized = await new FeatureHttpTestRequests(fixture).InitializeOrganizationAsync(admin);
        var department = initialized.GetProperty("departmentId").GetString()!;
        var entry = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "workspace.spaces.access");
        using var rootResponse = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);
        var root = JsonSerializer.Deserialize<JsonElement>(await rootResponse.Content.ReadAsStringAsync());
        var position = new { spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = root.GetProperty("rootFolderId").GetString(), linkIds = Array.Empty<string>() };
        var create = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "workspace.folder.create");
        using var created = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/workspace/content/folders", new { position, values = new { ENAME = "归属对象" } });
        Assert.True(created.StatusCode == HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var receipt = JsonSerializer.Deserialize<JsonElement>(await created.Content.ReadAsStringAsync());
        var id = receipt.GetProperty("id").GetString()!;
        var fields = receipt.GetProperty("fields");
        Assert.Equal(fields.GetProperty("CreatedBy").GetString(), fields.GetProperty("OwnerId").GetString());
        Assert.Equal(department, fields.GetProperty("OwnerDepartmentId").GetString());
        string linkId;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var row = await db.Set<Dictionary<string, object?>>("WorkspaceFolder").SingleAsync(row => EF.Property<string>(row, "EID") == id);
            var audit = await db.Set<Dictionary<string, object?>>("BusinessChange").SingleAsync(row => EF.Property<string>(row, "TargetId") == id
                && EF.Property<string>(row, "EventType") == "entity.ownership.initialized");
            Assert.Equal(row["CreatedBy"], audit["ActorId"]);
            Assert.Equal(row["CreatedAt"], audit["OccurredAt"]);
            linkId = (string)(await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").SingleAsync())["LID"]!;
            // 模拟后续已独立维护的归属事实，普通保存不得重算为创建人或操作者。
            await db.Set<Dictionary<string, object?>>("WorkspaceFolder").Where(row => EF.Property<string>(row, "EID") == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string>(row, "OwnerId"), "other-persisted-owner"));
        }
        var edit = fixture.FeatureCatalog.First(point => point.BehaviorKey == "workspace.folder.editObject");
        using var forged = await FeatureHttpTestRequests.SendAsync(admin, edit, HttpMethod.Put, $"/api/workspace/content/{linkId}",
            new { position, values = new { OwnerId = "forged" } });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using var saved = await FeatureHttpTestRequests.SendAsync(admin, edit, HttpMethod.Put, $"/api/workspace/content/{linkId}",
            new { position, values = new { ENAME = "保存后" } });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var afterScope = fixture.Services.CreateScope();
        var after = await afterScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolder")
            .SingleAsync(row => EF.Property<string>(row, "EID") == id);
        Assert.Equal("other-persisted-owner", after["OwnerId"]);
        Assert.Equal(department, after["OwnerDepartmentId"]);
        Assert.Equal(fields.GetProperty("CreatedBy").GetString(), after["CreatedBy"]);
    }
}
