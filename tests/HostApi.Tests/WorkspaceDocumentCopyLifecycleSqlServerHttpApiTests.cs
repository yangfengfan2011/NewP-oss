using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Files.Services;
using ICP.Platform.Modules.Files.Storage;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Testing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DocFiles = ICP.Platform.HostApi.Tests.DocumentFilesSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "复制intent尚未写入时取消也形成持久拒绝，迟到intent不能开始传输或留下准备")]
    public async Task ObjectCopy_DocumentCancellationBeforeCopyIntentPreventsLateWrites()
    {
        var barrier = new ObjectCopyWriteFault { PauseTable = "FileCopyLease" }; var io = new CopyIo();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => {
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(barrier));
            services.AddSingleton<IFileContentStore>(provider => { io.Inner = provider.GetRequiredService<LocalStorageProvider>(); return io; });
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "取消迟到来源"); var target = await Folder(fixture, admin, root, "取消迟到目标");
        var row = await CreateCopyDocument(fixture, admin, source, "DOC-CANCEL-INTENT");
        var preparation = await PrepareDocumentCopy(fixture, admin, source, target, row.LinkId);
        barrier.Pause = true; io.Enabled = true;
        var pending = Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = preparation, values = new { ECODE = "CANCEL-LATE", ENAME = "不得迟到" } });
        try
        {
            await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var cancellation = Command(fixture, admin, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy/cancel", new { operationId = preparation });
            // 先观测业务取消持久化；Files短事务中的intent登记/取消仍需串行裁决，不能把请求发出当作完成。
            for (var attempt = 0; attempt < 100 && !Equals(Assert.Single(await Rows(fixture, "WorkspaceObjectCopy"))["State"], "cancelled"); attempt++) await Task.Delay(20);
            Assert.Equal("cancelled", Assert.Single(await Rows(fixture, "WorkspaceObjectCopy"))["State"]);
            barrier.Pause = false; barrier.Release.TrySetResult();
            var cancelled = await cancellation.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("cancelled", cancelled.GetProperty("state").GetString());
            var writesAtAcknowledgement = io.Writes;
            using var response = await pending; await Problem(response, HttpStatusCode.Conflict, "Workspace.CopyCancelled");
            Assert.Equal(writesAtAcknowledgement, io.Writes);
        }
        finally { barrier.Pause = false; barrier.Release.TrySetResult(); (await pending).Dispose(); }
        Assert.Single(await Rows(fixture, "Document"));
        Assert.DoesNotContain(await Rows(fixture, "FileCopyLease"), item => Equals(item["State"], "prepared") || Equals(item["State"], "confirmed"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "源download拒绝、引用copy拒绝且无edit/delete的有限主体仍可按真副本资格完整内部复制")]
    public async Task ObjectCopy_DocumentInternalCopyDoesNotBorrowDownloadOrReferenceRights()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "独立文件资格源"); var target = await Folder(fixture, admin, root, "独立文件资格目标");
        var row = await CreateCopyDocument(fixture, admin, source, "DOC-RIGHTS");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "DocumentCopyActor", "图文档副本", "document-copy", "图文档副本账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.copy"), Point(fixture, "workspace.folder.pasteObjectCopy"), Point(fixture, "workspace.folder.paste"),
            DocFiles.Point(fixture, "document.document.read"), DocFiles.Point(fixture, "document.document.download"));
        using var ordinary = actor.Client;
        foreach (var action in new[] { "download", "documentCopy", "edit", "delete" }) await Rule(fixture, admin, "DenyDoc" + action, "document.document", row.Id, action, "deny");
        var list = await DocumentFileHttpTestRequests.ReadAsync(fixture, ordinary, DocFiles.Point(fixture, "document.document.read"), $"/api/document/documents/{row.Id}/files");
        var fileLink = list.GetProperty("files")[0].GetProperty("fileLinkId").GetString();
        using (var denied = await DocumentFileHttpTestRequests.SendAsync(fixture, ordinary, DocFiles.Point(fixture, "document.document.download"), HttpMethod.Get, $"/api/document/documents/{row.Id}/files/{fileLink}/content"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await Command(fixture, ordinary, "workspace.folder.copy", "/api/workspace/content/clipboard/validate", new { mode = "copy", sourcePosition = source, linkIds = new[] { row.LinkId } });
        using (var deniedReference = await Send(fixture, ordinary, "workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = source, linkIds = new[] { row.LinkId }, targetPosition = target }))
            await Problem(deniedReference, HttpStatusCode.Forbidden, "Workspace.ObjectCopyDenied");
        var preparation = await PrepareDocumentCopy(fixture, ordinary, source, target, row.LinkId);
        using var copied = await Send(fixture, ordinary, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = preparation, values = new { ECODE = "DOC-RIGHTS-COPY", ENAME = "内部副本" } });
        Assert.True(copied.StatusCode == HttpStatusCode.OK, await copied.Content.ReadAsStringAsync());
        Assert.Equal(2, (await Rows(fixture, "Document")).Length); Assert.Equal(6, (await Rows(fixture, "FileUsage")).Length);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "锁外字节准备期间源正常编辑不阻塞，最终拒绝旧regular字段确认且不提交半副本")]
    public async Task ObjectCopy_DocumentSourceChangesDuringIoRequireReconfirmation()
    {
        var io = new CopyIo();
        await using var fixture = await CopyIoFixture(io);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "传输变化源"); var target = await Folder(fixture, admin, root, "传输变化目标");
        var row = await CreateCopyDocument(fixture, admin, source, "DOC-CHANGE");
        var preparation = await PrepareDocumentCopy(fixture, admin, source, target, row.LinkId);
        io.PauseAtWrite = 1; io.Enabled = true;
        var pending = Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = preparation, values = new { ECODE = "DOC-CHANGE-COPY", ENAME = "主动输入" } });
        try
        {
            await io.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            using var edit = await FeatureHttpTestRequests.SendAsync(admin, RowPoint(fixture, "edit"), HttpMethod.Put, "/api/workspace/content/" + row.LinkId,
                new { position = source, values = new { ENAME = "传输期间源已变化" } }).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
            io.Release.TrySetResult();
            using var response = await pending;
            await Problem(response, HttpStatusCode.Conflict, "Workspace.CopySourceChanged");
        }
        finally { io.Release.TrySetResult(); (await pending).Dispose(); }
        Assert.Single(await Rows(fixture, "Document")); Assert.Equal(3, (await Rows(fixture, "FileUsage")).Length);
        Assert.DoesNotContain(await Rows(fixture, "BusinessChange"), item => Equals(item["EventType"], "workspace.object.copied"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "字节传输期间源正常删除与回收不受长图锁阻塞，持久来源保护防误删，副本最终拒绝并回收全部孤儿")]
    public async Task ObjectCopy_DocumentSourceDeleteDuringIoProtectsContentUntilTheCopyExits()
    {
        var io = new CopyIo(); await using var fixture = await CopyIoFixture(io);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "传输删除源"); var target = await Folder(fixture, admin, root, "传输删除目标");
        var row = await CreateCopyDocument(fixture, admin, source, "DOC-DELETE");
        var preparation = await PrepareDocumentCopy(fixture, admin, source, target, row.LinkId);
        io.PauseAtWrite = 1; io.Enabled = true;
        var pending = Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = preparation, values = new { ECODE = "DOC-DELETE-COPY", ENAME = "不可提交" } });
        try
        {
            await io.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Command(fixture, admin, "workspace.folder.deleteObjects", "/api/workspace/content/delete", new { position = source, linkIds = new[] { row.LinkId } }).WaitAsync(TimeSpan.FromSeconds(10));
            using (var scope = fixture.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<FileCleanupService>().RunOnceAsync();
            var protectedFileId = (string)Assert.Single(await Rows(fixture, "FileCopyLease"))["SourceFileId"]!;
            Assert.Contains(await Rows(fixture, "FileRecord"), item => Equals(item["EID"], protectedFileId) && !Equals(item["State"], "deleted"));
            Assert.True(Directory.EnumerateFiles(fixture.StorageRoot).Any());
            io.Release.TrySetResult();
            using var response = await pending; Assert.False(response.IsSuccessStatusCode);
        }
        finally { io.Release.TrySetResult(); (await pending).Dispose(); }
        for (var attempt = 0; attempt < 3; attempt++) { using var scope = fixture.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<FileCleanupService>().RunOnceAsync(); }
        Assert.Empty(await Rows(fixture, "Document")); Assert.Empty(Directory.EnumerateFiles(fixture.StorageRoot));
        Assert.DoesNotContain(await Rows(fixture, "FileUsage"), item => Equals(item["State"], "active"));
    }

    private static Task<BusinessAuditHostFixture> CopyIoFixture(CopyIo io) => BusinessAuditHostFixture.CreateAsync(services =>
        services.AddSingleton<IFileContentStore>(provider => { io.Inner = provider.GetRequiredService<LocalStorageProvider>(); return io; }), includeWorkspace: true);
    private static async Task<(string Id, string LinkId)> CreateCopyDocument(BusinessAuditHostFixture fixture, HttpClient admin, WorkspacePosition source, string code)
    {
        var values = new { ECODE = code, ENAME = code + " source", Description = "源说明" }; var tokens = new string[3];
        for (var index = 0; index < tokens.Length; index++) tokens[index] = (await DocFiles.StageAsync(fixture, admin, source, values, [1, 2, 3, (byte)index], $"source-{index}.pdf")).GetProperty("uploadId").GetString()!;
        await Command(fixture, admin, "workspace.folder.createDocument", "/api/workspace/content/documents", new { position = source, values, uploads = new { mainUploadId = tokens[0], attachmentUploadIds = tokens[1..] } });
        var master = Assert.Single(await Rows(fixture, "Document"), item => Equals(item["ECODE"], code));
        var link = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), item => Equals(item["CEID"], master["EID"]));
        return ((string)master["EID"]!, (string)link["LID"]!);
    }
    private static async Task<string> PrepareDocumentCopy(BusinessAuditHostFixture fixture, HttpClient client, WorkspacePosition source, WorkspacePosition target, string linkId) =>
        (await Command(fixture, client, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy/prepare", new { mode = "copy", sourcePosition = source, linkIds = new[] { linkId }, targetPosition = target })).GetProperty("operationId").GetString()!;

    private sealed class CopyIo : IFileContentStore
    {
        public LocalStorageProvider Inner { private get; set; } = null!;
        public bool Enabled { get; set; }
        public int PauseAtWrite { get; set; }
        private int writes;
        private int reads, completions, injected;
        public int Writes => writes;
        public string? FaultPhase { get; private set; }
        public int FaultIndex { get; private set; }
        public int Injected => injected;
        public void Arm(string phase, int index) { Enabled = true; writes = reads = completions = injected = 0; FaultPhase = phase; FaultIndex = index; }
        private void Fail(string phase, int index) { if (FaultPhase == phase && FaultIndex == index && Interlocked.CompareExchange(ref injected, 1, 0) == 0) throw new IOException("COPY_IO_FAULT_" + phase + "_" + index); }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<FileContentMeasurement> WritePartialAsync(string key, Stream source, long maximumBytes, CancellationToken token)
        {
            var index = Enabled ? Interlocked.Increment(ref writes) : 0;
            if (Enabled && index == PauseAtWrite) { Reached.TrySetResult(); await Release.Task.WaitAsync(token); }
            var measured = await Inner.WritePartialAsync(key, source, maximumBytes, token);
            if (Enabled) Fail("write", index);
            return measured;
        }
        public async Task CompleteAsync(string key, CancellationToken token) { var index = Enabled ? Interlocked.Increment(ref completions) : 0; await Inner.CompleteAsync(key, token); if (Enabled) Fail("complete", index); }
        public Task<Stream> OpenAsync(string key, CancellationToken token) { if (Enabled) Fail("read", Interlocked.Increment(ref reads)); return Inner.OpenAsync(key, token); }
        public Task DeleteContentAsync(string key, CancellationToken token) => Inner.DeleteContentAsync(key, token);
    }
}
