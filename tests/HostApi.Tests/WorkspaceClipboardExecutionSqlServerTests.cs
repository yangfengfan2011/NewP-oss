using System.Data.Common;
using System.Net;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式剪贴板入口缺声明或借用其他执行声明不能回退到旧判权")]
    public async Task ClipboardExecution_RejectsMissingAndBorrowedDeclarationsWithoutWrites()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "声明来源");
        var child = await Folder(fixture, admin, source, "声明目标");
        var before = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
        var auditBefore = Snapshot(await Rows(fixture, "BusinessChange"));
        var feature = Point(fixture, "workspace.folder.copy");
        var body = new { mode = "copy", sourcePosition = source, linkIds = new[] { child.LinkIds[^1] } };
        using (var missing = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post,
            "/api/workspace/content/clipboard/validate", body))
            await Problem(missing, HttpStatusCode.BadRequest, "ActionDataAccess.DeclarationInvalid");
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var cut = Point(fixture, "workspace.folder.cut");
        var borrowed = plan.ActionDataAccessCatalog.Single(item => item.Execution == "workspace.clipboard.cut" && item.FeaturePositions.Contains(cut.Position));
        using (var wrongExecution = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post,
            "/api/workspace/content/clipboard/validate", body, borrowed))
            await Problem(wrongExecution, HttpStatusCode.BadRequest, "ActionDataAccess.DeclarationInvalid");
        await Command(fixture, admin, "workspace.folder.copy", "/api/workspace/content/clipboard/validate", body);
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        Assert.Equal(auditBefore, Snapshot(await Rows(fixture, "BusinessChange")));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "异构选择逐项保留read动作根约束顺序，不以首个对象代表整批")]
    public async Task ClipboardExecution_PreservesOrderedFailuresAcrossEveryHeterogeneousTarget()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "有序来源");
        var folder = await Folder(fixture, admin, source, "首个可复制");
        var target = await Folder(fixture, admin, root, "只读落点");
        await Command(fixture, admin, "workspace.folder.createPart", "/api/workspace/content/parts",
            new { position = source, values = new { ECODE = "ORDERED-PART", ENAME = "第二项" } });
        await Command(fixture, admin, "workspace.folder.createDocument", "/api/workspace/content/documents",
            new { position = source, values = new { ECODE = "ORDERED-DOC", ENAME = "第三项" } });
        var rows = await Rows(fixture, "WorkspaceFolderContent");
        var part = Assert.Single(rows, row => Equals(row["ChildModelKey"], "Part"));
        var document = Assert.Single(rows, row => Equals(row["ChildModelKey"], "Document"));
        var selection = new[] { folder.LinkIds[^1], (string)part["LID"]!, (string)document["LID"]! };
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ClipboardOrdered", "有序权限", "clipboard-ordered", "有序权限账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.copy"), Point(fixture, "workspace.folder.paste"));
        using var ordinary = actor.Client;
        await Rule(fixture, admin, "OrderedPartReference", "part.part", (string)part["CEID"]!, "partCopy", "deny");
        await Rule(fixture, admin, "OrderedPartObject", "part.part", (string)part["CEID"]!, "partObjectCopy", "deny");
        await Rule(fixture, admin, "OrderedDocumentRead", "document.document", (string)document["CEID"]!, "read", "deny");
        await Rule(fixture, admin, "OrderedTargetEdit", "workspace.folder", FolderId(target, rows), "edit", "deny");
        var before = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
        await Rejected("workspace.folder.copy", "clipboard/validate", new { mode = "copy", sourcePosition = source, linkIds = selection },
            HttpStatusCode.Forbidden, "Workspace.ObjectCopyDenied");
        await Rejected("workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = source, linkIds = selection, targetPosition = target },
            HttpStatusCode.Forbidden, "Workspace.ObjectCopyDenied");
        await Rule(fixture, admin, "OrderedPartObject", "part.part", (string)part["CEID"]!, "partObjectCopy", "allow");
        await Rejected("workspace.folder.copy", "clipboard/validate", new { mode = "copy", sourcePosition = source, linkIds = selection },
            HttpStatusCode.NotFound, "Workspace.TargetUnavailable");
        await Rule(fixture, admin, "OrderedDocumentRead", "document.document", (string)document["CEID"]!, "read", "allow");
        await Command(fixture, ordinary, "workspace.folder.copy", "/api/workspace/content/clipboard/validate",
            new { mode = "copy", sourcePosition = source, linkIds = selection });
        // OR 真副本资格只允许选择；正式引用提交仍拒绝第二项，而非改用第三项或目标父错误。
        await Rejected("workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = source, linkIds = selection, targetPosition = target },
            HttpStatusCode.Forbidden, "Workspace.ObjectCopyDenied");
        await Rule(fixture, admin, "OrderedPartReference", "part.part", (string)part["CEID"]!, "partCopy", "allow");
        await Rejected("workspace.folder.paste", "paste", new { mode = "copy", sourcePosition = source, linkIds = selection, targetPosition = target },
            HttpStatusCode.Forbidden, "Workspace.ParentWriteDenied");

        async Task Rejected(string behavior, string path, object body, HttpStatusCode status, string code)
        {
            // 配置写入本身有正式审计；只比较本次被拒绝命令前后的完整事实，不把规则变更归给它。
            var auditBefore = Snapshot(await Rows(fixture, "BusinessChange"));
            using var response = await Send(fixture, ordinary, behavior, path, body);
            await Problem(response, status, code);
            Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
            Assert.Equal(auditBefore, Snapshot(await Rows(fixture, "BusinessChange")));
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "预检允许后在正式图锁取得时撤销复制资格，锁内按当前事实拒绝且原子无写入")]
    public async Task ClipboardExecution_RechecksPermissionAfterTheFormalGraphLock()
    {
        var control = new ClipboardExecutionGraphPause();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>(
            (_, options) => options.AddInterceptors(control)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "锁内来源");
        var target = await Folder(fixture, admin, root, "锁内目标");
        var child = await Folder(fixture, admin, source, "锁内复制对象");
        var rows = await Rows(fixture, "WorkspaceFolderContent");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ClipboardLock", "锁内权限", "clipboard-lock", "锁内权限账号",
            Point(fixture, "workspace.spaces.access"), Point(fixture, "workspace.folder.copy"), Point(fixture, "workspace.folder.paste"));
        using var ordinary = actor.Client;
        var selection = new[] { child.LinkIds[^1] };
        await Command(fixture, ordinary, "workspace.folder.copy", "/api/workspace/content/clipboard/validate",
            new { mode = "copy", sourcePosition = source, linkIds = selection });
        var before = Snapshot(await Rows(fixture, "WorkspaceFolderContent"));
        var auditBefore = Snapshot(await Rows(fixture, "BusinessChange"));
        control.Enabled = true;
        var pending = Send(fixture, ordinary, "workspace.folder.paste", "paste",
            new { mode = "copy", sourcePosition = source, linkIds = selection, targetPosition = target });
        try
        {
            await control.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Rule(fixture, admin, "CopyRevokedUnderLock", "workspace.folder", FolderId(child, rows), "workspaceCopy", "deny");
            auditBefore = Snapshot(await Rows(fixture, "BusinessChange"));
        }
        finally { control.Resume.TrySetResult(); }
        using var denied = await pending;
        await Problem(denied, HttpStatusCode.Forbidden, "Workspace.ObjectCopyDenied");
        Assert.Equal(before, Snapshot(await Rows(fixture, "WorkspaceFolderContent")));
        Assert.Equal(auditBefore, Snapshot(await Rows(fixture, "BusinessChange")));
    }

    private sealed class ClipboardExecutionGraphPause : DbCommandInterceptor
    {
        internal bool Enabled;
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("Workspace.FolderGraph", StringComparison.Ordinal))
            {
                Reached.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
