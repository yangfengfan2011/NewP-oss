using System.Net;
using ICP.Platform.Modules.Files.Services;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "首/中/末文件真实read/write/complete故障均拒绝整文档，所有独立内容有持久归属并实际回收")]
    public async Task ObjectCopy_DocumentFirstMiddleLastIoFailuresReclaimAllPreparations()
    {
        var io = new CopyIo(); await using var fixture = await CopyIoFixture(io);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "文件故障源"); var target = await Folder(fixture, admin, root, "文件故障目标");
        var row = await CreateCopyDocument(fixture, admin, source, "DOC-IO-FAULT");
        foreach (var phase in new[] { "read", "write", "complete" })
            for (var index = 1; index <= 3; index++)
            {
                var operation = await PrepareDocumentCopy(fixture, admin, source, target, row.LinkId);
                io.Arm(phase, index);
                using var response = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = operation,
                    values = new { ECODE = "DOC-FAULT-" + phase + index, ENAME = "不得部分成功" } });
                Assert.False(response.IsSuccessStatusCode); Assert.Equal(1, io.Injected);
                Assert.Single(await Rows(fixture, "Document")); Assert.Equal(3, (await Rows(fixture, "FileUsage")).Length);
                Assert.DoesNotContain(await Rows(fixture, "FileCopyLease"), item => Equals(item["State"], "prepared") || Equals(item["State"], "confirmed"));
                for (var attempt = 0; attempt < 2; attempt++) { using var scope = fixture.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<FileCleanupService>().RunOnceAsync(); }
                Assert.Equal(3, Directory.EnumerateFiles(fixture.StorageRoot).Count());
                Assert.DoesNotContain(await Rows(fixture, "BusinessChange"), item => Equals(item["EventType"], "workspace.object.copied"));
            }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Document/版次/FileLink/usage/目录Link/审计逐点SQL失败整体回滚，新物理内容确定回收，源图完整")]
    public async Task ObjectCopy_DocumentBusinessGraphWriteFailuresRollBackEveryOwnedFact()
    {
        var fault = new ObjectCopyWriteFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(fault)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "业务故障源"); var target = await Folder(fixture, admin, root, "业务故障目标");
        var row = await CreateCopyDocument(fixture, admin, source, "DOC-GRAPH-FAULT");
        var expected = new Dictionary<string, int> { ["Document"] = 1, ["DocumentVersion"] = 1, ["DocumentIteration"] = 1,
            ["DocumentFileLink"] = 3, ["FileUsage"] = 3, ["WorkspaceFolderContent"] = 3 };
        foreach (var table in new[] { "Document", "DocumentVersion", "DocumentIteration", "DocumentFileLink", "FileUsage", "WorkspaceFolderContent", "BusinessChange" })
        {
            var operation = await PrepareDocumentCopy(fixture, admin, source, target, row.LinkId);
            fault.Table = table; fault.Triggered = false;
            using var response = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = operation,
                values = new { ECODE = "DOC-WRITE-" + table, ENAME = "不得半图" } });
            fault.Table = null;
            Assert.False(response.IsSuccessStatusCode); Assert.True(fault.Triggered);
            foreach (var (model, count) in expected) Assert.Equal(count, (await Rows(fixture, model)).Length);
            Assert.DoesNotContain(await Rows(fixture, "BusinessChange"), item => Equals(item["EventType"], "workspace.object.copied"));
            for (var attempt = 0; attempt < 2; attempt++) { using var scope = fixture.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<FileCleanupService>().RunOnceAsync(); }
            Assert.Equal(3, Directory.EnumerateFiles(fixture.StorageRoot).Count());
        }
    }
}
