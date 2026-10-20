using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Document;
using ICP.Platform.Modules.Files;
using ICP.Platform.Modules.Files.Services;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using DocFiles = ICP.Platform.HostApi.Tests.DocumentFilesSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "已有合法主附件不受后来上传政策收紧影响；连续独立副本、移除引用、双向删除及实际回收互不破坏")]
    public async Task ObjectCopy_DocumentPolicyTighteningAndIndependentDeletionKeepEveryOtherContentUsable()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "独立回收源"); var target = await Folder(fixture, admin, root, "独立回收目标");
        var alias = await Folder(fixture, admin, root, "移除后删除位置");
        var original = await CreateCopyDocument(fixture, admin, source, "DOC-INDEPENDENT");
        var originalFacts = JsonSerializer.Serialize(Assert.Single(await Rows(fixture, "Document")));
        // 政策变化只作用后续新上传；复制已有正规内容不借Stage/当前附件上限降级。
        fixture.Services.GetRequiredService<IOptions<DocumentOptions>>().Value.AllowedFileExtensions = [".txt"];
        fixture.Services.GetRequiredService<IOptions<DocumentOptions>>().Value.MaxAttachmentsPerDocument = 0;
        fixture.Services.GetRequiredService<IOptions<FilesOptions>>().Value.MaxUploadSizeBytes = 2;
        var newValues = new { ECODE = "TIGHTENED-UPLOAD", ENAME = "新上传仍执行政策" };
        using (var request = DocFiles.UploadRequest(fixture, source, newValues, new ByteArrayContent([1, 2, 3, 4]), "new.pdf"))
        using (var rejected = await admin.SendAsync(request)) await Problem(rejected, HttpStatusCode.BadRequest, "Document.ExtensionNotAllowed");
        using (var request = DocFiles.UploadRequest(fixture, source, newValues, new ByteArrayContent([1, 2, 3, 4]), "new.txt"))
        using (var rejected = await admin.SendAsync(request)) await Problem(rejected, HttpStatusCode.RequestEntityTooLarge, "Files.UploadTooLarge");
        var first = await SaveDocumentCopy(fixture, admin, source, target, original.LinkId, "DOC-INDEPENDENT-B");
        var second = await SaveDocumentCopy(fixture, admin, source, target, original.LinkId, "DOC-INDEPENDENT-C");
        Assert.Equal(originalFacts, JsonSerializer.Serialize(Assert.Single(await Rows(fixture, "Document"), item => Equals(item["EID"], original.Id))));
        Assert.Equal(9, (await Rows(fixture, "FileUsage")).Length);
        await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "copy", sourcePosition = target, linkIds = new[] { first.LinkId }, targetPosition = alias });
        await Command(fixture, admin, "workspace.folder.removeSelected", "/api/workspace/content/references/remove", new { position = target, linkIds = new[] { first.LinkId } });
        await CleanDocumentCopies(fixture);
        Assert.Equal(9, (await Rows(fixture, "FileUsage")).Count(item => Equals(item["State"], "active")));
        Assert.Equal(9, Directory.EnumerateFiles(fixture.StorageRoot).Count());
        var remaining = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), item => Equals(item["CEID"], first.Id));
        await Command(fixture, admin, "workspace.folder.deleteObjects", "/api/workspace/content/delete", new { position = alias, linkIds = new[] { remaining["LID"] } });
        await CleanDocumentCopies(fixture);
        Assert.Equal(6, Directory.EnumerateFiles(fixture.StorageRoot).Count());
        await AssertCopiedDocumentDownloads(fixture, admin, original.Id);
        await Command(fixture, admin, "workspace.folder.deleteObjects", "/api/workspace/content/delete", new { position = source, linkIds = new[] { original.LinkId } });
        await CleanDocumentCopies(fixture);
        Assert.Equal(3, Directory.EnumerateFiles(fixture.StorageRoot).Count());
        await AssertCopiedDocumentDownloads(fixture, admin, second.Id);
        await Command(fixture, admin, "workspace.folder.deleteObjects", "/api/workspace/content/delete", new { position = target, linkIds = new[] { second.LinkId } });
        await CleanDocumentCopies(fixture);
        Assert.Empty(Directory.EnumerateFiles(fixture.StorageRoot));
        Assert.All(await Rows(fixture, "FileUsage"), item => Assert.Equal("released", item["State"]));
        Assert.Equal(3, (await Rows(fixture, "FileUploadLease")).Count(item => Equals(item["State"], "confirmed")));
        Assert.Equal(6, (await Rows(fixture, "FileCopyLease")).Count(item => Equals(item["State"], "confirmed")));
        Assert.Equal(2, (await Rows(fixture, "BusinessChange")).Count(item => Equals(item["EventType"], "workspace.object.copied")));
    }

    private static async Task<(string Id, string LinkId)> SaveDocumentCopy(BusinessAuditHostFixture fixture, HttpClient client,
        ICP.Platform.Modules.Workspace.WorkspacePosition source, ICP.Platform.Modules.Workspace.WorkspacePosition target, string linkId, string code)
    {
        var operation = await PrepareDocumentCopy(fixture, client, source, target, linkId);
        await Command(fixture, client, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy", new { operationId = operation, values = new { ECODE = code, ENAME = code + " copy" } });
        var master = Assert.Single(await Rows(fixture, "Document"), item => Equals(item["ECODE"], code));
        var link = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), item => Equals(item["CEID"], master["EID"]));
        return ((string)master["EID"]!, (string)link["LID"]!);
    }

    private static async Task CleanDocumentCopies(BusinessAuditHostFixture fixture)
    {
        for (var attempt = 0; attempt < 2; attempt++) { using var scope = fixture.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<FileCleanupService>().RunOnceAsync(); }
    }

    private static async Task AssertCopiedDocumentDownloads(BusinessAuditHostFixture fixture, HttpClient client, string id)
    {
        var list = await DocumentFileHttpTestRequests.ReadAsync(fixture, client, DocFiles.Point(fixture, "document.document.read"), $"/api/document/documents/{id}/files");
        var files = list.GetProperty("files").EnumerateArray().ToArray(); Assert.Equal(3, files.Length);
        for (var index = 0; index < files.Length; index++)
        {
            Assert.Equal(index == 0 ? "main" : "attachment", files[index].GetProperty("role").GetString());
            using var download = await DocumentFileHttpTestRequests.SendAsync(fixture, client, DocFiles.Point(fixture, "document.document.download"), HttpMethod.Get,
                $"/api/document/documents/{id}/files/{files[index].GetProperty("fileLinkId").GetString()}/content");
            Assert.Equal(HttpStatusCode.OK, download.StatusCode); Assert.Equal(new byte[] { 1, 2, 3, (byte)index }, await download.Content.ReadAsByteArrayAsync());
        }
    }
}
