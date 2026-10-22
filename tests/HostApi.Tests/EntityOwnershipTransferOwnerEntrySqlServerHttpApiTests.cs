using System.Net;
using System.Text.Json;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ICP.Platform.Crud.Ownership;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;
using static ICP.Platform.HostApi.Tests.EntityOwnershipTransferSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class EntityOwnershipTransferOwnerEntrySqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Part保正式祖先装配，缺接收实现的真实转交入口明确503而非DI失败")]
    public Task Part_WithoutReceivingCapability_ReturnsServiceUnavailable() => WithoutReceivingCapabilityAsync("Part", "parts");

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Document保正式祖先装配，缺接收实现的真实转交入口明确503而非DI失败")]
    public Task Document_WithoutReceivingCapability_ReturnsServiceUnavailable() => WithoutReceivingCapabilityAsync("Document", "documents");

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "人员调动不回写旧归属，自转交混合批次只记录真实部门变化，完整无变化不造目录/引用/通知")]
    public async Task SelfTransfer_ChangesOnlyTheOldDepartment_ThenAnUnchangedBatchIsANoOp()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var browse = Point(fixture, "workspace.spaces.access");
        var transfer = Point(fixture, "workspace.folder.transfer");
        var owner = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "SelfTransferOwner", "自转交所属人",
            "self-transfer-owner", "自转交所属人", browse, transfer, Point(fixture, "workspace.folder.createPart"));
        using var ownerClient = owner.Client;
        var original = await CreateAsync(fixture, ownerClient, position, "Part", "parts", "createPart", "SELF-TRANSFER-OLD");
        var originalRow = await RowAsync(fixture, original);
        string personId; string companyId;
        using (var scope = fixture.Services.CreateScope())
        {
            var person = await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("Person").AsNoTracking()
                .SingleAsync(row => EF.Property<string?>(row, "AccountEID") == owner.AccountId);
            personId = (string)person["EID"]!;
            companyId = (string)person["CompanyId"]!;
        }
        var http = new FeatureHttpTestRequests(fixture);
        var department = await FeatureHttpTestRequests.CreateAsync(admin, http.Organization("departments", "create", "list.pageActions"),
            "/api/organization/departments", new { ECODE = "SELF-TRANSFER-DEPT", ENAME = "调动后部门", CompanyId = companyId, SortOrder = "1" },
            http.Declaration("organization.department.create", http.Organization("departments", "create", "list.pageActions")));
        var newDepartment = BusinessAuditSqlServerHttpApiTests.Id(department);
        using (var moved = await FeatureHttpTestRequests.SendAsync(admin, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put,
                   $"/api/organization/persons/{personId}", new { CompanyId = companyId, DepartmentId = newDepartment },
                   http.Declaration("organization.person.update", http.Organization("persons", "edit", "list.rowActions"))))
            await ReadOkAsync(moved);
        Assert.Equal(originalRow["OwnerDepartmentId"], (await RowAsync(fixture, original))["OwnerDepartmentId"]);
        var current = await CreateAsync(fixture, ownerClient, position, "Part", "parts", "createPart", "SELF-TRANSFER-CURRENT");
        Assert.Equal(newDepartment, (await RowAsync(fixture, current))["OwnerDepartmentId"]);
        var input = new { position, linkIds = new[] { original.LinkId, current.LinkId }, recipientId = owner.AccountId };
        using (var mixed = await FeatureHttpTestRequests.SendAsync(ownerClient, transfer, HttpMethod.Post, "/api/workspace/ownership/transfer", input, OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer)))
            Assert.Equal(1, (await ReadOkAsync(mixed)).GetProperty("changed").GetInt32());
        Assert.Equal(owner.AccountId, (await RowAsync(fixture, original))["OwnerId"]);
        Assert.Equal(newDepartment, (await RowAsync(fixture, original))["OwnerDepartmentId"]);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var events = await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
                .Where(row => EF.Property<string>(row, "EventType") == "entity.ownership.transferred").ToArrayAsync();
            Assert.Equal(original.Id, Assert.Single(events)["TargetId"]);
            var notification = await db.Set<Dictionary<string, object?>>("WorkspaceTransferNotification").AsNoTracking().SingleAsync();
            Assert.Equal(original.Id, notification["ObjectId"]);
        }
        var models = new[] { "WorkspaceSpace", "WorkspaceFolder", "WorkspaceFolderContent", "Part", "BusinessChange", "WorkspaceTransferNotification" };
        var beforeNoOp = await SnapshotAsync(fixture, models);
        using (var noOp = await FeatureHttpTestRequests.SendAsync(ownerClient, transfer, HttpMethod.Post, "/api/workspace/ownership/transfer", input, OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer)))
        {
            var result = await ReadOkAsync(noOp);
            Assert.True(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal(0, result.GetProperty("changed").GetInt32());
        }
        Assert.Equal(beforeNoOp, await SnapshotAsync(fixture, models));
    }

    // 资源已要求Workspace功能祖先；只移除可选接收实现，精确验证原503边界而非伪造无祖先装配。
    private static async Task WithoutReceivingCapabilityAsync(string model, string plural)
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.RemoveAll<IEntityOwnershipTransferReceiver>(),
            includePart: model == "Part", includeDocument: model == "Document");
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var slug = model.ToLowerInvariant();
        var created = await FeatureHttpTestRequests.CreateAsync(admin, Point(fixture, $"{slug}.{slug}.create"), $"/api/{slug}/{plural}",
            new { ECODE = "NO-RECEIVER-" + model, ENAME = "独立对象" },
            OwnershipDeclaration(fixture, $"{slug}.{slug}.create", Point(fixture, $"{slug}.{slug}.create")));
        var id = BusinessAuditSqlServerHttpApiTests.Id(created);
        using var response = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, $"{slug}.{slug}.transfer"), HttpMethod.Post,
            $"/api/{slug}/{plural}/{id}/transfer", new { recipientId = fixture.ActorId }, OwnershipDeclaration(fixture, $"{slug}.{slug}.transfer", Point(fixture, $"{slug}.{slug}.transfer")));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var failure = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        Assert.Equal("EntityOwnership.ReceivingUnavailable", failure.GetProperty("code").GetString());
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.False(await db.Set<Dictionary<string, object?>>("WorkspaceTransferNotification").AnyAsync());
        var row = await db.Set<Dictionary<string, object?>>(model).AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == id);
        Assert.Equal(fixture.ActorId, row["OwnerId"]);
        Assert.False(await db.Set<Dictionary<string, object?>>("BusinessChange")
            .AnyAsync(row => EF.Property<string>(row, "EventType") == "entity.ownership.transferred"));
    }
}
