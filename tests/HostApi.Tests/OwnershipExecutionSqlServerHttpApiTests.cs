using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Ownership;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.Ownership;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class OwnershipExecutionSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "成本准备沿原三模型转交、通知与NULL旧Folder维护完整消费链，不以准备失败代替声明反例")]
    public async Task OriginalOwnershipSetup_TransfersThreeModelsAndInitializesOneLegacyFolder()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var browse = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Key == "workspace" && point.Kind == "navigation");
        var transfer = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Key == "transfer" && point.ActionScope == "list.rowActions");
        var notification = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Key == "workspace.transfers" && point.Kind == "navigation");
        var create = Kinds.Select(kind => Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace"
            && point.BehaviorKey == "workspace.folder." + kind.Action && point.ActionScope == "list.pageActions")).ToArray();
        var owner = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Ownership319SetupOwner", "准备所属人",
            "ownership319-setup-owner", "准备所属人", [browse, transfer, .. create]);
        var recipient = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Ownership319SetupRecipient", "准备接收人",
            "ownership319-setup-recipient", "准备接收人", browse, notification);
        using var sender = owner.Client;
        using var receiver = recipient.Client;
        var targets = new List<Target>();
        foreach (var kind in Kinds)
            targets.Add(await CreateAsync(fixture, sender, position, kind.Model, kind.Kind, kind.Action, "OWNERSHIP319-SETUP-" + kind.Model));
        using (var changed = await FeatureHttpTestRequests.SendAsync(sender, transfer, HttpMethod.Post, "/api/workspace/ownership/transfer",
            new { position, linkIds = targets.Select(target => target.LinkId).ToArray(), recipientId = recipient.AccountId },
            OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer)))
            Assert.Equal(3, (await ReadOkAsync(changed)).GetProperty("changed").GetInt32());
        using (var received = await FeatureHttpTestRequests.SendAsync(receiver, notification, HttpMethod.Get, "/api/workspace/transfers/notifications?page=1&pageSize=100"))
        {
            var page = await ReadOkAsync(received);
            Assert.Equal(3, page.GetProperty("total").GetInt32());
            Assert.All(page.GetProperty("items").EnumerateArray(), item => Assert.Equal("available", item.GetProperty("status").GetString()));
        }
        var legacy = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "OWNERSHIP319-LEGACY");
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            await db.Set<Dictionary<string, object?>>("WorkspaceFolder").Where(row => EF.Property<string>(row, "EID") == legacy.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string?>(row, "OwnerId"), (string?)null)
                    .SetProperty(row => EF.Property<string?>(row, "OwnerDepartmentId"), (string?)null));
        }
        var maintenance = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Key == "initializeOwnership" && point.ActionScope == "list.custom");
        using (var pending = await FeatureHttpTestRequests.SendAsync(admin, maintenance, HttpMethod.Get, PendingUrl(position, pageSize: 100),
                   dataAccess: OwnershipReadDeclaration(fixture, "workspace.folder.initializeOwnership", maintenance)))
            Assert.Equal(legacy.LinkId, Assert.Single((await ReadOkAsync(pending)).GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        using (var initialized = await FeatureHttpTestRequests.SendAsync(admin, maintenance, HttpMethod.Post, "/api/workspace/ownership/initialize",
            new { position, linkIds = new[] { legacy.LinkId } }, OwnershipDeclaration(fixture, "workspace.folder.initializeOwnership", maintenance)))
            Assert.Equal(1, (await ReadOkAsync(initialized)).GetProperty("changed").GetInt32());
        using (var pending = await FeatureHttpTestRequests.SendAsync(admin, maintenance, HttpMethod.Get, PendingUrl(position, pageSize: 100),
                   dataAccess: OwnershipReadDeclaration(fixture, "workspace.folder.initializeOwnership", maintenance)))
            Assert.Equal(0, (await ReadOkAsync(pending)).GetProperty("total").GetInt32());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实独立Part转交准备要求正式入口声明，普通所属人不能凭功能头省略来源")]
    public async Task IndependentTransfer_RejectsMissingDeclarationAtTheRealOwnerEntry()
        => await MissingDeclarationAsync("Part", "parts");

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实独立Document转交不能凭功能头省略正式来源")]
    public async Task IndependentDocumentTransfer_RejectsMissingDeclarationAtTheRealOwnerEntry()
        => await MissingDeclarationAsync("Document", "documents");

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "AdminOnly资格和专属账号目录保持原入口，真实待初始化读取独立要求正式来源")]
    public async Task IndependentMaintenance_RejectsMissingReadDeclarationWithoutChangingAdminOnlyEligibility()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var create = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part" && point.Key == "create"
            && point.ActionScope == "list.pageActions");
        var requests = new FeatureHttpTestRequests(fixture);
        var created = await FeatureHttpTestRequests.CreateAsync(admin, create, "/api/part/parts",
            new { ECODE = "OWNERSHIP319-PENDING", ENAME = "真实待初始化对象" }, requests.Declaration("part.part.create", create));
        var id = BusinessAuditSqlServerHttpApiTests.Id(created);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.Equal(1, await db.Set<Dictionary<string, object?>>("Part").Where(row => EF.Property<string>(row, "EID") == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string?>(row, "OwnerId"), (string?)null)
                    .SetProperty(row => EF.Property<string?>(row, "OwnerDepartmentId"), (string?)null)));
        }
        var maintenance = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Part"
            && point.Key == "initializeOwnership" && point.ActionScope == "list.custom");
        using (var eligible = await FeatureHttpTestRequests.SendAsync(admin, maintenance, HttpMethod.Get, "/api/part/parts/ownership/eligibility"))
            Assert.True((await ReadOkAsync(eligible)).GetProperty("allowed").GetBoolean());
        using (var accounts = await FeatureHttpTestRequests.SendAsync(admin, maintenance, HttpMethod.Get, "/api/part/parts/ownership/accounts?query=admin"))
            Assert.Equal(HttpStatusCode.OK, accounts.StatusCode);
        using var pending = await FeatureHttpTestRequests.SendAsync(admin, maintenance, HttpMethod.Get,
            "/api/part/parts/ownership/pending?page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.BadRequest, pending.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "独立Part维护读取与NULL-only提交消费正式来源，错误来源不写入且完整对象不重复记审计")]
    public async Task IndependentPartMaintenance_UsesBoundReadAndSubmitAndPreservesNullOnlyWrites()
        => await BoundMaintenanceAsync("Part", "parts");

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "独立Document维护沿相同正式读取/提交合同，保留真实归属与NULL-only语义")]
    public async Task IndependentDocumentMaintenance_UsesBoundReadAndSubmitAndPreservesNullOnlyWrites()
        => await BoundMaintenanceAsync("Document", "documents");

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Workspace真实LID来源保read404/edit403及后项拒绝原子性，混合转交/私有通知/NULL维护各消费自己的正式来源")]
    public async Task WorkspaceOwnership_PreservesReadEditAndAtomicSelectionWithItsActualSources()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var browse = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Key == "workspace" && point.Kind == "navigation");
        var transfer = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Key == "transfer" && point.ActionScope == "list.rowActions");
        var batchTransfer = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Key == "transferSelected" && point.ActionScope == "list.pageActions");
        var maintenance = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Key == "initializeOwnership" && point.ActionScope == "list.custom");
        var notices = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace" && point.Key == "workspace.transfers" && point.Kind == "navigation");
        var create = Kinds.Select(kind => Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace"
            && point.BehaviorKey == "workspace.folder." + kind.Action && point.ActionScope == "list.pageActions")).ToArray();
        var owner = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Ownership319WorkspaceOwner", "混合来源所属人",
            "ownership319-workspace-owner", "混合来源所属人", [browse, transfer, batchTransfer, .. create]);
        var recipient = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Ownership319WorkspaceRecipient", "混合接收人",
            "ownership319-workspace-recipient", "混合接收人", browse, notices);
        using var sender = owner.Client;
        using var receiver = recipient.Client;
        var targets = new List<Target>();
        foreach (var kind in Kinds)
            targets.Add(await CreateAsync(fixture, sender, position, kind.Model, kind.Kind, kind.Action, "OWNERSHIP319-WS-" + kind.Model));
        using var services = fixture.Services.CreateScope();
        var plan = services.ServiceProvider.GetRequiredService<CanonicalModulePlan>();
        var transferSource = Assert.Single(plan.ActionDataAccessCatalog, item => (item.BusinessBinding ?? item.Execution) == "workspace.folder.transfer"
            && item.Surface == "rowActions" && item.ActionKey == "transfer" && item.FeaturePositions.Contains(transfer.Position));
        var maintenanceSource = Assert.Single(plan.ActionDataAccessCatalog, item => item.BusinessBinding == "workspace.folder.initializeOwnership"
            && item.Surface == "featureActions" && item.FeaturePositions.Contains(maintenance.Position));
        var maintenanceReadSource = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "workspace.folder.initializeOwnership"
            && item.Surface == "read" && item.FeaturePositions.Contains(maintenance.Position));
        var wrongSource = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "workspace.content.read"
            && item.ViewKey == "workspace.folder.list" && item.Surface == "read");
        var scope = Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        string Eligibility(IEnumerable<string> ids) => "/api/workspace/ownership/transfer/eligibility?queryScope=" + scope
            + "&selectedIds=" + Uri.EscapeDataString(JsonSerializer.Serialize(ids));
        var input = new { position, linkIds = targets.Select(target => target.LinkId).ToArray(), recipientId = recipient.AccountId };
        using (var missing = await FeatureHttpTestRequests.SendAsync(sender, transfer, HttpMethod.Get, Eligibility(input.linkIds)))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var swapped = await TransferRequestAsync(HttpMethod.Get, Eligibility(input.linkIds), identity: wrongSource))
            Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
        using (var actualButton = await TransferRequestAsync(HttpMethod.Get, Eligibility(input.linkIds)))
            Assert.True((await ReadOkAsync(actualButton)).GetProperty("allowed").GetBoolean());
        var batchSource = Assert.Single(plan.ActionDataAccessCatalog, item => item.BusinessBinding == "workspace.folder.transfer"
            && item.Surface == "pageActions" && item.ActionKey == "transferSelected" && item.FeaturePositions.Contains(batchTransfer.Position));
        using (var batch = await FeatureHttpTestRequests.SendAsync(sender, batchTransfer, HttpMethod.Get, Eligibility(input.linkIds), dataAccess: batchSource))
            Assert.True((await ReadOkAsync(batch)).GetProperty("allowed").GetBoolean());
        using (var mixedIdentity = new HttpRequestMessage(HttpMethod.Get, Eligibility(input.linkIds)))
        {
            mixedIdentity.Headers.Add("X-NewP-Feature", transfer.Position);
            mixedIdentity.Headers.Add("X-NewP-Button", transferSource.Id);
            mixedIdentity.Headers.Add("X-NewP-Data-Declaration", wrongSource.Id);
            using var rejected = await sender.SendAsync(mixedIdentity);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        using (var wrongLink = await TransferRequestAsync(HttpMethod.Get, Eligibility([targets[1].Id])))
            Assert.Equal(HttpStatusCode.NotFound, wrongLink.StatusCode);
        await ConfigureAsync(targets[1], "deny", "allow");
        using (var unreadable = await TransferRequestAsync(HttpMethod.Get, Eligibility(input.linkIds)))
            Assert.Equal(HttpStatusCode.NotFound, unreadable.StatusCode);
        await ConfigureAsync(targets[1], "allow", "deny");
        using (var uneditable = await TransferRequestAsync(HttpMethod.Get, Eligibility(input.linkIds)))
            Assert.Equal(HttpStatusCode.Forbidden, uneditable.StatusCode);
        await ConfigureAsync(targets[1], "allow", "allow");
        using (var allowed = await TransferRequestAsync(HttpMethod.Get, Eligibility(input.linkIds)))
            Assert.True((await ReadOkAsync(allowed)).GetProperty("allowed").GetBoolean());
        await ConfigureAsync(targets[2], "allow", "deny");
        using (var laterDenied = await TransferRequestAsync(HttpMethod.Post, "/api/workspace/ownership/transfer", input))
            Assert.Equal(HttpStatusCode.Forbidden, laterDenied.StatusCode);
        foreach (var target in targets) Assert.Equal(owner.AccountId, (await RowAsync(fixture, target))["OwnerId"]);
        var db = services.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.False(await db.Set<Dictionary<string, object?>>("WorkspaceTransferNotification").AnyAsync());
        await ConfigureAsync(targets[2], "allow", "allow");
        using (var transferred = await TransferRequestAsync(HttpMethod.Post, "/api/workspace/ownership/transfer", input))
            Assert.Equal(3, (await ReadOkAsync(transferred)).GetProperty("changed").GetInt32());
        using (var received = await FeatureHttpTestRequests.SendAsync(receiver, notices, HttpMethod.Get, "/api/workspace/transfers/notifications?page=1&pageSize=100"))
        {
            var page = await ReadOkAsync(received);
            Assert.Equal(3, page.GetProperty("total").GetInt32());
            Assert.DoesNotContain(targets[1].Name, page.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(targets[1].Id, page.GetRawText(), StringComparison.Ordinal);
        }
        foreach (var target in targets) await EntityOwnershipFailureSqlServerHttpApiTests.ClearOwnershipAsync(fixture, target);
        using (var missing = await FeatureHttpTestRequests.SendAsync(admin, maintenance, HttpMethod.Get, PendingUrl(position)))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var pending = await FeatureHttpTestRequests.SendAsync(admin, maintenance, HttpMethod.Get, PendingUrl(position), dataAccess: maintenanceReadSource))
            Assert.Equal(3, (await ReadOkAsync(pending)).GetProperty("total").GetInt32());
        using (var initialized = await FeatureHttpTestRequests.SendAsync(admin, maintenance, HttpMethod.Post, "/api/workspace/ownership/initialize",
                   new { position, input.linkIds }, maintenanceSource))
            Assert.Equal(3, (await ReadOkAsync(initialized)).GetProperty("changed").GetInt32());
        foreach (var target in targets) Assert.Equal(owner.AccountId, (await RowAsync(fixture, target))["OwnerId"]);

        // 新按钮读取与真实混合LID来源一起提交；不能由测试辅助器回退成旧执行声明。
        async Task<HttpResponseMessage> TransferRequestAsync(HttpMethod method, string path, object? payload = null,
            ActionDataAccessRequirement? identity = null)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-NewP-Feature", transfer.Position);
            request.Headers.Add("X-NewP-Button", (identity ?? transferSource).Id);
            if (payload is not null) request.Content = JsonContent.Create(payload);
            return await sender.SendAsync(request);
        }

        async Task ConfigureAsync(Target target, string read, string edit)
        {
            var slug = target.Model.ToLowerInvariant();
            var resource = slug + "." + slug;
            var feature = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == target.Model && point.Key == "authorize" && point.ActionScope == "list.rowActions");
            using var configured = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Put,
                $"/api/{slug}/{(target.Model == "Part" ? "parts" : "documents")}/{target.Id}/permissions", new DataScopeObjectDto(resource, target.Id,
                    [new("ownership319-ws-owner-" + target.Id, "user", owner.AccountId, 0, new Dictionary<string, string> { ["read"] = read, ["edit"] = edit }),
                     new("ownership319-ws-recipient-" + target.Id, "user", recipient.AccountId, 0, new Dictionary<string, string> { ["read"] = "deny", ["edit"] = "deny" })]),
                OwnedResourceReadHttpRequests.Button(fixture, feature, resource + ".authorize"));
            Assert.True(configured.StatusCode == HttpStatusCode.NoContent, await configured.Content.ReadAsStringAsync());
        }
    }

    private static async Task BoundMaintenanceAsync(string model, string plural)
    {
        var slug = model.ToLowerInvariant();
        var resource = slug + "." + slug;
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includePart: model == "Part", includeDocument: model == "Document");
        using var admin = await fixture.LoginAdministratorAsync();
        var department = await InitializeAsync(fixture, admin);
        var create = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == model && point.Key == "create" && point.ActionScope == "list.pageActions");
        var requests = new FeatureHttpTestRequests(fixture);
        var created = await FeatureHttpTestRequests.CreateAsync(admin, create, $"/api/{slug}/{plural}",
            new { ECODE = "OWNERSHIP319-NULL", ENAME = "真实NULL-only对象" }, requests.Declaration(resource + ".create", create));
        var target = new Target(model, BusinessAuditSqlServerHttpApiTests.Id(created), "", "真实NULL-only对象");
        var before = await RowAsync(fixture, target);
        await EntityOwnershipFailureSqlServerHttpApiTests.ClearOwnershipAsync(fixture, target);
        var feature = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == model && point.Key == "initializeOwnership" && point.ActionScope == "list.custom");
        using var services = fixture.Services.CreateScope();
        var plan = services.ServiceProvider.GetRequiredService<CanonicalModulePlan>();
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => (item.BusinessBinding ?? item.Execution) == resource + ".initializeOwnership"
            && item.ViewKey == resource + ".list" && item.Surface == "featureActions" && item.ActionKey == "initializeOwnership"
            && item.FeaturePositions.Contains(feature.Position));
        var wrong = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == resource + ".list" && item.Surface == "read");
        var readDeclaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == resource + ".initializeOwnership"
            && item.ViewKey == resource + ".initializeOwnership" && item.Surface == "read");
        var path = $"/api/{slug}/{plural}/ownership";
        var rowFeature = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == model
            && point.Key == "initializeOwnership" && point.ActionScope == "list.rowActions");
        var rowButton = Assert.Single(plan.ActionDataAccessCatalog, item => (item.BusinessBinding ?? item.Execution) == resource + ".initializeOwnership"
            && item.Surface == "rowActions" && item.FeaturePositions.Contains(rowFeature.Position));
        using (var missingButton = await FeatureHttpTestRequests.SendAsync(admin, rowFeature, HttpMethod.Get, path + "/" + target.Id + "/status"))
            Assert.Equal(HttpStatusCode.BadRequest, missingButton.StatusCode);
        using (var borrowed = await MaintenanceStatusAsync(wrong))
            Assert.Equal(HttpStatusCode.BadRequest, borrowed.StatusCode);
        using (var readAsButton = await MaintenanceStatusAsync(readDeclaration))
            Assert.Equal(HttpStatusCode.BadRequest, readAsButton.StatusCode);
        using (var actualButton = await MaintenanceStatusAsync(rowButton))
            Assert.True((await ReadOkAsync(actualButton)).GetProperty("missing").GetBoolean());
        var pendingFeature = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == model
            && point.Key == "initializeOwnershipPending" && point.ActionScope == "list.pageActions");
        Assert.Equal(new[] { feature.Position, rowFeature.Position, pendingFeature.Position }.Order(StringComparer.Ordinal),
            readDeclaration.FeaturePositions.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(wrong.FeaturePositions, position => readDeclaration.FeaturePositions.Contains(position));

        using (var absentSource = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path + "/pending"))
            Assert.Equal(HttpStatusCode.BadRequest, absentSource.StatusCode);
        using (var swapped = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path + "/pending", dataAccess: wrong))
            Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
        using (var buttonAsRead = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path + "/pending", dataAccess: declaration))
            Assert.Equal(HttpStatusCode.BadRequest, buttonAsRead.StatusCode);
        using (var pending = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path + "/pending?page=1&pageSize=100", dataAccess: readDeclaration))
        {
            var page = await ReadOkAsync(pending);
            Assert.Equal(1, page.GetProperty("total").GetInt32());
            Assert.Equal(target.Id, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        }
        using (var missing = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path + "/" + target.Id + "/status", dataAccess: declaration))
            Assert.True((await ReadOkAsync(missing)).GetProperty("missing").GetBoolean());
        using (var unavailable = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path + "/absent/status", dataAccess: declaration))
            Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
        using (var swapped = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post, path + "/initialize", new { ids = new[] { target.Id } }, wrong))
            Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
        Assert.Null((await RowAsync(fixture, target))["OwnerId"]);
        using (var initialized = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post, path + "/initialize", new { ids = new[] { target.Id } }, declaration))
            Assert.Equal(1, (await ReadOkAsync(initialized)).GetProperty("changed").GetInt32());
        var after = await RowAsync(fixture, target);
        Assert.Equal(fixture.ActorId, after["OwnerId"]);
        Assert.Equal(department, after["OwnerDepartmentId"]);
        Assert.Equal(before["CreatedBy"], after["CreatedBy"]);
        Assert.Equal(before["CreatedAt"], after["CreatedAt"]);
        Assert.Equal(before["UpdatedBy"], after["UpdatedBy"]);
        Assert.Equal(before["UpdatedAt"], after["UpdatedAt"]);
        var db = services.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var events = await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
            .CountAsync(row => EF.Property<string>(row, "EventType") == "entity.ownership.backfilled");
        Assert.Equal(1, events);
        using (var unchanged = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post, path + "/" + target.Id, new { }, declaration))
            Assert.False((await ReadOkAsync(unchanged)).GetProperty("changed").GetBoolean());
        Assert.Equal(events, await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
            .CountAsync(row => EF.Property<string>(row, "EventType") == "entity.ownership.backfilled"));
        using (var pending = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Get, path + "/pending", dataAccess: readDeclaration))
            Assert.Equal(0, (await ReadOkAsync(pending)).GetProperty("total").GetInt32());

        // 状态与提交按钮身份来自实际行入口；原待补齐查询仍独立消费自己的SQL读取来源。
        async Task<HttpResponseMessage> MaintenanceStatusAsync(ActionDataAccessRequirement identity)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path + "/" + target.Id + "/status");
            request.Headers.Add("X-NewP-Feature", rowFeature.Position);
            request.Headers.Add("X-NewP-Button", identity.Id);
            return await admin.SendAsync(request);
        }
    }

    private static async Task MissingDeclarationAsync(string model, string plural)
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var browse = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace"
            && point.Kind == "navigation" && point.Key == "workspace" && point.BehaviorKey == "workspace.spaces.access");
        var transfer = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == model
            && point.Key == "transfer" && point.ActionScope == "list.rowActions");
        var create = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace"
            && point.BehaviorKey == "workspace.folder.create" + model && point.ActionScope == "list.pageActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Ownership319Owner", "转交来源所属人",
            "ownership319-owner", "转交来源所属人", browse, transfer, create);
        using var ordinary = actor.Client;
        var target = await CreateAsync(fixture, ordinary, position, model, plural, "create" + model, "OWNERSHIP319-SOURCE");
        Assert.Equal(actor.AccountId, (await RowAsync(fixture, target))["OwnerId"]);
        using var missing = await FeatureHttpTestRequests.SendAsync(ordinary, transfer, HttpMethod.Get,
            $"/api/{model.ToLowerInvariant()}/{plural}/{target.Id}/transfer/eligibility");
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "独立Part正式prepare/submit保留当前所属人、edit隐含read与接收者不可读边界，不能串用读取声明")]
    public async Task IndependentTransfer_UsesItsBoundSourceAndRechecksOwnerAndEditBeforeReceiving()
        => await BoundTransferAsync("Part", "parts");

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "独立Document正式prepare/submit不改变原owner/edit、完整读取依赖或接收资格")]
    public async Task IndependentDocumentTransfer_UsesItsBoundSourceAndRechecksOwnerAndEditBeforeReceiving()
        => await BoundTransferAsync("Document", "documents");

    private static async Task BoundTransferAsync(string model, string plural)
    {
        var slug = model.ToLowerInvariant();
        var resource = slug + "." + slug;
        var receivingFault = new TransferReceivingFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.RemoveAll<IEntityOwnershipTransferReceiver>();
            services.AddScoped<IEntityOwnershipTransferReceiver>(provider => new TransferReceiver(
                ActivatorUtilities.CreateInstance<WorkspaceOwnershipTransferReceiver>(provider), receivingFault));
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var browse = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace"
            && point.Kind == "navigation" && point.Key == "workspace");
        var transfer = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == model
            && point.Key == "transfer" && point.ActionScope == "list.rowActions");
        var create = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == "Workspace"
            && point.BehaviorKey == "workspace.folder.create" + model && point.ActionScope == "list.pageActions");
        var manage = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == model
            && point.Key == "authorize" && point.ActionScope == "list.rowActions");
        var owner = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Ownership319BoundOwner", "正式来源所属人",
            "ownership319-bound-owner", "正式来源所属人", browse, transfer, create);
        var recipient = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Ownership319BoundRecipient", "不可读接收人",
            "ownership319-bound-recipient", "不可读接收人", browse, transfer);
        using var sender = owner.Client;
        using var receiver = recipient.Client;
        var target = await CreateAsync(fixture, sender, position, model, plural, "create" + model, "OWNERSHIP319-BOUND");
        using var services = fixture.Services.CreateScope();
        var plan = services.ServiceProvider.GetRequiredService<CanonicalModulePlan>();
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => (item.BusinessBinding ?? item.Execution) == resource + ".transfer"
            && item.Resources.SequenceEqual([resource])
            && item.FeaturePositions.Contains(transfer.Position));
        var listPoint = Assert.Single(fixture.FeatureCatalog, point => point.ModuleId == model
            && point.Key == slug + "." + plural && point.Kind == "navigation");
        var wrongSource = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == resource + ".list"
            && item.FeaturePositions.Contains(listPoint.Position));
        var path = $"/api/{slug}/{plural}/{target.Id}/transfer";
        var input = new { recipientId = recipient.AccountId };
        await ConfigureAsync("allow", "allow");
        using (var missing = await FeatureHttpTestRequests.SendAsync(sender, transfer, HttpMethod.Get, path + "/eligibility"))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var otherModel = model == "Part" ? "Document" : "Part";
        var otherSlug = otherModel.ToLowerInvariant();
        var borrowed = Assert.Single(plan.ActionDataAccessCatalog, item => item.ModuleId == otherModel
            && item.ViewKey == otherSlug + "." + otherSlug + ".list" && item.Surface == "rowActions" && item.ActionKey == "transfer");
        using (var crossed = await SendTransferAsync(sender, HttpMethod.Get, "/eligibility", identity: borrowed))
            Assert.Equal(HttpStatusCode.BadRequest, crossed.StatusCode);

        using (var wrong = await SendTransferAsync(sender, HttpMethod.Get, "/eligibility", identity: wrongSource))
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using (var wrong = await SendTransferAsync(sender, HttpMethod.Post, payload: input, identity: wrongSource))
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using (var prepared = await SendTransferAsync(sender, HttpMethod.Get, "/eligibility"))
            Assert.True((await ReadOkAsync(prepared)).GetProperty("allowed").GetBoolean());
        using (var candidates = await SendTransferAsync(sender, HttpMethod.Get, "/accounts?query=ownership319-bound-recipient"))
            Assert.Equal(recipient.AccountId, Assert.Single((await ReadOkAsync(candidates)).EnumerateArray()).GetProperty("accountId").GetString());
        using (var notOwner = await SendTransferAsync(admin, HttpMethod.Post, payload: input))
            Assert.Equal(HttpStatusCode.Forbidden, notOwner.StatusCode);

        // 已准备不形成允许凭据；submit原位置重读当前edit及其read依赖，失败不产生接收事实。
        foreach (var permission in new[] { (Read: "allow", Edit: "deny"), (Read: "deny", Edit: "allow") })
        {
            await ConfigureAsync(permission.Read, permission.Edit);
            using var denied = await SendTransferAsync(sender, HttpMethod.Post, payload: input);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal(owner.AccountId, (await RowAsync(fixture, target))["OwnerId"]);
            var db = services.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.False(await db.Set<Dictionary<string, object?>>("WorkspaceTransferNotification").AnyAsync());
        }
        await ConfigureAsync("allow", "allow");
        // 相同错误在平台目标检查之后从真实Receive边界抛出时，不能被目标拒绝403映射吞掉。
        var beforeReceiveFailure = await EntityOwnershipTransferSqlServerHttpApiTests.SnapshotAsync(fixture,
            model, "WorkspaceSpace", "WorkspaceFolder", "WorkspaceFolderContent", "BusinessChange", "WorkspaceTransferNotification");
        receivingFault.Failure = new ActionDataAccessException(404, "StandardCrud.NotFound");
        using (var businessFailure = await SendTransferAsync(sender, HttpMethod.Post, payload: input))
        {
            Assert.Equal(HttpStatusCode.NotFound, businessFailure.StatusCode);
            Assert.Equal("StandardCrud.NotFound", (await businessFailure.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        Assert.Equal(beforeReceiveFailure, await EntityOwnershipTransferSqlServerHttpApiTests.SnapshotAsync(fixture,
            model, "WorkspaceSpace", "WorkspaceFolder", "WorkspaceFolderContent", "BusinessChange", "WorkspaceTransferNotification"));
        receivingFault.Failure = null;
        using (var succeeded = await SendTransferAsync(sender, HttpMethod.Post, payload: input))
            Assert.Equal(1, (await ReadOkAsync(succeeded)).GetProperty("changed").GetInt32());
        Assert.Equal(recipient.AccountId, (await RowAsync(fixture, target))["OwnerId"]);
        using (var formerOwner = await SendTransferAsync(sender, HttpMethod.Get, "/eligibility"))
            Assert.Equal(HttpStatusCode.Forbidden, formerOwner.StatusCode);
        using (var unreadableReceiver = await SendTransferAsync(receiver, HttpMethod.Get, "/eligibility"))
            Assert.Equal(HttpStatusCode.Forbidden, unreadableReceiver.StatusCode);

        // 强制走实际新按钮头；即使authoring尚未迁移也必须到真实API产生RED，不能由测试自动回退旧声明。
        async Task<HttpResponseMessage> SendTransferAsync(HttpClient client, HttpMethod method, string suffix = "", object? payload = null,
            ActionDataAccessRequirement? identity = null)
        {
            using var request = new HttpRequestMessage(method, path + suffix);
            request.Headers.Add("X-NewP-Feature", transfer.Position);
            request.Headers.Add("X-NewP-Button", (identity ?? declaration).Id);
            if (payload is not null) request.Content = JsonContent.Create(payload);
            return await client.SendAsync(request);
        }

        async Task ConfigureAsync(string read, string edit)
        {
            using var configured = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put,
                $"/api/{slug}/{plural}/{target.Id}/permissions", new DataScopeObjectDto(resource, target.Id,
                    [new("ownership319-owner", "user", owner.AccountId, 0, new Dictionary<string, string> { ["read"] = read, ["edit"] = edit }),
                     new("ownership319-recipient", "user", recipient.AccountId, 0, new Dictionary<string, string> { ["read"] = "deny", ["edit"] = "deny" })]),
                OwnedResourceReadHttpRequests.Button(fixture, manage, resource + ".authorize"));
            Assert.Equal(HttpStatusCode.NoContent, configured.StatusCode);
        }
    }
    private sealed class TransferReceivingFault
    {
        internal Exception? Failure;
    }

    // 注入点是接收模块稳定协作端口；准备与无故障接收仍调用真实Workspace业务，不替代目标或权限事实。
    private sealed class TransferReceiver(IEntityOwnershipTransferReceiver actual, TransferReceivingFault fault) : IEntityOwnershipTransferReceiver
    {
        public ValueTask PrepareAsync(CancellationToken cancellationToken = default) => actual.PrepareAsync(cancellationToken);
        public ValueTask ReceiveAsync(AccountOwnership recipient, IReadOnlyCollection<EntityOwnershipTransferTarget> targets,
            CancellationToken cancellationToken = default) => fault.Failure is { } failure
                ? ValueTask.FromException(failure) : actual.ReceiveAsync(recipient, targets, cancellationToken);
    }

}
