using System.Data.Common;
using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Files.Services;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DocFiles = ICP.Platform.HostApi.Tests.DocumentFilesSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "后文档SQL失败回滚前文档所有图和使用，成功整批逐LID对应且文件不能跨文档串用")]
    public async Task ObjectCopy_DocumentBatchLaterFailureRollsBackAndSuccessPreservesEveryMapping()
    {
        var fault = new BatchDocumentWriteFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(fault)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "集合来源"); var target = await Folder(fixture, admin, root, "集合目标");
        var links = new List<string>();
        for (var index = 1; index <= 3; index++) links.Add(await CreateBatchDocument(fixture, admin, source, index));
        var prepared = await PrepareDocumentBatch(fixture, admin, source, target, links.ToArray());
        Assert.Equal(links, prepared.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("sourceLinkId").GetString()!));
        fault.Enabled = true;
        using (var failed = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = prepared.GetProperty("operationId").GetString(), values = BatchValues(links, "BATCH-FAIL") }))
            Assert.False(failed.IsSuccessStatusCode);
        fault.Enabled = false; Assert.Equal(3, fault.Attempts);
        Assert.Equal(3, (await Rows(fixture, "Document")).Length); Assert.Equal(3, (await Rows(fixture, "DocumentVersion")).Length);
        Assert.Equal(6, (await Rows(fixture, "DocumentFileLink")).Length); Assert.Equal(6, (await Rows(fixture, "FileUsage")).Length);
        Assert.DoesNotContain(await Rows(fixture, "BusinessChange"), item => Equals(item["EventType"], "workspace.object.copied"));
        for (var index = 0; index < 2; index++) { using var scope = fixture.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<FileCleanupService>().RunOnceAsync(); }
        Assert.Equal(6, Directory.EnumerateFiles(fixture.StorageRoot).Count());
        prepared = await PrepareDocumentBatch(fixture, admin, source, target, links.ToArray());
        var receipt = await Command(fixture, admin, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy", new { operationId = prepared.GetProperty("operationId").GetString(), values = BatchValues(links, "BATCH-OK") });
        Assert.True(receipt.GetProperty("succeeded").GetBoolean());
        Assert.Equal(links, receipt.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("sourceLinkId").GetString()!));
        Assert.All(receipt.GetProperty("items").EnumerateArray(), row => { Assert.True(row.GetProperty("succeeded").GetBoolean()); Assert.Equal(3, row.EnumerateObject().Count()); });
        Assert.Equal(6, (await Rows(fixture, "Document")).Length); Assert.Equal(12, (await Rows(fixture, "FileUsage")).Length);
        Assert.Equal(3, (await Rows(fixture, "BusinessChange")).Count(item => Equals(item["EventType"], "workspace.object.copied")));
        for (var index = 0; index < links.Count; index++)
        {
            var copy = Assert.Single(await Rows(fixture, "Document"), row => Equals(row["ECODE"], "BATCH-OK-" + index));
            var list = await FeatureHttpTestRequests.ReadAsync(admin, DocFiles.Point(fixture, "document.document.read"), $"/api/document/documents/{copy["EID"]}/files");
            var files = list.GetProperty("files").EnumerateArray().ToArray(); Assert.Equal(2, files.Length);
            for (var file = 0; file < files.Length; file++)
            {
                Assert.Equal($"batch-{index + 1}-{file}.pdf", files[file].GetProperty("fileName").GetString());
                using var content = await FeatureHttpTestRequests.SendAsync(admin, DocFiles.Point(fixture, "document.document.download"), HttpMethod.Get,
                    $"/api/document/documents/{copy["EID"]}/files/{files[file].GetProperty("fileLinkId").GetString()}/content");
                Assert.Equal(HttpStatusCode.OK, content.StatusCode); Assert.Equal(new byte[] { (byte)(index + 1), (byte)file, 97, 98 }, await content.Content.ReadAsByteArrayAsync());
            }
        }
    }

    private static object BatchValues(IReadOnlyList<string> links, string prefix) => new { items = links.Select((link, index) => new { sourceLinkId = link, values = new { ECODE = prefix + "-" + index, ENAME = "集合副本 " + index, Description = "用户值 " + index } }).ToArray() };
    private static Task<JsonElement> PrepareDocumentBatch(BusinessAuditHostFixture fixture, HttpClient client, WorkspacePosition source, WorkspacePosition target, string[] links) =>
        Command(fixture, client, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy/prepare", new { mode = "copy", sourcePosition = source, linkIds = links, targetPosition = target });
    private static async Task<string> CreateBatchDocument(BusinessAuditHostFixture fixture, HttpClient client, WorkspacePosition source, int ordinal)
    {
        var values = new { ECODE = "BATCH-SOURCE-" + ordinal, ENAME = "集合源 " + ordinal, Description = "说明 " + ordinal }; var tokens = new string[2];
        for (var file = 0; file < tokens.Length; file++) tokens[file] = (await DocFiles.StageAsync(fixture, client, source, values, [(byte)ordinal, (byte)file, 97, 98], $"batch-{ordinal}-{file}.pdf")).GetProperty("uploadId").GetString()!;
        await Command(fixture, client, "workspace.folder.createDocument", "/api/workspace/content/documents", new { position = source, values, uploads = new { mainUploadId = tokens[0], attachmentUploadIds = tokens[1..] } });
        var document = Assert.Single(await Rows(fixture, "Document"), row => Equals(row["ECODE"], values.ECODE));
        return (string)Assert.Single(await Rows(fixture, "WorkspaceFolderContent"), row => Equals(row["CEID"], document["EID"]))["LID"]!;
    }
    private sealed class BatchDocumentWriteFault : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public int Attempts { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (Enabled && command.CommandText.Contains("INSERT INTO", StringComparison.Ordinal) && command.CommandText.Contains("[Document]", StringComparison.Ordinal) && ++Attempts == 3) throw new InvalidOperationException("BATCH_LATER_DOCUMENT_FAULT");
            return ValueTask.FromResult(result);
        }
    }
}