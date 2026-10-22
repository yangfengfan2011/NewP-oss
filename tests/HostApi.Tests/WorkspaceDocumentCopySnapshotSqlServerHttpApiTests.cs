using System.Net;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "current正规身份与传输中文件集合变化要求重新确认，损坏回链不猜其他版次")]
    public async Task ObjectCopy_DocumentCurrentIdentityAndFileCollectionCannotMixAcrossPreparation()
    {
        var io = new CopyIo(); await using var fixture = await CopyIoFixture(io);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "当前快照来源"); var target = await Folder(fixture, admin, root, "当前快照目标");
        var row = await CreateCopyDocument(fixture, admin, source, "DOC-CURRENT");
        var operation = await PrepareDocumentCopy(fixture, admin, source, target, row.LinkId);
        var ids = fixture.Services.GetRequiredService<IPlatformObjectIdGenerator>();
        var versionId = ids.NewId("DocumentVersion"); var iterationId = ids.NewId("DocumentIteration");
        var oldVersion = Assert.Single(await Rows(fixture, "DocumentVersion")); var oldIteration = Assert.Single(await Rows(fixture, "DocumentIteration"));
        // 当前产品没有修订入口；用真实owner模型在一个SQL事务安装合法后继current事实，专测消费方不能沿用旧确认。
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>(); await using var transaction = await db.Database.BeginTransactionAsync();
            var version = new Dictionary<string, object?>(oldVersion) { ["EID"] = versionId, ["VersionOrdinal"] = 2L, ["VersionLabel"] = "2", ["CurrentIterationId"] = iterationId };
            var iteration = new Dictionary<string, object?>(oldIteration) { ["EID"] = iterationId, ["VersionId"] = versionId };
            db.Set<Dictionary<string, object?>>("DocumentVersion").Add(version); db.Set<Dictionary<string, object?>>("DocumentIteration").Add(iteration); await db.SaveChangesAsync();
            await db.Set<Dictionary<string, object?>>("DocumentFileLink").Where(item => EF.Property<string>(item, "PEID") == (string)oldIteration["EID"]!)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => EF.Property<string>(item, "PEID"), iterationId));
            await db.Set<Dictionary<string, object?>>("Document").Where(item => EF.Property<string>(item, "EID") == row.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => EF.Property<string>(item, "LatestVersionId"), versionId));
            await transaction.CommitAsync();
        }
        using (var changed = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = operation, values = new { ECODE = "OLD-CURRENT", ENAME = "不得旧current" } }))
            await Problem(changed, HttpStatusCode.Conflict, "Workspace.CopySourceChanged");
        Assert.Empty(await Rows(fixture, "FileCopyLease"));
        operation = await PrepareDocumentCopy(fixture, admin, source, target, row.LinkId);
        io.Enabled = true; io.PauseAtWrite = 1;
        var pending = Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = operation, values = new { ECODE = "CHANGED-COLLECTION", ENAME = "不得混合集合" } });
        try
        {
            await io.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            using (var scope = fixture.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>(); await using var transaction = await db.Database.BeginTransactionAsync();
                var attachments = db.Set<Dictionary<string, object?>>("DocumentFileLink").Where(item => EF.Property<string>(item, "PEID") == iterationId && EF.Property<string>(item, "Role") == "attachment");
                await attachments.ExecuteUpdateAsync(setters => setters.SetProperty(item => EF.Property<long>(item, "Ordinal"), item => -EF.Property<long>(item, "Ordinal")));
                await attachments.ExecuteUpdateAsync(setters => setters.SetProperty(item => EF.Property<long>(item, "Ordinal"), item => 3 + EF.Property<long>(item, "Ordinal")));
                await transaction.CommitAsync();
            }
            io.Release.TrySetResult(); using var changed = await pending;
            await Problem(changed, HttpStatusCode.Conflict, "Workspace.CopySourceChanged");
        }
        finally { io.Release.TrySetResult(); (await pending).Dispose(); }
        await CleanDocumentCopies(fixture); Assert.Equal(3, Directory.EnumerateFiles(fixture.StorageRoot).Count());
        operation = await PrepareDocumentCopy(fixture, admin, source, target, row.LinkId);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            await db.Set<Dictionary<string, object?>>("DocumentVersion").Where(item => EF.Property<string>(item, "EID") == versionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => EF.Property<string>(item, "CurrentIterationId"), (string)oldIteration["EID"]!));
        }
        using (var corrupted = await Send(fixture, admin, "workspace.folder.pasteObjectCopy", "objects/copy", new { operationId = operation, values = new { ECODE = "CORRUPTED-CURRENT", ENAME = "不得猜测" } }))
            Assert.Equal(HttpStatusCode.InternalServerError, corrupted.StatusCode);
        Assert.Single(await Rows(fixture, "Document")); Assert.Equal(3, (await Rows(fixture, "FileUsage")).Length);
    }
}
