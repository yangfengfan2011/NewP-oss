using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using Xunit;
using DocFiles = ICP.Platform.HostApi.Tests.DocumentFilesSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式单Document真副本建立新master/首版/regular及全部独立文件使用，源与副本下载完整")]
    public async Task ObjectCopy_DocumentClonesTheCurrentRegularAndAllMainAttachments()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "图文档副本来源"); var target = await Folder(fixture, admin, root, "图文档副本目标");
        var values = new { ECODE = "DOC-COPY-SOURCE", ENAME = "完整源图文档", Description = "正规说明" };
        byte[][] bytes = [[1, 2, 3, 4], [5, 6, 7], [8, 9, 10, 11, 12]];
        var staged = new string[bytes.Length];
        for (var index = 0; index < staged.Length; index++)
            staged[index] = (await DocFiles.StageAsync(fixture, admin, source, values, bytes[index], $"copy-{index}.pdf")).GetProperty("uploadId").GetString()!;
        await Command(fixture, admin, "workspace.folder.createDocument", "/api/workspace/content/documents", new { position = source, values, uploads = new { mainUploadId = staged[0], attachmentUploadIds = staged[1..] } });
        var original = Assert.Single(await Rows(fixture, "Document"));
        var originalLink = Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["ChildModelKey"], "Document"));
        var prepared = await Command(fixture, admin, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy/prepare", new { mode = "copy", sourcePosition = source, linkIds = new[] { originalLink["LID"] }, targetPosition = target });
        Assert.Equal("", prepared.GetProperty("values").GetProperty("ECODE").GetString());
        await Command(fixture, admin, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy", new { operationId = prepared.GetProperty("operationId").GetString(), values = new { ECODE = "DOC-COPY-TARGET", ENAME = "独立图文档", Description = "我的说明" } });
        var copy = Assert.Single(await Rows(fixture, "Document"), row => Equals(row["ECODE"], "DOC-COPY-TARGET"));
        Assert.NotEqual(original["EID"], copy["EID"]);
        Assert.Equal(2, (await Rows(fixture, "DocumentVersion")).Length); Assert.Equal(2, (await Rows(fixture, "DocumentIteration")).Length);
        Assert.Equal(6, (await Rows(fixture, "DocumentFileLink")).Length); Assert.Equal(6, (await Rows(fixture, "FileUsage")).Length);
        Assert.Equal(6, (await Rows(fixture, "FileRecord")).Length);
        foreach (var file in await Rows(fixture, "FileRecord"))
        {
            var index = int.Parse(((string)file["FileName"]!)[5..6], System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal("application/pdf", file["ContentType"]);
            Assert.Equal((long)bytes[index].Length, file["Length"]);
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes[index])), ((string)file["Sha256"]!).ToLowerInvariant());
        }
        foreach (var documentId in new[] { (string)original["EID"]!, (string)copy["EID"]! })
        {
            var list = await FeatureHttpTestRequests.ReadAsync(admin, DocFiles.Point(fixture, "document.document.read"), $"/api/document/documents/{documentId}/files");
            var files = list.GetProperty("files").EnumerateArray().ToArray(); Assert.Equal(3, files.Length);
            for (var index = 0; index < files.Length; index++)
            {
                Assert.Equal($"copy-{index}.pdf", files[index].GetProperty("fileName").GetString());
                Assert.Equal(index == 0 ? "main" : "attachment", files[index].GetProperty("role").GetString());
                Assert.Equal(index, files[index].GetProperty("ordinal").GetInt32());
                using var download = await FeatureHttpTestRequests.SendAsync(admin, DocFiles.Point(fixture, "document.document.download"), HttpMethod.Get,
                    $"/api/document/documents/{documentId}/files/{files[index].GetProperty("fileLinkId").GetString()}/content");
                Assert.Equal(HttpStatusCode.OK, download.StatusCode); Assert.Equal(bytes[index], await download.Content.ReadAsByteArrayAsync());
            }
        }
    }
}
