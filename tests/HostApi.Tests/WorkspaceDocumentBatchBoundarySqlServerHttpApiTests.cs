using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "后文档第三文件真实写入故障取消前文档全部prepared，物理回收保留四个来源文件")]
    public async Task ObjectCopy_DocumentBatchLaterIoFailureCancelsAndReclaimsEarlierPreparations()
    {
        var io = new CopyIo(); await using var fixture = await CopyIoFixture(io);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "后项IO故障来源"); var target = await Folder(fixture, admin, root, "后项IO故障目标");
        string[] links = [await CreateBatchDocument(fixture, admin, source, 1), await CreateBatchDocument(fixture, admin, source, 2)];
        var sourceKeys = Directory.EnumerateFiles(fixture.StorageRoot).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prepared = await PrepareDocumentBatch(fixture, admin, source, target, links); var operationId = prepared.GetProperty("operationId").GetString();
        io.Arm("write", 3);
        using var failed = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId, values = BatchValues(links, "LATER-IO") });
        Assert.False(failed.IsSuccessStatusCode); Assert.Equal(1, io.Injected); Assert.Equal(3, io.Writes);
        var receipt = await Command(fixture, admin, "workspace.folder.resolveObjectCopy", "/api/workspace/content/objects/copy/result", new { operationId });
        Assert.Equal("cancelled", receipt.GetProperty("state").GetString()); Assert.Equal(2, receipt.GetProperty("items").GetArrayLength());
        Assert.All(receipt.GetProperty("items").EnumerateArray(), row => Assert.False(row.GetProperty("succeeded").GetBoolean()));
        Assert.Equal(2, (await Rows(fixture, "Document")).Length); Assert.Equal(4, (await Rows(fixture, "DocumentFileLink")).Length);
        Assert.Equal(4, (await Rows(fixture, "FileUsage")).Count(row => Equals(row["State"], "active")));
        Assert.DoesNotContain(await Rows(fixture, "FileCopyLease"), row => Equals(row["State"], "prepared") || Equals(row["State"], "confirmed"));
        Assert.DoesNotContain(await Rows(fixture, "BusinessChange"), row => Equals(row["EventType"], "workspace.object.copied"));
        await CleanDocumentCopies(fixture); Assert.True(sourceKeys.SetEquals(Directory.EnumerateFiles(fixture.StorageRoot)));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实多Part/混合/文件夹/cut/根目标不获得集合资格，旧混合引用仍保留原对象")]
    public async Task ObjectCopy_DocumentBatchRejectsValidMixedSelectionsAndPreservesOldReferences()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "真实混合来源"); var target = await Folder(fixture, admin, root, "真实混合目标");
        for (var index = 0; index < 2; index++) await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts",
            new { position = source, values = new { ECODE = "MIXED-PART-" + index, ENAME = "混合零组件 " + index } });
        await Command(fixture, admin, "workspace.folder.createDocument", "/api/workspace/content/documents",
            new { position = source, values = new { ECODE = "MIXED-DOCUMENT", ENAME = "混合图文档" } });
        var child = await Folder(fixture, admin, source, "混合文件夹");
        var all = await Rows(fixture, "WorkspaceFolderContent");
        var parts = all.Where(row => Equals(row["ChildModelKey"], "Part")).Select(row => (string)row["LID"]!).ToArray();
        var document = (string)Assert.Single(all, row => Equals(row["ChildModelKey"], "Document"))["LID"]!;
        foreach (var selection in new[] { parts, new[] { parts[0], document }, new[] { document, child.LinkIds[^1] } })
        {
            using var rejected = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy/prepare",
                new { mode = "copy", sourcePosition = source, linkIds = selection, targetPosition = target });
            await Problem(rejected, HttpStatusCode.BadRequest, "Workspace.ObjectCopySourceRequired");
        }
        foreach (var invalid in new object[] {
            new { mode = "cut", sourcePosition = source, linkIds = new[] { document }, targetPosition = target },
            new { mode = "copy", sourcePosition = source, linkIds = new[] { document }, targetPosition = root } })
        {
            using var rejected = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy/prepare", invalid);
            Assert.InRange((int)rejected.StatusCode, 400, 499);
        }
        Assert.Empty(await Rows(fixture, "WorkspaceObjectCopy")); Assert.Empty(await Rows(fixture, "FileCopyLease"));
        string[] mixed = [parts[0], document, child.LinkIds[^1]];
        await Command(fixture, admin, "workspace.folder.copy", "/api/workspace/content/clipboard/validate", new { mode = "copy", sourcePosition = source, linkIds = mixed });
        await Command(fixture, admin, "workspace.folder.paste", "/api/workspace/content/paste", new { mode = "copy", sourcePosition = source, linkIds = mixed, targetPosition = target });
        Assert.Equal(2, (await Rows(fixture, "Part")).Length); Assert.Single(await Rows(fixture, "Document"));
        var after = await Rows(fixture, "WorkspaceFolderContent");
        foreach (var link in mixed) Assert.Equal(2, after.Count(row => Equals(row["CEID"], Assert.Single(all, original => Equals(original["LID"], link))["CEID"])));
        Assert.DoesNotContain(await Rows(fixture, "BusinessChange"), row => Equals(row["EventType"], "workspace.object.copied"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "批内重复编码定位后行并回滚前行，纠正值复用准备；LID错序/少行/跨操作载荷不能调包")]
    public async Task ObjectCopy_DocumentBatchCodeRowsAndMappingsRemainCorrectable()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "行编码来源"); var target = await Folder(fixture, admin, root, "行编码目标");
        string[] links = [await CreateBatchDocument(fixture, admin, source, 1), await CreateBatchDocument(fixture, admin, source, 2)];
        var prepared = await PrepareDocumentBatch(fixture, admin, source, target, links); var id = prepared.GetProperty("operationId").GetString();
        var duplicate = new { items = links.Select(link => new { sourceLinkId = link, values = new { ECODE = "BATCH-DUPLICATE", ENAME = "同码行" } }).ToArray() };
        using (var response = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = id, values = duplicate }))
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(error.GetProperty("fieldErrors").EnumerateArray(), field => field.GetProperty("field").GetString() == $"items.{links[1]}.ECODE");
        }
        Assert.Equal(2, (await Rows(fixture, "Document")).Length); Assert.Equal(4, (await Rows(fixture, "FileUsage")).Length);
        var files = (await Rows(fixture, "FileRecord")).Length;
        foreach (var invalid in new[] { BatchValues(links.Reverse().ToArray(), "SWAPPED"), BatchValues(links[..1], "MISSING"), BatchValues([links[0], "foreign-source-lid"], "FOREIGN") })
        {
            using var response = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = id, values = invalid });
            await Problem(response, HttpStatusCode.BadRequest, "Workspace.InvalidClipboardInput");
        }
        Assert.Equal(files, (await Rows(fixture, "FileRecord")).Length);
        var corrected = await Command(fixture, admin, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy", new { operationId = id, values = BatchValues(links, "CORRECTED") });
        Assert.True(corrected.GetProperty("succeeded").GetBoolean()); Assert.Equal(4, (await Rows(fixture, "Document")).Length);
        Assert.Equal(files, (await Rows(fixture, "FileRecord")).Length); Assert.Equal(8, (await Rows(fixture, "FileUsage")).Length);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "后文档准备期间正常创建赢得编码竞争，整个集合回滚且并发重码精确定位原来源行")]
    public async Task ObjectCopy_DocumentBatchConcurrentCodeWinnerDoesNotCommitEarlierRows()
    {
        var io = new CopyIo(); await using var fixture = await CopyIoFixture(io);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "并发编码源"); var target = await Folder(fixture, admin, root, "并发编码目标");
        string[] links = [await CreateBatchDocument(fixture, admin, source, 1), await CreateBatchDocument(fixture, admin, source, 2)];
        var prepared = await PrepareDocumentBatch(fixture, admin, source, target, links); var id = prepared.GetProperty("operationId").GetString();
        io.Enabled = true; io.PauseAtWrite = 3;
        var pending = Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = id, values = BatchValues(links, "CONCURRENT") });
        try
        {
            await io.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var point = Assert.Single(fixture.FeatureCatalog, item => item.ModuleId == "Document" && item.BehaviorKey == "document.document.create" && item.ActionScope == "list.pageActions");
            using var winner = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Post, "/api/document/documents", new { ECODE = "CONCURRENT-1", ENAME = "普通创建赢家" }, OwnedResourceReadHttpRequests.Declaration(fixture, point, "document.document", "create"));
            Assert.Equal(HttpStatusCode.Created, winner.StatusCode); io.Release.TrySetResult();
            using var response = await pending; Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(error.GetProperty("fieldErrors").EnumerateArray(), field => field.GetProperty("field").GetString() == $"items.{links[1]}.ECODE");
        }
        finally { io.Release.TrySetResult(); (await pending).Dispose(); }
        Assert.Equal(3, (await Rows(fixture, "Document")).Length); Assert.Equal(4, (await Rows(fixture, "FileUsage")).Length);
        Assert.DoesNotContain(await Rows(fixture, "BusinessChange"), row => Equals(row["EventType"], "workspace.object.copied"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "前文档已准备时后文档源字段变化拒绝整批；整批取消收敛全部行并停止迟到写入")]
    public async Task ObjectCopy_DocumentBatchChangedSourceAndCancellationCannotLeaveEarlierPreparations()
    {
        foreach (var cancel in new[] { false, true })
        {
            var io = new CopyIo(); await using var fixture = await CopyIoFixture(io);
            using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
            var source = await Folder(fixture, admin, root, "准备竞争源"); var target = await Folder(fixture, admin, root, "准备竞争目标");
            string[] links = [await CreateBatchDocument(fixture, admin, source, 1), await CreateBatchDocument(fixture, admin, source, 2)];
            var prepared = await PrepareDocumentBatch(fixture, admin, source, target, links); var id = prepared.GetProperty("operationId").GetString();
            io.Enabled = true; io.PauseAtWrite = 3;
            var pending = Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = id, values = BatchValues(links, "PREPARING") });
            try
            {
                await io.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
                if (cancel)
                {
                    var receipt = await Command(fixture, admin, "workspace.folder.pasteObjectCopy", "/api/workspace/content/objects/copy/cancel", new { operationId = id });
                    Assert.Equal("cancelled", receipt.GetProperty("state").GetString()); Assert.Equal(2, receipt.GetProperty("items").GetArrayLength());
                }
                else
                {
                    using var edit = await FeatureHttpTestRequests.SendAsync(admin, RowPoint(fixture, "edit"), HttpMethod.Put, "/api/workspace/content/" + links[1],
                        new { position = source, values = new { ENAME = "第二行源发生变化" } }).WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
                }
                var writes = io.Writes; io.Release.TrySetResult(); using var response = await pending;
                await Problem(response, HttpStatusCode.Conflict, cancel ? "Workspace.CopyCancelled" : "Workspace.CopySourceChanged");
                if (cancel) Assert.Equal(writes, io.Writes);
            }
            finally { io.Release.TrySetResult(); (await pending).Dispose(); }
            Assert.Equal(2, (await Rows(fixture, "Document")).Length); Assert.Equal(4, (await Rows(fixture, "FileUsage")).Length);
            await CleanDocumentCopies(fixture); Assert.Equal(4, Directory.EnumerateFiles(fixture.StorageRoot).Count());
            Assert.DoesNotContain(await Rows(fixture, "FileCopyLease"), row => Equals(row["State"], "prepared") || Equals(row["State"], "confirmed"));
        }
    }
}