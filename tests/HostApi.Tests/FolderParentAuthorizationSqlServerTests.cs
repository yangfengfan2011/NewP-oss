using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.HostApi.Persistence;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class FolderParentAuthorizationSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "普通Folder明确复制父直接项不需父authorize，保绝对期限和动态主体，新对象不可读仍安全成功")]
    public async Task CreateFolder_CopiesParentDirectConfigurationWithoutAuthorize_AndReturnsUnreadableSuccess()
    {
        var ticks = new AuditTicks();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.Replace(
            ServiceDescriptor.Scoped<IEntityAuditStampProvider>(provider => new AdvancingStamp(new HttpContextEntityAuditStampProvider(
                provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), provider.GetRequiredService<TimeProvider>()), ticks))), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var departmentId = await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ParentInit298", "父授权初始化", "parent-init298", "新建文件夹账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.create"));
        using var ordinary = actor.Client;
        var parentLinkId = position.LinkIds[^1];
        string parentId;
        using (var scope = fixture.Services.CreateScope())
            parentId = (string)(await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>()
                .Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "LID") == parentLinkId))["CEID"]!;
        DataScopeDirectSubjectDto[] subjects = [
            new("Parent298Creator", "creator", null, 10, new Dictionary<string, string> { ["read"] = "deny" }, ExpiresAt: "2099-01-01T00:00:00Z"),
            new("Parent298Actor", "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow", ["authorize"] = "deny" }),
            new("Parent298Department", "department", departmentId, 7, new Dictionary<string, string> { ["history"] = "allow" }, new(true), "2099-02-01T00:00:00Z")
        ];
        var parentOccurrence = position with { LinkIds = position.LinkIds[..^1] };
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.authorize"), HttpMethod.Put,
            PermissionsPath(parentOccurrence, position.LinkIds[^1]), new DataScopeObjectConfiguration("workspace.folder", parentId, subjects)))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using var created = await FeatureHttpTestRequests.SendAsync(ordinary, Point(fixture, "workspace.folder.create"), HttpMethod.Post,
            "/api/workspace/content/folders", new { position, values = new { ENAME = "InheritedUnreadable298", Description = "创建后按新对象creator求值" }, copyParentAuthorization = true });
        var receipt = await ReadOkAsync(created);
        Target child;
        Dictionary<string, object?> childRow;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            childRow = await db.Set<Dictionary<string, object?>>("WorkspaceFolder").AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "ENAME") == "InheritedUnreadable298");
            var id = (string)childRow["EID"]!;
            var link = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "CEID") == id && EF.Property<string>(row, "PEID") == parentId);
            child = new("WorkspaceFolder", id, (string)link["LID"]!, "InheritedUnreadable298");
        }
        var configuration = await FeatureHttpTestRequests.ReadAsync(admin, Point(fixture, "workspace.folder.authorize"), PermissionsPath(position, child.LinkId));
        var copied = configuration.GetProperty("subjects").EnumerateArray().ToArray();
        Assert.Equal(3, copied.Length);
        foreach (var expected in subjects)
        {
            var actual = Assert.Single(copied, item => item.GetProperty("type").GetString() == expected.Type);
            Assert.DoesNotContain(actual.GetProperty("id").GetString(), subjects.Select(subject => subject.Id));
            Assert.Equal(expected.SubjectId, actual.GetProperty("subjectId").GetString());
            Assert.Equal(expected.Priority, actual.GetProperty("priority").GetInt64());
            Assert.Equal(expected.ExpiresAt, actual.TryGetProperty("expiresAt", out var expiry) ? expiry.GetString() : null);
            foreach (var (action, opinion) in expected.Actions) Assert.Equal(opinion, actual.GetProperty("actions").GetProperty(action).GetString());
        }
        Assert.True(Assert.Single(copied, item => item.GetProperty("type").GetString() == "department")
            .GetProperty("matchOptions").GetProperty("includeDescendants").GetBoolean());
        Assert.Equal(actor.AccountId, childRow["CreatedBy"]);
        Assert.True(receipt.GetProperty("succeeded").GetBoolean());
        Assert.False(receipt.TryGetProperty("id", out _));
        Assert.False(receipt.TryGetProperty("fields", out _));
        Assert.DoesNotContain(child.Id, receipt.GetRawText(), StringComparison.Ordinal);
        using var unreadable = await FeatureHttpTestRequests.SendAsync(ordinary, Point(fixture, "workspace.spaces.access"), HttpMethod.Get,
            "/api/workspace/folders/" + child.Id);
        Assert.Equal(HttpStatusCode.NotFound, unreadable.StatusCode);
        using var finalScope = fixture.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var entries = await finalDb.Set<Dictionary<string, object?>>("DataScopeObjectSubject").AsNoTracking()
            .Where(row => EF.Property<string>(row, "ObjectId") == child.Id).ToArrayAsync();
        Assert.Equal(3, entries.Length);
        foreach (var entry in entries)
        {
            Assert.Equal(childRow["CreatedBy"], entry["CreatedBy"]);
            Assert.Equal(childRow["CreatedAt"], entry["CreatedAt"]);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "旧请求与取消复制不初始化，空间根没有普通父源，空父配置不造授权变更")]
    public async Task CreateFolder_OmittedFalseRootAndEmptyParent_DoNotCreateDirectConfiguration()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var parent = await FindFolderAsync(fixture, "归属维护目录");
        DataScopeDirectSubjectDto[] subjects = [new("CompatibilityParent298", "creator", null, 4, new Dictionary<string, string> { ["history"] = "allow" })];
        await SaveAsync(fixture, admin, position with { LinkIds = [] }, parent, subjects);
        await CreateWithOptionAsync("Omitted298", new { position, values = new { ENAME = "Omitted298" } });
        await CreateWithOptionAsync("False298", new { position, values = new { ENAME = "False298" }, copyParentAuthorization = false });
        var root = position with { LinkIds = [] };
        // 正式空间根不是普通对象授权目标；显式true仍不能把它当作普通父来源。
        await CreateWithOptionAsync("AtRoot298", new { position = root, values = new { ENAME = "AtRoot298" }, copyParentAuthorization = true });
        var emptyParent = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "EmptyParent298");
        var emptyPosition = position with { LinkIds = [.. position.LinkIds, emptyParent.LinkId] };
        await CreateWithOptionAsync("EmptyChild298", new { position = emptyPosition, values = new { ENAME = "EmptyChild298" }, copyParentAuthorization = true });

        async Task CreateWithOptionAsync(string name, object input)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.create"), HttpMethod.Post,
                "/api/workspace/content/folders", input);
            await ReadOkAsync(response);
            var created = await FindFolderAsync(fixture, name);
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.False(await db.Set<Dictionary<string, object?>>("DataScopeObjectSubject").AsNoTracking()
                .AnyAsync(row => EF.Property<string>(row, "ObjectId") == created.Id));
            Assert.False(await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
                .AnyAsync(row => EF.Property<string>(row, "EventType") == "workspace.folder.parentAuthorizationInitialized"
                    && EF.Property<string>(row, "TargetId") == created.Id));
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "浅副本只从目标父初始化新根，旧省略和false不复制，源ACL与共享子对象配置不变")]
    public async Task ShallowCopy_InitializesOnlyNewRootFromTargetParent_AndPreservesLegacyOmission()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var source = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "Source298");
        var sourcePosition = position with { LinkIds = [.. position.LinkIds, source.LinkId] };
        var shared = await CreateAsync(fixture, admin, sourcePosition, "WorkspaceFolder", "folders", "create", "Shared298");
        var destination = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "Destination298");
        var targetPosition = position with { LinkIds = [.. position.LinkIds, destination.LinkId] };
        DataScopeDirectSubjectDto[] sourceSubjects = [new("ShallowSource298", "creator", null, 3, new Dictionary<string, string> { ["edit"] = "deny" })];
        DataScopeDirectSubjectDto[] sharedSubjects = [new("ShallowShared298", "owner", null, 9, new Dictionary<string, string> { ["read"] = "deny" })];
        DataScopeDirectSubjectDto[] targetSubjects = [new("ShallowTarget298", "creator", null, 17, new Dictionary<string, string> { ["history"] = "allow" }, ExpiresAt: "2099-03-01T00:00:00Z")];
        await SaveAsync(fixture, admin, position, source, sourceSubjects);
        await SaveAsync(fixture, admin, sourcePosition, shared, sharedSubjects);
        await SaveAsync(fixture, admin, position, destination, targetSubjects);
        var sharedBefore = JsonSerializer.Serialize(await RowAsync(fixture, shared));
        await CopyAsync("LegacyShallow298", null);
        await CopyAsync("InheritedShallow298", true);
        await CopyAsync("OptOutShallow298", false);
        Assert.Equal(sharedBefore, JsonSerializer.Serialize(await RowAsync(fixture, shared)));
        var sourceAfter = await FeatureHttpTestRequests.ReadAsync(admin, Point(fixture, "workspace.folder.authorize"), PermissionsPath(position, source.LinkId));
        Assert.Equal("ShallowSource298", Assert.Single(sourceAfter.GetProperty("subjects").EnumerateArray()).GetProperty("id").GetString());
        var sharedAfter = await FeatureHttpTestRequests.ReadAsync(admin, Point(fixture, "workspace.folder.authorize"), PermissionsPath(sourcePosition, shared.LinkId));
        Assert.Equal("ShallowShared298", Assert.Single(sharedAfter.GetProperty("subjects").EnumerateArray()).GetProperty("id").GetString());

        async Task CopyAsync(string name, bool? copyParent)
        {
            var body = new Dictionary<string, object?> { ["mode"] = "copy", ["sourcePosition"] = position,
                ["linkIds"] = new[] { source.LinkId }, ["targetPosition"] = targetPosition, ["name"] = name };
            if (copyParent.HasValue) body["copyParentAuthorization"] = copyParent.Value;
            using var response = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.pasteFolderCopy"), HttpMethod.Post,
                "/api/workspace/content/folders/copy", body);
            var receipt = await ReadOkAsync(response);
            Assert.True(receipt.GetProperty("succeeded").GetBoolean());
            Assert.False(receipt.TryGetProperty("id", out _));
            var copied = await FindFolderAsync(fixture, name);
            var configuration = await FeatureHttpTestRequests.ReadAsync(admin, Point(fixture, "workspace.folder.authorize"), PermissionsPath(targetPosition, copied.LinkId));
            var entries = configuration.GetProperty("subjects").EnumerateArray().ToArray();
            if (copyParent == true)
            {
                var entry = Assert.Single(entries);
                Assert.NotEqual("ShallowTarget298", entry.GetProperty("id").GetString());
                Assert.Equal("creator", entry.GetProperty("type").GetString());
                Assert.Equal(17, entry.GetProperty("priority").GetInt64());
                Assert.Equal("2099-03-01T00:00:00Z", entry.GetProperty("expiresAt").GetString());
                Assert.Equal("allow", entry.GetProperty("actions").GetProperty("history").GetString());
                Assert.False(entry.GetProperty("actions").TryGetProperty("edit", out _));
            }
            else Assert.Empty(entries);
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var children = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
                .Where(row => EF.Property<string>(row, "PEID") == copied.Id).ToArrayAsync();
            var link = Assert.Single(children);
            Assert.Equal(shared.Id, link["CEID"]);
            Assert.Equal(true, link["IsScopeNeutral"]);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "父初始化失败与真实请求Flush取消使新Folder、直接项、动作、图和事件原子回滚")]
    public async Task Initialization_AuditFailuresAndRequestCancellation_RollBackFolderAclAndGraphTogether()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var parent = await FindFolderAsync(fixture, "归属维护目录");
        var source = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "RollbackSource298");
        await CreateAsync(fixture, admin, position with { LinkIds = [.. position.LinkIds, source.LinkId] }, "WorkspaceFolder", "folders", "create", "RollbackShared298");
        await SaveAsync(fixture, admin, position with { LinkIds = [] }, parent,
            [new("RollbackParent298", "creator", null, 4, new Dictionary<string, string> { ["history"] = "allow" })]);
        foreach (var shallow in new[] { false, true })
        foreach (var cancel in new[] { false, true })
        {
            var before = await SnapshotAsync(fixture);
            var reached = false; var flushed = false;
            using var cancellation = new CancellationTokenSource();
            var eventType = shallow ? "workspace.folder.shallowCopied" : cancel ? "workspace.folder.parentAuthorizationInitialized" : "dataScope.object.changed";
            fixture.Fault.AfterRegister = async (change, token) =>
            {
                if (change.EventType != eventType) return;
                reached = true;
                if (!cancel) throw new InvalidOperationException("injected parent authorization initialization failure");
                var context = fixture.Services.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>().HttpContext!;
                await context.RequestServices.GetRequiredService<IPlatformTransactionExecutor>().FlushAsync(token);
                flushed = true;
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            };
            try
            {
                object body = shallow
                    ? new { mode = "copy", sourcePosition = position, linkIds = new[] { source.LinkId }, targetPosition = position, name = "RollbackInherited298", copyParentAuthorization = true }
                    : new { position, values = new { ENAME = "RollbackInherited298" }, copyParentAuthorization = true };
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/content/folders" + (shallow ? "/copy" : ""))
                { Content = JsonContent.Create(body, options: new JsonSerializerOptions()) };
                request.Headers.Add("X-NewP-Feature", Point(fixture, "workspace.folder." + (shallow ? "pasteFolderCopy" : "create")).Position);
                if (cancel)
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                    {
                        using var response = await admin.SendAsync(request, cancellation.Token);
                        Assert.Fail($"请求未进入取消点，HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                    });
                else
                {
                    using var response = await admin.SendAsync(request);
                    Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                }
            }
            finally { fixture.Fault.AfterRegister = null; }
            Assert.True(reached, eventType);
            if (cancel) Assert.True(flushed, eventType);
            Assert.Equal(before, await SnapshotAsync(fixture));
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "初始化真实事件从新Folder和父履历关联去重；固定快照安全，不可读新对象不泄露原ID或ACL")]
    public async Task InitializationHistory_AssociatesParentAndNewFolder_AndProtectsUnreadableTarget()
    {
        var ticks = new AuditTicks();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.Replace(
            ServiceDescriptor.Scoped<IEntityAuditStampProvider>(provider => new AdvancingStamp(new HttpContextEntityAuditStampProvider(
                provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(), provider.GetRequiredService<TimeProvider>()), ticks))), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var parent = await FindFolderAsync(fixture, "归属维护目录");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "InitHistory298", "初始化履历", "init-history298", "初始化履历账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.history"));
        using var ordinary = actor.Client;
        await SaveAsync(fixture, admin, position with { LinkIds = [] }, parent,
            [new("HistoryParent298", "creator", null, 4, new Dictionary<string, string> { ["history"] = "allow" })]);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.create"), HttpMethod.Post,
            "/api/workspace/content/folders", new { position, values = new { ENAME = "HistoryInitialized298" }, copyParentAuthorization = true }))
            await ReadOkAsync(response);
        var child = await FindFolderAsync(fixture, "HistoryInitialized298");
        var row = await RowAsync(fixture, child);
        using (var scope = fixture.Services.CreateScope())
        {
            var change = await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
                .SingleAsync(item => EF.Property<string>(item, "TargetId") == child.Id && EF.Property<string>(item, "EventType") == "workspace.folder.parentAuthorizationInitialized");
            Assert.Equal(row["CreatedBy"], change["ActorId"]); Assert.Equal(row["CreatedAt"], change["OccurredAt"]);
            var facts = JsonSerializer.Deserialize<JsonElement>((string)change["FieldsJson"]!);
            foreach (var (field, id) in new[] { ("PEID", parent.Id), ("CEID", child.Id) })
            {
                var fact = Assert.Single(facts.EnumerateArray(), value => value.GetProperty("Field").GetString() == field).GetProperty("After");
                Assert.Equal("workspace.folder", fact.GetProperty("Kind").GetString()); Assert.Equal(id, fact.GetProperty("Value").GetString());
            }
        }
        var parentPage = await HistoryAsync(position with { LinkIds = [] }, parent.LinkId);
        var parentItem = Assert.Single(parentPage.GetProperty("items").EnumerateArray());
        Assert.Equal(1, parentPage.GetProperty("total").GetInt32());
        var childPage = await HistoryAsync(position, child.LinkId);
        var childItem = Assert.Single(childPage.GetProperty("items").EnumerateArray());
        Assert.Equal(1, childPage.GetProperty("total").GetInt32());
        Assert.Equal(parentItem.GetProperty("id").GetString(), childItem.GetProperty("id").GetString());
        Assert.Equal(child.Name, childItem.GetProperty("target").GetProperty("name").GetString());
        Assert.Equal("historical", childItem.GetProperty("target").GetProperty("nameSource").GetString());
        AssertSafe(parentItem); AssertSafe(childItem);
        await SaveAsync(fixture, admin, position, child,
            [new("HistoryDeny298", "user", actor.AccountId, 10, new Dictionary<string, string> { ["read"] = "deny" })]);
        var hidden = Assert.Single((await HistoryAsync(position with { LinkIds = [] }, parent.LinkId)).GetProperty("items").EnumerateArray());
        Assert.Equal("unavailable", hidden.GetProperty("target").GetProperty("nameSource").GetString());
        Assert.Null(hidden.GetProperty("target").GetProperty("name").GetString()); AssertSafe(hidden);

        Task<JsonElement> HistoryAsync(Position occurrence, string linkId) => FeatureHttpTestRequests.ReadAsync(ordinary,
            Point(fixture, "workspace.folder.history"), PermissionsPath(occurrence, linkId).Replace("/permissions?", "/history?", StringComparison.Ordinal)
                + "&eventType=workspace.folder.parentAuthorizationInitialized&objectType=WorkspaceFolder&pageSize=1");
        void AssertSafe(JsonElement item)
        {
            foreach (var forbidden in new[] { child.Id, child.LinkId, parent.Id, parent.LinkId, "Configuration", "FieldsJson", "TraceId", "HistoryParent298", "PEID", "CEID" })
                Assert.DoesNotContain(forbidden, item.GetRawText(), StringComparison.Ordinal);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "父直接配置锁取得前已提交的撤权必须重验；清空配置也不能跳过原完整父位置edit资格")]
    public async Task CreateFolder_RechecksParentEditUnderConfigurationLock_AfterCommittedRevocation()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var parent = await FindFolderAsync(fixture, "归属维护目录");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Recheck298", "父来源重验", "parent-recheck298", "父来源重验账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.create"));
        using var ordinary = actor.Client;
        var rule = new DataScopeRuleDto("ParentEdit298", "ParentEdit298", null, "父配置清空后回到规则拒绝", true, "workspace.folder",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(parent.Id)),
            [new("ParentEdit298All", "all", null, 0, new Dictionary<string, string> { ["edit"] = "deny" })], []);
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "authorization.dataScope.save"), HttpMethod.Put,
            "/api/authorization/data-scope/rules/ParentEdit298", rule))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        foreach (var clearConfiguration in new[] { false, true })
        {
            await SaveAsync(fixture, admin, position with { LinkIds = [] }, parent,
                [new("ParentRecheck298", "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow", ["authorize"] = "deny" })]);
            var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            string? candidateId = null;
            fixture.Fault.AfterRegister = async (change, token) =>
            {
                if (change.EventType != "workspace.folder.created") return;
                candidateId = change.TargetId;
                reached.TrySetResult();
                await proceed.Task.WaitAsync(token);
            };
            Task<HttpResponseMessage>? creating = null;
            try
            {
                creating = FeatureHttpTestRequests.SendAsync(ordinary, Point(fixture, "workspace.folder.create"), HttpMethod.Post,
                    "/api/workspace/content/folders", new { position, values = new { ENAME = "RevokedParent298" }, copyParentAuthorization = true });
                await reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
                // generic正式管理入口只取ACL锁，不取Workspace图锁；此PUT从测试调用方独立提交，不能用owner来源路由制造锁等待。
                DataScopeDirectSubjectDto[] revoked = clearConfiguration ? [] :
                    [new("ParentRecheck298", "user", actor.AccountId, 1, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "deny" })];
                using (var saved = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.authorize"), HttpMethod.Put,
                    "/api/authorization/data-scope/objects/workspace.folder/" + parent.Id, new DataScopeObjectDto("workspace.folder", parent.Id, revoked)))
                    Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
                using (var scope = fixture.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                    var entries = await db.Set<Dictionary<string, object?>>("DataScopeObjectSubject").AsNoTracking()
                        .Where(row => EF.Property<string>(row, "ObjectId") == parent.Id).ToArrayAsync();
                    if (clearConfiguration) Assert.Empty(entries);
                    else
                    {
                        var entryId = (string)Assert.Single(entries)["EID"]!;
                        var opinion = await db.Set<Dictionary<string, object?>>("DataScopeObjectAction").AsNoTracking()
                            .SingleAsync(row => EF.Property<string>(row, "EntryId") == entryId && EF.Property<string>(row, "ActionKey") == "edit");
                        Assert.Equal("deny", opinion["Opinion"]);
                    }
                }
                proceed.TrySetResult();
                using var response = await creating;
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                using var finalScope = fixture.Services.CreateScope();
                var finalDb = finalScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                Assert.NotNull(candidateId);
                foreach (var (model, field) in new[] { ("WorkspaceFolder", "EID"), ("WorkspaceFolderContent", "CEID"), ("DataScopeObjectSubject", "ObjectId"), ("BusinessChange", "TargetId") })
                    Assert.False(await finalDb.Set<Dictionary<string, object?>>(model).AsNoTracking()
                        .AnyAsync(row => EF.Property<string>(row, field) == candidateId));
            }
            finally
            {
                proceed.TrySetResult();
                fixture.Fault.AfterRegister = null;
                if (creating is not null && !creating.IsCompleted) { using var ignored = await creating; }
            }
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "父直接项后改、清空与cut换父均不动态同步子完整配置；原绝对期限独立生效")]
    public async Task InitializedConfiguration_RemainsIndependentAfterParentChangesClearAndMove_AndExpiresAtOriginalInstant()
    {
        var clock = new InitializationClock();
        var childExpiry = clock.Now.AddMinutes(10);
        var childExpiryText = childExpiry.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var departmentExpiryText = clock.Now.AddMinutes(20).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var changedExpiryText = clock.Now.AddHours(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddSingleton<TimeProvider>(clock), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var departmentId = await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var parent = await FindFolderAsync(fixture, "归属维护目录");
        var destination = await CreateAsync(fixture, admin, position, "WorkspaceFolder", "folders", "create", "IndependentDestination298");
        var destinationPosition = position with { LinkIds = [.. position.LinkIds, destination.LinkId] };
        var read = fixture.FeatureCatalog.First(point => point.ReadBehaviors.Contains("workspace.folder.read"));
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "IndependentParent298", "独立初始化", "independent-parent298", "独立初始化账号",
            Point(fixture, "workspace.spaces.access"), read);
        using var ordinary = actor.Client;
        DataScopeDirectSubjectDto[] original = [
            new("IndependentUser298", "user", actor.AccountId, 11, new Dictionary<string, string> { ["read"] = "allow", ["edit"] = "allow" }, ExpiresAt: childExpiryText),
            new("IndependentDepartment298", "department", departmentId, 7, new Dictionary<string, string> { ["history"] = "deny" }, new(true), departmentExpiryText)
        ];
        var parentOccurrence = position with { LinkIds = [] };
        await SaveAsync(fixture, admin, parentOccurrence, parent, original);
        await SaveAsync(fixture, admin, position, destination,
            [new("IndependentDestinationDeny298", "all", null, 20, new Dictionary<string, string> { ["read"] = "deny" })]);
        using (var created = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.create"), HttpMethod.Post,
            "/api/workspace/content/folders", new { position, values = new { ENAME = "IndependentChild298" }, copyParentAuthorization = true }))
            await ReadOkAsync(created);
        var child = await FindFolderAsync(fixture, "IndependentChild298");
        var configuration = await FeatureHttpTestRequests.ReadAsync(admin, Point(fixture, "workspace.folder.authorize"), PermissionsPath(position, child.LinkId));
        var initial = configuration.GetProperty("subjects").GetRawText();
        Assert.Equal(2, configuration.GetProperty("subjects").GetArrayLength());
        Assert.Equal(childExpiryText, Assert.Single(configuration.GetProperty("subjects").EnumerateArray(), subject => subject.GetProperty("type").GetString() == "user").GetProperty("expiresAt").GetString());
        // 回退规则只约束该子对象，使到期后的拒绝不能被默认read允许掩盖。
        var rule = new DataScopeRuleDto("IndependentChildRule298", "IndependentChildRule298", null, "子直接项到期回退拒绝", true, "workspace.folder",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(child.Id)),
            [new("IndependentChildFallback298", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        using (var savedRule = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "authorization.dataScope.save"), HttpMethod.Put,
            "/api/authorization/data-scope/rules/IndependentChildRule298", rule))
            Assert.Equal(HttpStatusCode.NoContent, savedRule.StatusCode);
        await AssertReadAsync(HttpStatusCode.OK);

        await SaveAsync(fixture, admin, parentOccurrence, parent,
            [original[0] with { Priority = 99, Actions = new Dictionary<string, string> { ["read"] = "deny" }, ExpiresAt = changedExpiryText }]);
        var changed = await FeatureHttpTestRequests.ReadAsync(admin, Point(fixture, "workspace.folder.authorize"), PermissionsPath(parentOccurrence, parent.LinkId));
        var changedSubject = Assert.Single(changed.GetProperty("subjects").EnumerateArray());
        Assert.Equal(99, changedSubject.GetProperty("priority").GetInt64());
        Assert.Equal("deny", changedSubject.GetProperty("actions").GetProperty("read").GetString());
        Assert.Equal(changedExpiryText, changedSubject.GetProperty("expiresAt").GetString());
        await AssertUnchangedAsync(position, child);
        await AssertReadAsync(HttpStatusCode.OK);
        await SaveAsync(fixture, admin, parentOccurrence, parent, []);
        var cleared = await FeatureHttpTestRequests.ReadAsync(admin, Point(fixture, "workspace.folder.authorize"), PermissionsPath(parentOccurrence, parent.LinkId));
        Assert.Empty(cleared.GetProperty("subjects").EnumerateArray());
        await AssertUnchangedAsync(position, child);
        await AssertReadAsync(HttpStatusCode.OK);

        // 复用正式cut输入；更换出现位置只换Link，不从新父的deny配置重新初始化对象。
        using (var moved = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.paste"), HttpMethod.Post,
            "/api/workspace/content/paste", new { mode = "cut", sourcePosition = position, linkIds = new[] { child.LinkId }, targetPosition = destinationPosition }))
            Assert.False((await ReadOkAsync(moved)).GetProperty("noChange").GetBoolean());
        var relocated = await FindFolderAsync(fixture, child.Name);
        Assert.Equal(child.Id, relocated.Id); Assert.NotEqual(child.LinkId, relocated.LinkId);
        await AssertUnchangedAsync(destinationPosition, relocated);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var link = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "LID") == relocated.LinkId);
            Assert.Equal(destination.Id, link["PEID"]); Assert.Equal(child.Id, link["CEID"]);
            Assert.Equal(1, await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
                .CountAsync(row => EF.Property<string>(row, "TargetId") == child.Id && EF.Property<string>(row, "EventType") == "workspace.folder.parentAuthorizationInitialized"));
        }
        clock.Now = childExpiry;
        await AssertReadAsync(HttpStatusCode.OK);
        clock.Now = clock.Now.AddTicks(1);
        await AssertReadAsync(HttpStatusCode.NotFound);
        await AssertUnchangedAsync(destinationPosition, relocated);

        async Task AssertUnchangedAsync(Position occurrence, Target current)
        {
            var actual = await FeatureHttpTestRequests.ReadAsync(admin, Point(fixture, "workspace.folder.authorize"), PermissionsPath(occurrence, current.LinkId));
            Assert.Equal(initial, actual.GetProperty("subjects").GetRawText());
        }
        async Task AssertReadAsync(HttpStatusCode expected)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, "/api/workspace/folders/" + child.Id);
            Assert.True(response.StatusCode == expected, $"Expected {expected}, HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    private sealed class InitializationClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task<string> SnapshotAsync(BusinessAuditHostFixture fixture)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var snapshot = new Dictionary<string, string[]>();
        foreach (var model in new[] { "WorkspaceFolder", "WorkspaceFolderContent", "DataScopeObjectSubject", "DataScopeObjectAction", "BusinessChange" })
        {
            var idField = model == "WorkspaceFolderContent" ? "LID" : "EID";
            var rows = await db.Set<Dictionary<string, object?>>(model).AsNoTracking().OrderBy(row => EF.Property<string>(row, idField)).ToArrayAsync();
            snapshot[model] = rows.Select(row => JsonSerializer.Serialize(row.OrderBy(pair => pair.Key, StringComparer.Ordinal))).ToArray();
        }
        return JsonSerializer.Serialize(snapshot);
    }

    private static async Task<Target> FindFolderAsync(BusinessAuditHostFixture fixture, string name)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var row = await db.Set<Dictionary<string, object?>>("WorkspaceFolder").AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "ENAME") == name);
        var id = (string)row["EID"]!;
        var link = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "CEID") == id);
        return new("WorkspaceFolder", id, (string)link["LID"]!, name);
    }

    private static async Task SaveAsync(BusinessAuditHostFixture fixture, HttpClient admin, Position position, Target target,
        DataScopeDirectSubjectDto[] subjects)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.authorize"), HttpMethod.Put, PermissionsPath(position, target.LinkId),
            new DataScopeObjectConfiguration("workspace.folder", target.Id, subjects));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static string PermissionsPath(Position position, string linkId) => "/api/workspace/content/" + linkId + "/permissions?queryScope="
        + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private sealed class AuditTicks { internal long Count; }

    // 每次获取可信stamp推进时间，使误开嵌套操作的实现不能靠固定时钟通过一致性断言。
    private sealed class AdvancingStamp(HttpContextEntityAuditStampProvider inner, AuditTicks ticks) : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent()
        {
            var stamp = inner.GetCurrent();
            return new(stamp.ActorId, stamp.OccurredAtUtc.AddMilliseconds(Interlocked.Increment(ref ticks.Count)));
        }
    }
}
