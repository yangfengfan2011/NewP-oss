using System.Data.Common;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceObjectMutationSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Document真实改码同步规范快照，重复码标准冲突与双侧回滚")]
    public async Task DocumentCodeEdit_CopiesCanonicalSnapshotAndDuplicateCodeKeepsBothSides()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await RootAsync(fixture, admin);
        var document = await CreateAsync(fixture, admin, root, "documents", "createDocument", "code-before");
        await CreateAsync(fixture, admin, root, "documents", "createDocument", "code-existing");
        using var notCanonical = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{document.Link}",
            new { position = root, values = new { ECODE = "  code-after  " } });
        Assert.Equal(HttpStatusCode.BadRequest, notCanonical.StatusCode);
        Assert.Equal("StandardCrud.ShortTextNotNormalized", JsonSerializer.Deserialize<JsonElement>(await notCanonical.Content.ReadAsStringAsync()).GetProperty("code").GetString());
        using var edited = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{document.Link}",
            new { position = root, values = new { ECODE = "code-after", ENAME = "名称已编辑", Description = "快照" } });
        Assert.True(edited.StatusCode == HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        using (var scope = fixture.Services.CreateScope())
        {
            var identity = await scope.ServiceProvider.GetRequiredService<ICP.Platform.Modules.Document.DocumentInitialIdentity>().ReadCurrentAsync(document.Id);
            Assert.Equal("code-after", identity.Master["ECODE"]); Assert.Equal("名称已编辑", identity.Master["ENAME"]);
            foreach (var field in new[] { "ECODE", "ENAME", "Description", "UpdatedBy", "UpdatedAt" }) Assert.Equal(identity.Master[field], identity.Iteration[field]);
        }
        using var duplicate = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{document.Link}",
            new { position = root, values = new { ECODE = "code-existing", ENAME = "不可提交" } });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("StandardCrud.PersistenceConflict", JsonSerializer.Deserialize<JsonElement>(await duplicate.Content.ReadAsStringAsync()).GetProperty("code").GetString());
        using var retainedScope = fixture.Services.CreateScope();
        var retained = await retainedScope.ServiceProvider.GetRequiredService<ICP.Platform.Modules.Document.DocumentInitialIdentity>().ReadCurrentAsync(document.Id);
        Assert.Equal("code-after", retained.Master["ECODE"]); Assert.Equal("名称已编辑", retained.Master["ENAME"]);
        Assert.Equal(retained.Master["ECODE"], retained.Iteration["ECODE"]); Assert.Equal(retained.Master["ENAME"], retained.Iteration["ENAME"]);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实图文档指针损坏及后项批删失败保持master和快照原子")]
    public async Task DocumentIdentity_CorruptionAndLaterMixedDeleteFailureRollBackAllOwnedRows()
    {
        var fault = new DeleteFault { FailSecondDocument = true };
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(fault)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await RootAsync(fixture, admin);
        var document = await CreateAsync(fixture, admin, root, "documents", "createDocument", "identity-doc");
        var otherDocument = await CreateAsync(fixture, admin, root, "documents", "createDocument", "identity-other");
        var part = await CreateAsync(fixture, admin, root, "parts", "createPart", "identity-part");
        var secondPart = await CreateAsync(fixture, admin, root, "parts", "createPart", "identity-second-part");
        string versionId, otherVersionId, iterationId;
        using (var initialScope = fixture.Services.CreateScope())
        {
            var identities = initialScope.ServiceProvider.GetRequiredService<ICP.Platform.Modules.Document.DocumentInitialIdentity>();
            var current = await identities.ReadCurrentAsync(document.Id);
            var other = await identities.ReadCurrentAsync(otherDocument.Id);
            versionId = (string)current.Version["EID"]!; otherVersionId = (string)other.Version["EID"]!;
            iterationId = (string)current.Iteration["EID"]!;
            var db = initialScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.Null(db.Model.FindEntityType("Part")!.FindProperty("LatestVersionId"));
            Assert.Null(db.Model.FindEntityType("WorkspaceFolder")!.FindProperty("LatestVersionId"));
            await db.Set<Dictionary<string, object?>>("Document").Where(row => EF.Property<string>(row, "EID") == document.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string>(row, "LatestVersionId"), otherVersionId));
        }
        using var forged = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{document.Link}",
            new { position = root, values = new { LatestVersionId = versionId } });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using var corrupt = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{document.Link}",
            new { position = root, values = new { ENAME = "must-rollback" } });
        Assert.Equal(HttpStatusCode.InternalServerError, corrupt.StatusCode);
        using var corruptDelete = await SendAsync(fixture, admin, "delete", HttpMethod.Delete, $"/api/workspace/content/{document.Link}", new { position = root });
        Assert.Equal(HttpStatusCode.InternalServerError, corruptDelete.StatusCode);
        using (var readScope = fixture.Services.CreateScope())
        {
            var db = readScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var master = await db.Set<Dictionary<string, object?>>("Document").SingleAsync(row => EF.Property<string>(row, "EID") == document.Id);
            var iteration = await db.Set<Dictionary<string, object?>>("DocumentIteration").SingleAsync(row => EF.Property<string>(row, "EID") == iterationId);
            Assert.Equal("identity-doc", master["ENAME"]); Assert.Equal(master["ENAME"], iteration["ENAME"]);
            await db.Set<Dictionary<string, object?>>("Document").Where(row => EF.Property<string>(row, "EID") == document.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string>(row, "LatestVersionId"), versionId));
        }
        fault.Enabled = true;
        using var failed = await SendAsync(fixture, admin, "deleteObjects", HttpMethod.Post, "/api/workspace/content/delete",
            new { position = root, linkIds = new[] { document.Link, otherDocument.Link, part.Link, secondPart.Link } });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        fault.Enabled = false;
        Assert.True(fault.DocumentDeleted, (await failed.Content.ReadAsStringAsync()).Split("HEADERS", StringSplitOptions.None)[0]);
        using (var retainedScope = fixture.Services.CreateScope())
        {
            var current = await retainedScope.ServiceProvider.GetRequiredService<ICP.Platform.Modules.Document.DocumentInitialIdentity>().ReadCurrentAsync(document.Id);
            Assert.Equal(versionId, current.Version["EID"]); Assert.Equal(iterationId, current.Iteration["EID"]);
            var db = retainedScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            foreach (var target in new[] { document, otherDocument, part, secondPart })
            {
                Assert.True(await db.Set<Dictionary<string, object?>>(target.Model).AnyAsync(row => EF.Property<string>(row, "EID") == target.Id));
                Assert.True(await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AnyAsync(row => EF.Property<string>(row, "LID") == target.Link));
            }
        }
        using var single = await SendAsync(fixture, admin, "delete", HttpMethod.Delete, $"/api/workspace/content/{document.Link}", new { position = root });
        Assert.True(single.StatusCode == HttpStatusCode.OK, (await single.Content.ReadAsStringAsync()).Split("HEADERS", StringSplitOptions.None)[0]);
        using var finalScope = fixture.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.False(await finalDb.Set<Dictionary<string, object?>>("DocumentVersion").AnyAsync(row => EF.Property<string>(row, "EID") == versionId));
        Assert.False(await finalDb.Set<Dictionary<string, object?>>("DocumentIteration").AnyAsync(row => EF.Property<string>(row, "EID") == iterationId));
        Assert.NotNull(await finalScope.ServiceProvider.GetRequiredService<ICP.Platform.Modules.Document.DocumentInitialIdentity>().ReadCurrentAsync(otherDocument.Id));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实混合内容编辑、共享引用清理和批量删除")]
    public async Task EditAndDelete_UsesOwningRulesAndDeletesAllReferences()
    {
        var reads = new ReadOnlyRequestGuard();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(reads)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await RootAsync(fixture, admin);
        var folder = await CreateAsync(fixture, admin, root, "folders", "create", "folder");
        var other = await CreateAsync(fixture, admin, root, "folders", "create", "other");
        var part = await CreateAsync(fixture, admin, root, "parts", "createPart", "part");
        var document = await CreateAsync(fixture, admin, root, "documents", "createDocument", "document");
        var anotherPart = await CreateAsync(fixture, admin, root, "parts", "createPart", "other-part");
        // 校准同一拦截器：即使恒等 UPDATE 也须在执行前拒绝，不能靠前后值相等漏掉隐式写入。
        using (var calibrationScope = fixture.Services.CreateScope())
        {
            var calibrationDb = calibrationScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            reads.Enabled = true;
            try
            {
                var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() => calibrationDb.Set<Dictionary<string, object?>>("Part")
                    .Where(row => EF.Property<string>(row, "EID") == part.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string>(row, "ENAME"),
                        row => EF.Property<string>(row, "ENAME"))));
                Assert.Equal("WORKSPACE_EDIT_VALUES_WRITE_FORBIDDEN", blocked.Message);
            }
            finally { reads.Enabled = false; }
        }
        reads.Reset();
        foreach (var target in new[] { folder, part, document })
        {
            using var beforeScope = fixture.Services.CreateScope();
            var before = await beforeScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>(target.Model).AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "EID") == target.Id);
            var beforeRead = await ReadPersistentStateAsync(fixture);
            JsonElement fields;
            reads.Enabled = true;
            try
            {
                using var editRead = await SendAsync(fixture, admin, "edit", HttpMethod.Post, $"/api/workspace/content/{target.Link}/edit-values", new { position = root });
                Assert.True(editRead.StatusCode == HttpStatusCode.OK, await editRead.Content.ReadAsStringAsync());
                fields = JsonSerializer.Deserialize<JsonElement>(await editRead.Content.ReadAsStringAsync()).GetProperty("fields");
            }
            finally { reads.Enabled = false; }
            Assert.Equal(0, reads.RejectedWrites);
            Assert.Equal(beforeRead, await ReadPersistentStateAsync(fixture));
            Assert.False(fields.TryGetProperty("OriginSpaceId", out _));
            var values = new Dictionary<string, object?> { ["ENAME"] = "edited-" + target.Id, ["Description"] = "changed" };
            if (target.Model != "WorkspaceFolder") values["ECODE"] = fields.GetProperty("ECODE").GetString();
            using var edited = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{target.Link}", new { position = root, values });
            Assert.True(edited.StatusCode == HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
            using var afterScope = fixture.Services.CreateScope();
            var after = await afterScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>(target.Model).AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "EID") == target.Id);
            Assert.Equal(values["ENAME"], after["ENAME"]);
            Assert.Equal("changed", after["Description"]);
            Assert.Equal(before["CreatedAt"], after["CreatedAt"]);
            Assert.Equal(before["CreatedBy"], after["CreatedBy"]);
            Assert.True((DateTime)after["UpdatedAt"]! >= (DateTime)before["UpdatedAt"]!);
            if (target.Model == "Document")
            {
                var identity = await afterScope.ServiceProvider.GetRequiredService<ICP.Platform.Modules.Document.DocumentInitialIdentity>().ReadCurrentAsync(target.Id);
                foreach (var field in new[] { "ECODE", "ENAME", "Description", "UpdatedBy", "UpdatedAt" })
                    Assert.Equal(after[field], identity.Iteration[field]);
            }
        }
        using var duplicate = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{part.Link}",
            new { position = root, values = new { ECODE = "other-part", ENAME = "duplicate" } });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var sibling = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{folder.Link}", new { position = root, values = new { ENAME = "other" } });
        Assert.Equal(HttpStatusCode.Conflict, sibling.StatusCode);
        using var forged = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{folder.Link}", new { position = root, values = new { OriginSpaceId = "foreign" } });
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using var longDescription = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{document.Link}", new { position = root, values = new { Description = new string('x', 501) } });
        Assert.Equal(HttpStatusCode.BadRequest, longDescription.StatusCode);
        var otherPosition = root with { LinkIds = [.. root.LinkIds, other.Link] };
        foreach (var target in new[] { folder, part, document })
        {
            using var added = await SendAsync(fixture, admin, "add", HttpMethod.Post, "/api/workspace/content/references",
                new { position = otherPosition, target = new { modelKey = target.Model, id = target.Id } });
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        }
        using var wrongPosition = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{part.Link}", new { position = otherPosition, values = new { ENAME = "wrong" } });
        Assert.Equal(HttpStatusCode.NotFound, wrongPosition.StatusCode);
        using var deleted = await SendAsync(fixture, admin, "deleteObjects", HttpMethod.Post, "/api/workspace/content/delete",
            new { position = root, linkIds = new[] { folder.Link, part.Link, document.Link } });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        foreach (var target in new[] { folder, part, document })
        {
            Assert.False(await db.Set<Dictionary<string, object?>>(target.Model).AnyAsync(row => EF.Property<string>(row, "EID") == target.Id));
            Assert.False(await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AnyAsync(row => EF.Property<string>(row, "CEID") == target.Id));
        }
        Assert.True(await db.Set<Dictionary<string, object?>>("Part").AnyAsync(row => EF.Property<string>(row, "EID") == anotherPart.Id));
        Assert.False(await db.Set<Dictionary<string, object?>>("DocumentVersion").AnyAsync());
        Assert.False(await db.Set<Dictionary<string, object?>>("DocumentIteration").AnyAsync());
        using var single = await SendAsync(fixture, admin, "delete", HttpMethod.Delete, $"/api/workspace/content/{anotherPart.Link}", new { position = root });
        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
    }

    private static async Task<string> ReadPersistentStateAsync(BusinessAuditHostFixture fixture)
    {
        // 固定六张小型夹具表比较完整持久事实，包含对象、空间、关系及审计；不只比较响应字段。
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var state = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var model in new[] { "WorkspaceFolder", "Part", "Document", "WorkspaceSpace", "WorkspaceFolderContent", "BusinessChange" })
        {
            var rows = await db.Set<Dictionary<string, object?>>(model).AsNoTracking().ToArrayAsync();
            state[model] = rows.Select(row => JsonSerializer.Serialize(row.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)))
                .Order(StringComparer.Ordinal).ToArray();
        }
        return JsonSerializer.Serialize(state);
    }

    private sealed class ReadOnlyRequestGuard : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public int RejectedWrites { get; private set; }
        public void Reset() => RejectedWrites = 0;

        // EF reader/nonquery/scalar 均受保护；@局部表变量及#临时表范围计算不属于持久表 DML。
        private void Inspect(DbCommand command)
        {
            if (!Enabled || !Regex.IsMatch(command.CommandText,
                @"\b(?:INSERT\s+(?:INTO\s+)?|UPDATE\s+|DELETE\s+(?:FROM\s+)?|MERGE\s+(?:INTO\s+)?|TRUNCATE\s+TABLE\s+)(?:\[|[A-Za-z_])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return;
            RejectedWrites++;
            throw new InvalidOperationException("WORKSPACE_EDIT_VALUES_WRITE_FORBIDDEN");
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result) { Inspect(command); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Inspect(command); return ValueTask.FromResult(result); }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result) { Inspect(command); return result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Inspect(command); return ValueTask.FromResult(result); }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result) { Inspect(command); return result; }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result, CancellationToken cancellationToken = default)
        { Inspect(command); return ValueTask.FromResult(result); }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "批量拒绝、写失败及权限边界不留下部分删除")]
    public async Task Delete_RejectsNonEmptyAndRollsBackFailureAndDeniedTargets()
    {
        var decisions = new MutationDecisions();
        var fault = new DeleteFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.AddSingleton<IDataScopeObjectDecisionProvider>(decisions);
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(fault));
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await RootAsync(fixture, admin);
        var folder = await CreateAsync(fixture, admin, root, "folders", "create", "non-empty");
        var part = await CreateAsync(fixture, admin, root, "parts", "createPart", "keep");
        var otherPart = await CreateAsync(fixture, admin, root, "parts", "createPart", "also-keep");
        using var shared = await SendAsync(fixture, admin, "add", HttpMethod.Post, "/api/workspace/content/references",
            new { position = root with { LinkIds = [.. root.LinkIds, folder.Link] }, target = new { modelKey = part.Model, id = part.Id } });
        Assert.Equal(HttpStatusCode.OK, shared.StatusCode);
        var body = new { position = root, linkIds = new[] { part.Link, folder.Link } };
        using var nonEmpty = await SendAsync(fixture, admin, "deleteObjects", HttpMethod.Post, "/api/workspace/content/delete", body);
        Assert.Equal(HttpStatusCode.Conflict, nonEmpty.StatusCode);
        Assert.Contains("FolderNotEmpty", await nonEmpty.Content.ReadAsStringAsync());
        decisions.DeniedId = part.Id;
        using var denied = await SendAsync(fixture, admin, "deleteObjects", HttpMethod.Post, "/api/workspace/content/delete", new { position = root, linkIds = new[] { part.Link } });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var editDenied = await SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{part.Link}", new { position = root, values = new { ENAME = "denied" } });
        Assert.Equal(HttpStatusCode.Forbidden, editDenied.StatusCode);
        decisions.DeniedId = null;
        using var wrongFeature = await SendAsync(fixture, admin, "create", HttpMethod.Post, "/api/workspace/content/delete", body);
        Assert.Equal(HttpStatusCode.Forbidden, wrongFeature.StatusCode);
        foreach (var ids in new[] { Array.Empty<string>(), new[] { part.Link, part.Link }, Enumerable.Range(0, 101).Select(index => "L" + index).ToArray() })
        {
            using var invalid = await SendAsync(fixture, admin, "deleteObjects", HttpMethod.Post, "/api/workspace/content/delete", new { position = root, linkIds = ids });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        fault.Enabled = true;
        using var failed = await SendAsync(fixture, admin, "deleteObjects", HttpMethod.Post, "/api/workspace/content/delete", new { position = root, linkIds = new[] { part.Link, otherPart.Link } });
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        fault.Enabled = false;
        Assert.Equal(2, fault.EntityDeletes);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.True(await db.Set<Dictionary<string, object?>>(otherPart.Model).AnyAsync(row => EF.Property<string>(row, "EID") == otherPart.Id));
        Assert.True(await db.Set<Dictionary<string, object?>>(part.Model).AnyAsync(row => EF.Property<string>(row, "EID") == part.Id));
        Assert.True(await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AnyAsync(row => EF.Property<string>(row, "LID") == part.Link));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "编辑与删除等待真实图锁后重验位置，编辑装载不申请图写锁")]
    public async Task Mutation_WaitingForGraphLockRejectsRemovedPositionWithoutChangingTarget()
    {
        foreach (var editing in new[] { true, false })
        {
            var gate = new GraphLockPause();
            await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
                services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(gate)), includeWorkspace: true);
            using var admin = await fixture.LoginAdministratorAsync();
            var root = await RootAsync(fixture, admin);
            var parent = await CreateAsync(fixture, admin, root, "folders", "create", "parent");
            var position = root with { LinkIds = [.. root.LinkIds, parent.Link] };
            var target = await CreateAsync(fixture, admin, position, "parts", "createPart", "target");
            using var beforeScope = fixture.Services.CreateScope();
            var before = await beforeScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>()
                .Set<Dictionary<string, object?>>(target.Model).AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == target.Id);

            // 移除请求已经在 SQL Server 中持有图锁；等竞争写入也开始申请同一锁后才放行移除。
            gate.Enabled = true;
            var removal = SendAsync(fixture, admin, "remove", HttpMethod.Delete, $"/api/workspace/content/references/{parent.Link}", new { position = root });
            Task<HttpResponseMessage>? mutation = null;
            try
            {
                await gate.Held.Task.WaitAsync(TimeSpan.FromSeconds(10));
                using var editValues = await SendAsync(fixture, admin, "edit", HttpMethod.Post, $"/api/workspace/content/{target.Link}/edit-values",
                    new { position }).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(HttpStatusCode.OK, editValues.StatusCode);
                mutation = editing
                    ? SendAsync(fixture, admin, "edit", HttpMethod.Put, $"/api/workspace/content/{target.Link}", new { position, values = new { ENAME = "must-not-change" } })
                    : SendAsync(fixture, admin, "delete", HttpMethod.Delete, $"/api/workspace/content/{target.Link}", new { position });
                await gate.ContenderRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(mutation.IsCompleted);
            }
            finally
            {
                gate.Release.TrySetResult();
                using var removed = await removal;
                Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
                if (mutation is not null)
                {
                    using var rejected = await mutation;
                    Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
                }
            }

            using var afterScope = fixture.Services.CreateScope();
            var db = afterScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var after = await db.Set<Dictionary<string, object?>>(target.Model).AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "EID") == target.Id);
            Assert.Equal(before["ENAME"], after["ENAME"]);
            Assert.Equal(before["UpdatedAt"], after["UpdatedAt"]);
            Assert.Equal(before["UpdatedBy"], after["UpdatedBy"]);
            var links = db.Set<Dictionary<string, object?>>("WorkspaceFolderContent");
            Assert.False(await links.AnyAsync(row => EF.Property<string>(row, "LID") == parent.Link));
            Assert.True(await links.AnyAsync(row => EF.Property<string>(row, "LID") == target.Link));
        }
    }

    private sealed class GraphLockPause : DbCommandInterceptor
    {
        private DbCommand? _holder;
        public bool Enabled { get; set; }
        public TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContenderRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("sp_getapplock", StringComparison.Ordinal)
                && command.CommandText.Contains("Workspace.FolderGraph", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref _holder, command, null) is not null)
                ContenderRequested.TrySetResult();
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(_holder, command))
            {
                Held.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed record Position(string SpaceId, string RootFolderId, string[] LinkIds);
    private sealed record Target(string Model, string Id, string Link);
    // 原 HTTP 场景消费列表位置；树菜单的同一行为拥有独立功能位置。
    private static FeaturePoint Point(BusinessAuditHostFixture fixture, string action) => Assert.Single(fixture.FeatureCatalog,
        point => point.BehaviorKey == "workspace.folder." + (action == "edit" ? "editObject" : action == "delete" ? "deleteObject" : action)
            && (action is not ("edit" or "remove") || point.ModuleId == "Workspace" && point.StaticScope == "folder"
                && point.ActionScope == "list.rowActions" && point.Key == action));
    private static Task<HttpResponseMessage> SendAsync(BusinessAuditHostFixture fixture, HttpClient client, string action, HttpMethod method, string path, object body) =>
        FeatureHttpTestRequests.SendAsync(client, Point(fixture, action), method, path, body);
    private static async Task<Position> RootAsync(BusinessAuditHostFixture fixture, HttpClient admin)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(admin, Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "workspace.spaces.access"), HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        var rootPosition = new Position(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
        var container = await CreateAsync(fixture, admin, rootPosition, "folders", "create", "对象行为消费目录");
        return rootPosition with { LinkIds = [container.Link] };
    }
    private static async Task<Target> CreateAsync(BusinessAuditHostFixture fixture, HttpClient admin, Position position, string kind, string action, string name)
    {
        var values = new Dictionary<string, object?> { ["ENAME"] = name };
        if (kind != "folders") values["ECODE"] = name;
        using var response = await SendAsync(fixture, admin, action, HttpMethod.Post, "/api/workspace/content/" + kind, new { position, values });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var model = kind == "folders" ? "WorkspaceFolder" : kind == "parts" ? "Part" : "Document";
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var entity = await db.Set<Dictionary<string, object?>>(model).SingleAsync(row => EF.Property<string>(row, "ENAME") == name);
        var id = (string)entity["EID"]!;
        var link = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").SingleAsync(row => EF.Property<string>(row, "CEID") == id);
        return new(model, id, (string)link["LID"]!);
    }
    private sealed class MutationDecisions : IDataScopeObjectDecisionProvider
    {
        public string? DeniedId { get; set; }
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken cancellationToken = default) => Task.FromResult(objectId != DeniedId || actionKey == "read");
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
    private sealed class DeleteFault : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public bool FailSecondDocument { get; init; }
        public bool DocumentDeleted { get; private set; }
        private int DocumentDeletes { get; set; }
        public int EntityDeletes { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            RecordDelete(command);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            RecordDelete(command);
            return ValueTask.FromResult(result);
        }
        private void RecordDelete(DbCommand command)
        {
            if (!Enabled || !command.CommandText.Contains("DELETE", StringComparison.Ordinal)) return;
            if (command.CommandText.Contains("[Document]", StringComparison.Ordinal))
            {
                DocumentDeleted = true;
                if (++DocumentDeletes == 2 && FailSecondDocument) throw new InvalidOperationException("WORKSPACE_DELETE_FAILURE");
            }
            if (!FailSecondDocument && command.CommandText.Contains("[Part]", StringComparison.Ordinal) && ++EntityDeletes == 2)
                throw new InvalidOperationException("WORKSPACE_DELETE_FAILURE");
        }
    }
}
