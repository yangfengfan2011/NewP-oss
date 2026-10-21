using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Modules.Document;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Files copy intent/ready提交前失败及提交后丢回执分别按持久事实恢复，prepared不误删也不能借旧owner确认")]
    public async Task ObjectCopy_DocumentFilesPreparationCommitFailuresHaveDistinctDurableOutcomes()
    {
        var fault = new CopyCommitFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(fault)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "准备提交来源");
        await CreateCopyDocument(fixture, admin, source, "DOC-PREP-COMMIT");
        var link = (await Rows(fixture, "DocumentFileLink")).OrderBy(item => (long)item["Ordinal"]!).First();
        var documentId = (string)Assert.Single(await Rows(fixture, "Document"))["EID"]!;
        var reference = new FileUsageReference((string)link["FileUsageId"]!, (string)link["CEID"]!, new(DocumentFileOwnerPolicy.Key, (string)link["LID"]!));
        foreach (var phase in new[] { "intent", "ready" })
            foreach (var after in new[] { false, true })
            {
                var key = "prepare-" + phase + "-" + after;
                fault.Arm(phase, after);
                await AsCopyActor(fixture, async provider => {
                    var identity = await provider.GetRequiredService<DocumentInitialIdentity>().ReadCurrentAsync(documentId);
                    using var origin = provider.GetRequiredService<DocumentFileCopyScope>().Begin(identity, DocumentFileCopyPhase.Prepare, out var sourceContext);
                    await Assert.ThrowsAnyAsync<Exception>(() => provider.GetRequiredService<IFileCopyService>().PrepareCopyAsync(key, reference, DocumentFileOwnerPolicy.Key, sourceContext));
                    return true;
                });
                Assert.Equal(1, fault.Injected);
                var durable = await AsCopyActor(fixture, provider => provider.GetRequiredService<IFileCopyService>().ReadCopyAsync(key, DocumentFileOwnerPolicy.Key));
                if (phase == "intent" && !after) Assert.Null(durable);
                else Assert.Equal(phase == "ready" && after ? "prepared" : "cancelled", durable!.State);
                if (phase == "ready" && after)
                {
                    await AsCopyActor(fixture, async provider => {
                        var exception = await Assert.ThrowsAsync<FileLifecycleException>(async () => await provider.GetRequiredService<IPlatformTransactionExecutor>()
                            .ExecuteAsync(async token =>
                            {
                                var identity = await provider.GetRequiredService<DocumentInitialIdentity>().ReadCurrentAsync(documentId, token);
                                using var origin = provider.GetRequiredService<DocumentFileCopyScope>().Begin(identity, DocumentFileCopyPhase.Confirm, out var sourceContext);
                                using var targetProof = provider.GetRequiredService<DocumentFileCreationScope>().BeginCopied(identity, out var creation);
                                return await provider.GetRequiredService<IFileCopyService>().ConfirmCopyAsync(key, reference.Owner, sourceContext, creation, token);
                            }));
                        Assert.Equal("Document.CreationProofRequired", exception.Code);
                        return true;
                    });
                }
                await AsCopyActor(fixture, async provider => { await provider.GetRequiredService<IFileCopyService>().CancelCopyAsync(key, DocumentFileOwnerPolicy.Key); return true; });
                await CleanDocumentCopies(fixture);
                Assert.Single(await Rows(fixture, "Document")); Assert.Equal(3, (await Rows(fixture, "FileUsage")).Length);
                Assert.Equal(3, Directory.EnumerateFiles(fixture.StorageRoot).Count());
            }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "业务最终commit前失败整体回滚，commit后未知保留完整图，Host重建后只核对结果不重放或盲删")]
    public async Task ObjectCopy_DocumentBusinessCommitUnknownPreservesTheGraphAndControlledResult()
    {
        var fault = new CopyCommitFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(fault)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "业务提交来源"); var target = await Folder(fixture, admin, root, "业务提交目标");
        var row = await CreateCopyDocument(fixture, admin, source, "DOC-BUSINESS-COMMIT");
        string? committedOperation = null;
        foreach (var after in new[] { false, true })
        {
            var operation = await PrepareDocumentCopy(fixture, admin, source, target, row.LinkId);
            fault.Arm("business", after);
            using var response = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = operation,
                values = new { ECODE = "DOC-BUSINESS-COPY", ENAME = "未知结果副本" } });
            Assert.False(response.IsSuccessStatusCode); Assert.Equal(1, fault.Injected);
            var result = await Command(fixture, admin, "workspace.folder.resolveObjectCopy", "/api/workspace/content/objects/copy/result", new { operationId = operation });
            Assert.Equal(after, result.GetProperty("succeeded").GetBoolean());
            var cancelled = await Command(fixture, admin, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy/cancel", new { operationId = operation });
            Assert.Equal(after ? "committed" : "cancelled", cancelled.GetProperty("state").GetString());
            Assert.Equal(after ? 2 : 1, (await Rows(fixture, "Document")).Length);
            await CleanDocumentCopies(fixture); Assert.Equal(after ? 6 : 3, Directory.EnumerateFiles(fixture.StorageRoot).Count());
            if (after) committedOperation = operation;
        }
        Assert.Single(await Rows(fixture, "BusinessChange"), item => Equals(item["EventType"], "workspace.object.copied"));
        await fixture.RestartWithActionsAsync(new Dictionary<string, bool>());
        using var restarted = fixture.CreateClient(); await BusinessAuditHostFixture.RefreshAntiforgeryAsync(restarted);
        using (var login = await restarted.PostAsJsonAsync("/api/auth/login", new { ECODE = "admin", Password = "Audit-test-secure-938!" })) Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(restarted);
        var recovered = await Command(fixture, restarted, "workspace.folder.resolveObjectCopy", "/api/workspace/content/objects/copy/result", new { operationId = committedOperation });
        Assert.Equal("committed", recovered.GetProperty("state").GetString());
        var copy = Assert.Single(await Rows(fixture, "Document"), item => Equals(item["ECODE"], "DOC-BUSINESS-COPY"));
        await AssertCopiedDocumentDownloads(fixture, restarted, (string)copy["EID"]!);
        Assert.Equal(2, (await Rows(fixture, "Document")).Length); Assert.Equal(6, (await Rows(fixture, "FileUsage")).Count(item => Equals(item["State"], "active")));
    }

    // 使用真实认证主体和Document owner policy消费Files合同，不以宽松policy替身证明新接缝。
    private static async Task<T> AsCopyActor<T>(BusinessAuditHostFixture fixture, Func<IServiceProvider, Task<T>> action, string? actorId = null)
    {
        using var scope = fixture.Services.CreateScope(); var accessor = fixture.Services.GetRequiredService<IHttpContextAccessor>(); var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actorId ?? fixture.ActorId)], "ActualFixtureActor")), RequestServices = scope.ServiceProvider };
        try { return await action(scope.ServiceProvider); } finally { accessor.HttpContext = previous; }
    }

    private sealed class CopyCommitFault : DbTransactionInterceptor
    {
        private string? phase; private bool after; private Guid? target;
        public int Injected { get; private set; }
        public void Arm(string next, bool afterCommit) { phase = next; after = afterCommit; target = null; Injected = 0; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
        {
            var entries = data.Context?.ChangeTracker.Entries().ToArray() ?? [];
            var copy = entries.FirstOrDefault(entry => entry.Metadata.Name == "FileCopyLease");
            var state = copy?.Property("State").CurrentValue as string;
            var matches = phase switch { "intent" => state == "receiving", "ready" => state == "prepared", "business" => state == "confirmed" && entries.Any(entry => entry.Metadata.Name == "DocumentFileLink"), _ => false };
            if (matches) { target = data.TransactionId; if (!after) Fail(); }
            return ValueTask.FromResult(result);
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData data, CancellationToken token = default)
        { if (phase is not null && after && target == data.TransactionId) Fail(); return Task.CompletedTask; }
        private void Fail() { var selected = phase; phase = null; Injected++; throw new InvalidOperationException("COPY_COMMIT_FAULT_" + selected + "_" + (after ? "AFTER" : "BEFORE")); }
    }
}
