using ICP.Platform.Authorization.Contracts;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.Modules.Document;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Modules.Files.Services;
using ICP.Platform.Modules.Files.Storage;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;
using static ICP.Platform.HostApi.Tests.DocumentFilesSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

/// <summary>真实 Workspace/Document HTTP、SQL Server 事务锁和 Local 字节共同证明 Files 的故障收敛。</summary>
public sealed class DocumentFileLifecycleFaultSqlServerHttpApiTests
{
    private const string SqlGate = "NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION";
    private static readonly TimeSpan BarrierTimeout = TimeSpan.FromSeconds(15);
    private static readonly byte[] Bytes = [0x25, 0x50, 0x44, 0x46, 1, 2, 3, 4];

    [EnvironmentGatedFact(SqlGate, "真实 SQL intent commit 前失败不写内容")]
    public Task StageIntentCommitBeforeFailure() => StageCommitFailureAsync(1, false);
    [EnvironmentGatedFact(SqlGate, "真实 SQL intent commit 后未知结果重读且不写内容")]
    public Task StageIntentCommitAfterUnknownResult() => StageCommitFailureAsync(1, true);
    [EnvironmentGatedFact(SqlGate, "真实 SQL ready commit 前失败按持久意图回收")]
    public Task StageReadyCommitBeforeFailure() => StageCommitFailureAsync(2, false);
    [EnvironmentGatedFact(SqlGate, "真实 SQL ready commit 后未知结果保合法 staged")]
    public Task StageReadyCommitAfterUnknownResult() => StageCommitFailureAsync(2, true);

    [EnvironmentGatedFact(SqlGate, "真实 SQL open 前取消与 writer gate 协作")]
    public Task CancelBeforeOpeningContent() => CancelOrExpireDuringIoAsync("open", false);
    [EnvironmentGatedFact(SqlGate, "真实 SQL rename 前取消与 writer gate 协作")]
    public Task CancelBeforeRenamingContent() => CancelOrExpireDuringIoAsync("rename", false);
    [EnvironmentGatedFact(SqlGate, "真实 SQL open 前到期不能迟到 ready")]
    public Task ExpireBeforeOpeningContent() => CancelOrExpireDuringIoAsync("open", true);
    [EnvironmentGatedFact(SqlGate, "真实 SQL rename 前到期不能迟到 ready")]
    public Task ExpireBeforeRenamingContent() => CancelOrExpireDuringIoAsync("rename", true);

