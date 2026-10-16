using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Document;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "必填与已知候选拒绝先于文件IO，不把完整文件复制当成字段校验前置")]
    public async Task ObjectCopy_DocumentInvalidCandidateDoesNotPrepareAnyBytes()
    {
        var io = new CopyIo(); await using var fixture = await CopyIoFixture(io);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "候选预检来源"); var target = await Folder(fixture, admin, root, "候选预检目标");
        var row = await CreateCopyDocument(fixture, admin, source, "DOC-CANDIDATE-SOURCE");
        var operation = await PrepareDocumentCopy(fixture, admin, source, target, row.LinkId); io.Enabled = true;
        using var response = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = operation, values = new { ECODE = "", ENAME = "缺少新编码" } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, io.Writes); Assert.Empty(await Rows(fixture, "FileCopyLease"));
        Assert.Equal("ready", Assert.Single(await Rows(fixture, "WorkspaceObjectCopy"))["State"]);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实Document owner拒绝伪造来源、裸文件或旧上传token作为复制凭据，其他主体不能读取或取消本主体准备")]
    public async Task ObjectCopy_DocumentCopyCredentialsCannotBeForgedOrTransferred()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "复制凭据来源");
        await CreateCopyDocument(fixture, admin, source, "DOC-COPY-CREDENTIAL");
        var links = await Rows(fixture, "DocumentFileLink");
        var first = links[0]; var second = links[1];
        var reference = new FileUsageReference((string)first["FileUsageId"]!, (string)first["CEID"]!, new(DocumentFileOwnerPolicy.Key, (string)first["LID"]!));
        var forgeries = new[] {
            reference with { FileId = (string)second["CEID"]! },
            reference with { UsageId = (string)second["FileUsageId"]! },
            reference with { Owner = new(DocumentFileOwnerPolicy.Key, (string)second["LID"]!) },
            reference with { Owner = new("invented-owner", reference.Owner.Id) },
        };
        foreach (var forged in forgeries)
            await AsCopyActor(fixture, async provider => {
                await Assert.ThrowsAnyAsync<Exception>(() => provider.GetRequiredService<IFileCopyService>().PrepareCopyAsync("forged-" + Guid.NewGuid().ToString("N"), forged, DocumentFileOwnerPolicy.Key));
                return true;
            });
        Assert.Empty(await Rows(fixture, "FileCopyLease"));
        var upload = (string)(await Rows(fixture, "FileUploadLease"))[0]["EID"]!;
        await AsCopyActor(fixture, async provider => {
            var copies = provider.GetRequiredService<IFileCopyService>();
            Assert.Null(await copies.ReadCopyAsync(upload, DocumentFileOwnerPolicy.Key));
            await Assert.ThrowsAsync<FileLifecycleException>(async () => await provider.GetRequiredService<IPlatformTransactionExecutor>()
                .ExecuteAsync(token => new ValueTask<ConfirmedFileUsage>(copies.ConfirmCopyAsync(upload, reference.Owner, token))));
            var prepared = await copies.PrepareCopyAsync("real-copy-credential", reference, DocumentFileOwnerPolicy.Key);
            Assert.Equal("prepared", prepared.State); Assert.NotEqual(reference.FileId, prepared.FileId);
            var forged = await Assert.ThrowsAsync<InvalidOperationException>(() => copies.PrepareCopyAsync("real-copy-credential", reference with { FileId = (string)second["CEID"]! }, DocumentFileOwnerPolicy.Key));
            Assert.Equal("Document.FileAssociationCorrupted", forged.Message);
            return true;
        });
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "CopyOtherActor", "其他主体", "copy-other", "其他主体账号", Point(fixture, "workspace.spaces.access"),
            Point(fixture, "workspace.folder.pasteObjectCopy"), Point(fixture, "workspace.folder.resolveObjectCopy"));
        // 通过另一个真实认证主体发结果/取消请求，不能读取操作；File服务以同一主体事实隔离准备。
        var target = await Folder(fixture, admin, root, "复制凭据目标"); var row = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), item => Equals(item["ChildModelKey"], "Document"));
        var operation = await PrepareDocumentCopy(fixture, admin, source, target, (string)row["LID"]!);
        using (actor.Client)
        using (var denied = await Send(fixture, actor.Client, "workspace.folder.pasteObjectCopy", "objects/copy/cancel", new { operationId = operation }))
            await Problem(denied, HttpStatusCode.NotFound, "Workspace.CopyOperationUnavailable");
        await AsCopyActor(fixture, async provider => {
            var copies = provider.GetRequiredService<IFileCopyService>();
            Assert.Null(await copies.ReadCopyAsync("real-copy-credential", DocumentFileOwnerPolicy.Key));
            await copies.CancelCopyAsync("real-copy-credential", DocumentFileOwnerPolicy.Key);
            return true;
        }, actor.AccountId);
        await AsCopyActor(fixture, async provider => {
            Assert.Equal("prepared", (await provider.GetRequiredService<IFileCopyService>().ReadCopyAsync("real-copy-credential", DocumentFileOwnerPolicy.Key))!.State);
            await provider.GetRequiredService<IFileCopyService>().CancelCopyAsync("real-copy-credential", DocumentFileOwnerPolicy.Key);
            return true;
        });
        await CleanDocumentCopies(fixture); Assert.Equal(3, Directory.EnumerateFiles(fixture.StorageRoot).Count());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "无文件Document复制仍创建完整初始身份，严格字段与多选拒绝不会产生文件或成功审计")]
    public async Task ObjectCopy_DocumentMetadataOnlyUsesTheSameStrictContract()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "空文档来源"); var target = await Folder(fixture, admin, root, "空文档目标");
        await Command(fixture, admin, "workspace.folder.createDocument", "/api/workspace/content/documents", new { position = source, values = new { ECODE = "EMPTY-SOURCE", ENAME = "空文件集合" } });
        var link = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), item => Equals(item["ChildModelKey"], "Document"));
        var id = (string)link["LID"]!;
        using (var multiple = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy/prepare", new { mode = "copy", sourcePosition = source, linkIds = new[] { id, Guid.NewGuid().ToString() }, targetPosition = target }))
            await Problem(multiple, HttpStatusCode.BadRequest, "Workspace.ObjectCopySourceRequired");
        var operation = await PrepareDocumentCopy(fixture, admin, source, target, id);
        using (var forged = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = operation, values = new { ECODE = "EMPTY-COPY", ENAME = "空副本", LatestVersionId = "forged" } }))
            await Problem(forged, HttpStatusCode.BadRequest, "Workspace.ServerFieldForbidden");
        using var copied = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = operation, values = new { ECODE = "EMPTY-COPY", ENAME = "空副本" } });
        Assert.Equal(HttpStatusCode.OK, copied.StatusCode);
        var receipt = JsonDocument.Parse(await copied.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(new[] { "state", "succeeded" }, receipt.EnumerateObject().Select(item => item.Name).Order().ToArray());
        Assert.Equal(2, (await Rows(fixture, "DocumentVersion")).Length); Assert.Equal(2, (await Rows(fixture, "DocumentIteration")).Length);
        Assert.Empty(await Rows(fixture, "DocumentFileLink")); Assert.Empty(await Rows(fixture, "FileCopyLease")); Assert.Empty(await Rows(fixture, "FileUsage"));
        Assert.Single(await Rows(fixture, "BusinessChange"), item => Equals(item["EventType"], "workspace.object.copied"));
    }
}