    private static async Task StageCommitFailureAsync(int phase, bool afterCommit)
    {
        var control = new FaultControl();
        await using var fixture = await CreateFixtureAsync(control);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        control.Transactions.ArmStageFailure(phase, afterCommit);
        using var request = UploadRequest(fixture, position, Values($"STAGE-{phase}-{afterCommit}"), new ByteArrayContent(Bytes), "main.pdf");
        var result = await HttpOutcomeAsync(() => admin.SendAsync(request));
        Assert.False(result.Succeeded);
        Assert.Equal(1, control.Transactions.InjectedFailures);
        Assert.False(control.Transactions.HasActiveFilesTransaction);
        var durable = await ObserveAsync(fixture);
        if (phase == 1 && !afterCommit)
        {
            Assert.Empty(durable.Files);
            Assert.Empty(durable.Leases);
            Assert.Equal(0, control.Io.RealWrites);
            Assert.Empty(Directory.EnumerateFiles(fixture.StorageRoot));
            return;
        }
        var file = Assert.Single(durable.Files);
        var lease = Assert.Single(durable.Leases);
        var staged = phase == 2 && afterCommit;
        Assert.Equal(staged ? "ready" : "cleanupPending", file["State"]);
        Assert.Equal(staged ? "staged" : "cleanupPending", lease["State"]);
        Assert.Empty(durable.Usages);
        Assert.Equal(phase == 1 ? 0 : 1, control.Io.RealWrites);
        if (phase == 1) Assert.Empty(Directory.EnumerateFiles(fixture.StorageRoot));
        if (staged)
        {
            Assert.Equal((long)Bytes.Length, file["Length"]);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Bytes)), file["Sha256"]);
            Assert.Equal(Bytes, await File.ReadAllBytesAsync(ContentPath(fixture, file)));
            Assert.Equal(0, await CleanupAsync(fixture));
            await ExpireAsync(fixture, (string)lease["EID"]!);
        }
        Assert.Equal(1, await CleanupAsync(fixture));
        Assert.Equal(0, await CleanupAsync(fixture));
        Assert.Empty(Directory.EnumerateFiles(fixture.StorageRoot));
        var final = await ObserveAsync(fixture);
        Assert.Equal("deleted", Assert.Single(final.Files)["State"]);
        Assert.Equal("cleaned", Assert.Single(final.Leases)["State"]);
    }

    private static async Task CancelOrExpireDuringIoAsync(string point, bool expire)
    {
        var control = new FaultControl();
        await using var fixture = await CreateFixtureAsync(control);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        control.Io.BarrierPoint = point;
        using var request = UploadRequest(fixture, position, Values($"BARRIER-{point}-{expire}"), new ByteArrayContent(Bytes), "main.pdf");
        var upload = HttpOutcomeAsync(() => admin.SendAsync(request));
        Task<HttpOutcome>? cancel = null;
        Task<CleanupOutcome>? cleanup = null;
        Exception? primary = null;
        try
        {
            await control.Io.Reached.Task.WaitAsync(BarrierTimeout);
            Assert.False(control.Transactions.HasActiveFilesTransaction);
            var pending = await ObserveAsync(fixture);
            var file = Assert.Single(pending.Files);
            var lease = Assert.Single(pending.Leases);
            Assert.Equal("receiving", file["State"]);
            Assert.Equal("receiving", lease["State"]);
            if (point == "open") Assert.Empty(Directory.EnumerateFiles(fixture.StorageRoot));
            else Assert.True(File.Exists(ContentPath(fixture, file) + ".part"));
            var uploadId = (string)lease["EID"]!;
            if (expire) await ExpireAsync(fixture, uploadId);
            else
                cancel = HttpOutcomeAsync(() => FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.createDocument"), HttpMethod.Delete,
                    "/api/workspace/content/document-uploads/" + uploadId));
            if (!expire) await control.Io.AbortObserved.Task.WaitAsync(BarrierTimeout);
            cleanup = CleanupOutcomeAsync(fixture);
            await control.Io.AbortObserved.Task.WaitAsync(BarrierTimeout);
            Assert.False(upload.IsCompleted);
            Assert.False(cleanup.IsCompleted);
            if (cancel is not null) Assert.False(cancel.IsCompleted);
            // writer 故意保有 gate 到显式释放；pending 已提交，等待期间没有 Files 数据库事务。
            Assert.False(control.Transactions.HasActiveFilesTransaction);
            var committedPending = await ObserveAsync(fixture);
            Assert.Equal("cleanupPending", Assert.Single(committedPending.Files)["State"]);
        }
        catch (Exception exception) { primary = exception; }
        finally { control.Io.Resume.TrySetResult(); }
        await JoinAfterReleaseAsync(primary, upload, cleanup, cancel);
        var uploadResult = await upload;
        var cleanupResult = cleanup is null ? null : await cleanup;
        var cancelResult = cancel is null ? null : await cancel;
        Assert.False(uploadResult.Succeeded);
        if (cancelResult is not null) { Assert.Null(cancelResult.Exception); Assert.Equal(HttpStatusCode.NoContent, cancelResult.Status); }
        Assert.NotNull(cleanupResult);
        Assert.Null(cleanupResult.Exception);
        Assert.Equal(1, cleanupResult.Cleaned);
        Assert.Equal(0, await CleanupAsync(fixture));
        Assert.Empty(Directory.EnumerateFiles(fixture.StorageRoot));
        var final = await ObserveAsync(fixture);
        Assert.Equal("deleted", Assert.Single(final.Files)["State"]);
        Assert.Equal("cleaned", Assert.Single(final.Leases)["State"]);
        Assert.Empty(final.Usages);
    }

    [EnvironmentGatedFact(SqlGate, "真实 Confirm 持 Files 事务锁阻塞取消与 cleanup，confirmed 后取消冲突且字节保留")]
    public async Task ConfirmWins_RealTransactionLockBlocksCleanupAndActiveUsageProtectsContent()
    {
        var control = new FaultControl();
        await using var fixture = await CreateFixtureAsync(control);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var staged = await StageAsync(fixture, admin, position, Values("CONFIRM-WINS"), Bytes);
        var initial = Assert.Single((await ObserveAsync(fixture)).Files);
        var fileId = (string)initial["EID"]!;
        control.Transactions.BlockConfirmCommit = true;
        var create = HttpOutcomeAsync(() => CreateDocumentAsync(fixture, admin, position, "CONFIRM-WINS", staged.GetProperty("uploadId").GetString()!));
        Task<CleanupOutcome>? cleanup = null;
        Task<HttpOutcome>? cancel = null;
        Exception? primary = null;
        try
        {
            await control.Transactions.ConfirmCommitReached.Task.WaitAsync(BarrierTimeout);
            await AssertFileLockHeldAsync(fixture, fileId);
            control.Commands.WatchCancelRead = true;
            cancel = HttpOutcomeAsync(() => FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.createDocument"), HttpMethod.Delete,
                "/api/workspace/content/document-uploads/" + staged.GetProperty("uploadId").GetString()!));
            await control.Commands.CancelReadDispatched.Task.WaitAsync(BarrierTimeout);
            Assert.False(cancel.IsCompleted);
            control.Clock.Advance(TimeSpan.FromHours(24));
            control.Commands.WatchCleanup = true;
            cleanup = CleanupOutcomeAsync(fixture);
            await control.Commands.CleanupDispatched.Task.WaitAsync(BarrierTimeout);
            Assert.False(create.IsCompleted);
            Assert.False(cleanup.IsCompleted);
        }
        catch (Exception exception) { primary = exception; }
        finally { control.Transactions.ResumeConfirm.TrySetResult(); }
        await JoinAfterReleaseAsync(primary, create, cleanup, cancel);
        var created = await create;
        var cleaned = cleanup is null ? null : await cleanup;
        var cancelled = cancel is null ? null : await cancel;
        Assert.Null(created.Exception);
        Assert.Equal(HttpStatusCode.OK, created.Status);
        Assert.NotNull(cleaned);
        Assert.Null(cleaned.Exception);
        Assert.Equal(0, cleaned.Cleaned);
        Assert.NotNull(cancelled);
        Assert.Null(cancelled.Exception);
        Assert.Equal(HttpStatusCode.Conflict, cancelled.Status);
        var final = await ObserveAsync(fixture);
        Assert.Equal("active", Assert.Single(final.Usages)["State"]);
        Assert.Equal("confirmed", Assert.Single(final.Leases)["State"]);
        Assert.Equal("ready", Assert.Single(final.Files)["State"]);
        Assert.Equal(Bytes, await File.ReadAllBytesAsync(ContentPath(fixture, Assert.Single(final.Files))));
    }

    [EnvironmentGatedFact(SqlGate, "真实 cleanup pending 先持锁，随后 Document 保存失败且没有部分提交")]
    public async Task CleanupWins_PendingStateAndFileLockRejectWholeDocumentCreate()
    {
        var control = new FaultControl();
        await using var fixture = await CreateFixtureAsync(control);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var staged = await StageAsync(fixture, admin, position, Values("CLEANUP-WINS"), Bytes);
        var fileId = (string)Assert.Single((await ObserveAsync(fixture)).Files)["EID"]!;
        await ExpireAsync(fixture, staged.GetProperty("uploadId").GetString()!);
        using var heldScope = fixture.Services.CreateScope();
        var tx = heldScope.ServiceProvider.GetRequiredService<IPlatformTransactionExecutor>();
        var state = heldScope.ServiceProvider.GetRequiredService<FileStateStore>();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = tx.ExecuteAsync(async token =>
        {
            Assert.True(await state.TryMarkCleanupPendingAsync(fileId, false, token));
            reached.TrySetResult();
            await resume.Task.WaitAsync(BarrierTimeout, token);
            return true;
        }).AsTask();
        Task<HttpOutcome>? create = null;
        Exception? primary = null;
        try
        {
            await reached.Task.WaitAsync(BarrierTimeout);
            await AssertFileLockHeldAsync(fixture, fileId);
            control.Commands.WatchStagedRead = true;
            create = HttpOutcomeAsync(() => CreateDocumentAsync(fixture, admin, position, "CLEANUP-WINS", staged.GetProperty("uploadId").GetString()!));
            await control.Commands.StagedReadDispatched.Task.WaitAsync(BarrierTimeout);
            Assert.False(create.IsCompleted);
        }
        catch (Exception exception) { primary = exception; }
        finally { resume.TrySetResult(); }
        await JoinAfterReleaseAsync(primary, pending, create);
        var created = create is null ? null : await create;
        Assert.NotNull(created);
        Assert.False(created.Succeeded);
        using (var observation = fixture.Services.CreateScope())
        {
            var db = observation.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            foreach (var model in new[] { "Document", "DocumentVersion", "DocumentIteration", "DocumentFileLink", "WorkspaceFolderContent", "FileUsage" })
                Assert.Equal(model == "WorkspaceFolderContent" ? 1 : 0, await db.Set<Dictionary<string, object?>>(model).AsNoTracking().CountAsync());
        }
        Assert.Equal(1, await CleanupAsync(fixture));
        Assert.Empty(Directory.EnumerateFiles(fixture.StorageRoot));
    }

    [EnvironmentGatedFact(SqlGate, "真实 SQL active/合法 staged 保护、最后 release 无 TTL、Local 删除失败 pending 幂等重试")]
    public async Task ActiveAndLegalStagedProtectContent_LastReleaseAndIoFailureRetryDoNotWaitConfirmedTtl()
    {
        var control = new FaultControl();
        await using var fixture = await CreateFixtureAsync(control);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var staged = await StageAsync(fixture, admin, position, Values("RELEASE-RETRY"), Bytes);
        Assert.Equal(0, await CleanupAsync(fixture));
        var initial = Assert.Single((await ObserveAsync(fixture)).Files);
        Assert.Equal(Bytes, await File.ReadAllBytesAsync(ContentPath(fixture, initial)));
        using (var created = await CreateDocumentAsync(fixture, admin, position, "RELEASE-RETRY", staged.GetProperty("uploadId").GetString()!))
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(0, await CleanupAsync(fixture));
        string contentLinkId;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            contentLinkId = (string)(await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().SingleAsync(row => EF.Property<string>(row, "ChildModelKey") == "Document"))["LID"]!;
        }
        using (var deleted = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.deleteObject"), HttpMethod.Delete,
                   "/api/workspace/content/" + contentLinkId, new { position })) Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var released = await ObserveAsync(fixture);
        Assert.Equal("released", Assert.Single(released.Usages)["State"]);
        Assert.Equal("cleanupPending", Assert.Single(released.Files)["State"]);
        Assert.Equal("confirmed", Assert.Single(released.Leases)["State"]);
        Assert.True((DateTime)Assert.Single(released.Leases)["ExpiresAt"]! > control.Clock.GetUtcNow().UtcDateTime);
        control.Io.DeleteFailures = 1;
        Assert.Equal(0, await CleanupAsync(fixture));
        Assert.True(File.Exists(ContentPath(fixture, initial)));
        Assert.Equal("cleanupPending", Assert.Single((await ObserveAsync(fixture)).Files)["State"]);
        Assert.Equal(1, await CleanupAsync(fixture));
        Assert.Equal(0, await CleanupAsync(fixture));
        Assert.False(File.Exists(ContentPath(fixture, initial)));
        Assert.Equal("deleted", Assert.Single((await ObserveAsync(fixture)).Files)["State"]);
    }

    [EnvironmentGatedFact(SqlGate, "真实 Document policy 在新 scope 无 Added Link 时重复 Confirm 原使用，不能换目标或复活 released")]
    public async Task ConfirmCommittedDocumentInFreshScopes_ReturnsOriginalUsageWithoutAddedLink_AndRejectsRetargetOrReleased()
    {
        var control = new FaultControl();
        await using var fixture = await CreateFixtureAsync(control);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await ContentAsync(fixture, admin);
        var staged = await StageAsync(fixture, admin, position, Values("CONFIRM-REPEAT"), Bytes);
        var uploadId = staged.GetProperty("uploadId").GetString()!;
        using (var created = await CreateDocumentAsync(fixture, admin, position, "CONFIRM-REPEAT", uploadId))
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var committed = await ObserveAsync(fixture);
        var lease = Assert.Single(committed.Leases);
        var originalUsage = Assert.Single(committed.Usages);
        var actorId = (string)lease["ActorId"]!;
        Assert.Equal(fixture.ActorId, actorId);
        var expected = new ConfirmedFileUsage((string)originalUsage["EID"]!, (string)originalUsage["FileId"]!);
        FileOwnerReference originalOwner;
        string contentLinkId, documentId;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            documentId = (string)(await db.Set<Dictionary<string, object?>>("Document").AsNoTracking().SingleAsync())["EID"]!;
            var actualLink = await db.Set<Dictionary<string, object?>>("DocumentFileLink").AsNoTracking().SingleAsync();
            Assert.Equal(expected.UsageId, actualLink["FileUsageId"]);
            Assert.Equal(expected.FileId, actualLink["CEID"]);
            originalOwner = new(DocumentFileOwnerPolicy.Key, (string)actualLink["LID"]!);
            contentLinkId = (string)(await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking().SingleAsync(row => EF.Property<string>(row, "ChildModelKey") == "Document"))["LID"]!;
        }
        // confirmed 的 ExpiresAt 只是历史；真实关联仍 active 时不得把重复确认当到期 token。
        await ExpireAsync(fixture, uploadId);
        Assert.Equal(expected, await ConfirmAsActualActorAsync(fixture, actorId, uploadId, originalOwner, documentId));
        Assert.Equal(expected, await ConfirmAsActualActorAsync(fixture, actorId, uploadId, originalOwner, documentId));

        var otherStage = await StageAsync(fixture, admin, position, Values("CONFIRM-OTHER"), Bytes);
        using (var otherCreated = await CreateDocumentAsync(fixture, admin, position, "CONFIRM-OTHER", otherStage.GetProperty("uploadId").GetString()!))
            Assert.Equal(HttpStatusCode.OK, otherCreated.StatusCode);
        FileOwnerReference otherOwner;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var otherLink = await db.Set<Dictionary<string, object?>>("DocumentFileLink").AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "LID") != originalOwner.Id);
            otherOwner = new(DocumentFileOwnerPolicy.Key, (string)otherLink["LID"]!);
        }
        var retarget = await Assert.ThrowsAsync<FileLifecycleException>(() => ConfirmAsActualActorAsync(fixture, actorId, uploadId, otherOwner, documentId));
        Assert.Equal(409, retarget.StatusCode);
        Assert.Equal("Files.UploadAlreadyConfirmed", retarget.Code);
        using (var deleted = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.deleteObject"), HttpMethod.Delete,
                   "/api/workspace/content/" + contentLinkId, new { position })) Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var released = await Assert.ThrowsAsync<FileLifecycleException>(() => ConfirmAsActualActorAsync(fixture, actorId, uploadId, originalOwner, documentId));
        Assert.Equal(409, released.StatusCode);
        Assert.Equal("Files.UploadAlreadyConfirmed", released.Code);
        var final = await ObserveAsync(fixture);
        Assert.Equal(2, final.Usages.Length);
        Assert.Equal("released", Assert.Single(final.Usages, row => Equals(row["EID"], expected.UsageId))["State"]);
        Assert.Equal("confirmed", Assert.Single(final.Leases, row => Equals(row["EID"], uploadId))["State"]);
    }

    private static async Task<ConfirmedFileUsage> ConfirmAsActualActorAsync(BusinessAuditHostFixture fixture, string actorId,
        string uploadId, FileOwnerReference owner, string documentId)
    {
        using var scope = fixture.Services.CreateScope();
        var accessor = fixture.Services.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorId)], "ActualFixtureActor")),
            RequestServices = scope.ServiceProvider
        };
        try
        {
            Assert.IsType<DocumentFileOwnerPolicy>(Assert.Single(scope.ServiceProvider.GetServices<IFileOwnerPolicy>(), policy => policy.OwnerKey == owner.Key));
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.Empty(db.ChangeTracker.Entries());
            var service = scope.ServiceProvider.GetRequiredService<IFileLifecycleService>();
            var transaction = scope.ServiceProvider.GetRequiredService<IPlatformTransactionExecutor>();
            var result = await transaction.ExecuteAsync(token => new ValueTask<ConfirmedFileUsage>(service.ConfirmUsageAsync(uploadId, owner,
                new(new(new("document.document.internal.create", "standardCrud.create", "submit"), "document.document", null)
                { Internal = new("document.document.internal.create", "standardCrud.create", typeof(DocumentFiles), nameof(DocumentFiles.CreateWithFilesAsync)) }, documentId), token)));
            Assert.DoesNotContain(db.ChangeTracker.Entries(), entry => entry.Metadata.Name == "DocumentFileLink" && entry.State == EntityState.Added);
            return result;
        }
        finally { accessor.HttpContext = previous; }
    }

    private static Task<BusinessAuditHostFixture> CreateFixtureAsync(FaultControl control) => BusinessAuditHostFixture.CreateAsync(services =>
    {
        var worker = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(FileCleanupWorker));
        services.Remove(worker);
        services.Replace(ServiceDescriptor.Singleton<TimeProvider>(control.Clock));
        services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(control.Transactions, control.Commands));
        services.AddSingleton<IFileContentStore>(provider => new ControlledLocalStore(provider.GetRequiredService<LocalStorageProvider>(), control));
    }, includeWorkspace: true);

    private static object Values(string code) => new { ECODE = code, ENAME = "真实文件故障", Description = "SQL/Local" };
    private static Task<HttpResponseMessage> CreateDocumentAsync(BusinessAuditHostFixture fixture, HttpClient admin, WorkspacePosition position,
        string code, string uploadId) => FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.createDocument"), HttpMethod.Post,
            "/api/workspace/content/documents", new { position, values = Values(code), uploads = new { mainUploadId = uploadId } });
    private static string ContentPath(BusinessAuditHostFixture fixture, IReadOnlyDictionary<string, object?> file) => Path.Combine(fixture.StorageRoot, (string)file["StorageKey"]!);

    // 观察永远用请求/失败事务退出后的 fresh scope 与无跟踪查询。
    private static async Task<DurableFacts> ObserveAsync(BusinessAuditHostFixture fixture)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        await AssertContentContainerUnchangedAsync(fixture);
        return new(await db.Set<Dictionary<string, object?>>("FileRecord").AsNoTracking().ToArrayAsync(),
            await db.Set<Dictionary<string, object?>>("FileUploadLease").AsNoTracking().ToArrayAsync(),
            await db.Set<Dictionary<string, object?>>("FileUsage").AsNoTracking().ToArrayAsync());
    }

    private static async Task ExpireAsync(BusinessAuditHostFixture fixture, string uploadId)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var now = fixture.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        Assert.Equal(1, await db.Set<Dictionary<string, object?>>("FileUploadLease").Where(row => EF.Property<string>(row, "EID") == uploadId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<DateTime>(row, "ExpiresAt"), now)));
    }

    private static async Task<int> CleanupAsync(BusinessAuditHostFixture fixture)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FileCleanupService>().RunOnceAsync();
    }
    private static async Task<CleanupOutcome> CleanupOutcomeAsync(BusinessAuditHostFixture fixture)
    {
        try { return new(await CleanupAsync(fixture), null); }
        catch (Exception exception) { return new(0, exception); }
    }
    private static async Task<HttpOutcome> HttpOutcomeAsync(Func<Task<HttpResponseMessage>> send)
    {
        try { using var response = await send(); return new(response.StatusCode, null); }
        catch (Exception exception) { return new(null, exception); }
    }

    // 释放所有屏障后等待参与者退出；后续等待失败不得覆盖最初的断言或 SQL 异常。
    private static async Task JoinAfterReleaseAsync(Exception? primary, params Task?[] participants)
    {
        foreach (var participant in participants)
        {
            if (participant is null) continue;
            try { await participant.WaitAsync(BarrierTimeout); }
            catch (Exception exception) { primary ??= exception; }
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    // 独立 SQL Session 只试取同一正式文件锁，不等待；意外取得的锁先释放，再报告失败。
    private static async Task AssertFileLockHeldAsync(BusinessAuditHostFixture fixture, string fileId)
    {
        string connection;
        using (var scope = fixture.Services.CreateScope()) connection = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync();
        await using var command = sql.CreateCommand();
        command.CommandText = "DECLARE @r int; EXEC @r=sp_getapplock @Resource=@key,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; IF @r>=0 EXEC sp_releaseapplock @Resource=@key,@LockOwner='Session'; SELECT @r;";
        command.Parameters.AddWithValue("@key", "Files." + fileId);
        Assert.Equal(-1, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    private sealed record DurableFacts(Dictionary<string, object?>[] Files, Dictionary<string, object?>[] Leases, Dictionary<string, object?>[] Usages);
    private sealed record HttpOutcome(HttpStatusCode? Status, Exception? Exception)
    { public bool Succeeded => Exception is null && Status is { } status && (int)status >= 200 && (int)status < 300; }
    private sealed record CleanupOutcome(int Cleaned, Exception? Exception);
    private sealed class FaultControl
    {
        public MutableClock Clock { get; } = new();
        public IoControl Io { get; } = new();
        public TransactionControl Transactions { get; } = new();
        public CommandControl Commands { get; }
        public FaultControl() => Commands = new(Transactions);
    }
    private sealed class MutableClock : TimeProvider
    {
        private long _ticks = DateTime.UtcNow.Ticks;
        public override DateTimeOffset GetUtcNow() => new(new DateTime(Interlocked.Read(ref _ticks), DateTimeKind.Utc));
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
    }
    private sealed class IoControl
    {
        public string? BarrierPoint { get; set; }
        public int RealWrites;
        public int DeleteFailures;
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AbortObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class ControlledLocalStore(LocalStorageProvider inner, FaultControl control) : IFileContentStore
    {
        public async Task<FileContentMeasurement> WritePartialAsync(string key, Stream source, long maximumBytes, CancellationToken token)
        {
            Assert.False(control.Transactions.HasActiveFilesTransaction);
            if (control.Io.BarrierPoint == "open") await PauseAsync(token);
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref control.Io.RealWrites);
            return await inner.WritePartialAsync(key, source, maximumBytes, token);
        }
        public async Task CompleteAsync(string key, CancellationToken token)
        {
            Assert.False(control.Transactions.HasActiveFilesTransaction);
            if (control.Io.BarrierPoint == "rename") await PauseAsync(token);
            token.ThrowIfCancellationRequested();
            await inner.CompleteAsync(key, token);
        }
        public Task<Stream> OpenAsync(string key, CancellationToken token) => inner.OpenAsync(key, token);
        public Task DeleteContentAsync(string key, CancellationToken token)
        {
            Assert.False(control.Transactions.HasActiveFilesTransaction);
            if (Interlocked.CompareExchange(ref control.Io.DeleteFailures, 0, 1) == 1) throw new IOException("Injected Local delete failure.");
            return inner.DeleteContentAsync(key, token);
        }
        private async Task PauseAsync(CancellationToken token)
        {
            using var cancelled = token.Register(() => control.Io.AbortObserved.TrySetResult());
            control.Io.Reached.TrySetResult();
            // 模拟迟到 writer，在测试释放前保留 gate；等待有上界，取消仍在实际 I/O 前生效。
            await control.Io.Resume.Task.WaitAsync(BarrierTimeout);
        }
    }

    private sealed class InjectedStageCommitFailure : Exception { }
    private sealed class TransactionControl : DbTransactionInterceptor
    {
        private readonly ConcurrentDictionary<Guid, DbContext?> _active = new();
        private readonly ConcurrentDictionary<Guid, byte> _filesContexts = new();
        private int _phase;
        private int _target;
        private bool _afterCommit;
        private Guid _targetTransaction;
        public int InjectedFailures;
        public bool BlockConfirmCommit { get; set; }
        public TaskCompletionSource ConfirmCommitReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ResumeConfirm { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HasActiveFilesTransaction => _active.Values.Any(context => context is not null && _filesContexts.ContainsKey(context.ContextId.InstanceId));

        public void ArmStageFailure(int phase, bool afterCommit) { _phase = 0; _target = phase; _afterCommit = afterCommit; }
        public void ObserveFilesCommand(DbContext? context, DbCommand command)
        {
            if (context is null) return;
            if (command.CommandText.Contains("[FileRecord]", StringComparison.Ordinal) || command.CommandText.Contains("[FileUploadLease]", StringComparison.Ordinal)
                || command.CommandText.Contains("[FileUsage]", StringComparison.Ordinal)
                || command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is string key && key.StartsWith("Files.", StringComparison.Ordinal)))
                _filesContexts.TryAdd(context.ContextId.InstanceId, 0);
        }
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        { _active[eventData.TransactionId] = eventData.Context; return ValueTask.FromResult(result); }
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            var entries = eventData.Context?.ChangeTracker.Entries().ToArray() ?? [];
            if (BlockConfirmCommit && entries.Any(entry => entry.Metadata.Name == "DocumentFileLink") && entries.Any(entry => entry.Metadata.Name == "FileUsage"))
            {
                ConfirmCommitReached.TrySetResult();
                await ResumeConfirm.Task.WaitAsync(BarrierTimeout, cancellationToken);
            }
            if (_target > 0 && entries.Any(entry => entry.Metadata.Name == "FileRecord") && entries.Any(entry => entry.Metadata.Name == "FileUploadLease"))
            {
                if (++_phase == _target)
                {
                    _targetTransaction = eventData.TransactionId;
                    if (!_afterCommit) { _target = 0; Interlocked.Increment(ref InjectedFailures); throw new InjectedStageCommitFailure(); }
                }
            }
            return result;
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            _active.TryRemove(eventData.TransactionId, out _);
            if (_target > 0 && _afterCommit && _targetTransaction == eventData.TransactionId)
            { _target = 0; Interlocked.Increment(ref InjectedFailures); throw new InjectedStageCommitFailure(); }
            return Task.CompletedTask;
        }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { _active.TryRemove(eventData.TransactionId, out _); return Task.CompletedTask; }
    }

    private sealed class CommandControl(TransactionControl transactions) : DbCommandInterceptor
    {
        public bool WatchCleanup { get; set; }
        public bool WatchStagedRead { get; set; }
        public bool WatchCancelRead { get; set; }
        public TaskCompletionSource CleanupDispatched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StagedReadDispatched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancelReadDispatched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            transactions.ObserveFilesCommand(eventData.Context, command);
            if (WatchCleanup && command.CommandText.Contains("[FileRecord]", StringComparison.Ordinal) && command.CommandText.Contains("cleanupPending", StringComparison.Ordinal)
                && command.CommandText.Contains("[FileUsage]", StringComparison.Ordinal)) CleanupDispatched.TrySetResult();
            // 暂存读取先查整批 actor/用途归属，再于服务内区分状态；不能依赖旧 state SQL 预过滤。
            if (WatchStagedRead && command.CommandText.Contains("[FileUploadLease]", StringComparison.Ordinal)
                && command.CommandText.Contains("[ActorId]", StringComparison.Ordinal) && command.CommandText.Contains("[UsageOwnerKey]", StringComparison.Ordinal))
                StagedReadDispatched.TrySetResult();
            if (WatchCancelRead && command.CommandText.Contains("[FileUploadLease]", StringComparison.Ordinal)) CancelReadDispatched.TrySetResult();
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { transactions.ObserveFilesCommand(eventData.Context, command); return ValueTask.FromResult(result); }
    }
}
