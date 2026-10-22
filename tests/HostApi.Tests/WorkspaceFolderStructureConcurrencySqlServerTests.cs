using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "同名结构复制只提交一个完整结果，自身目标不把新增节点再纳入本次复制")]
    public async Task StructureCopy_SerializesSameNameAndFreezesSourceBeforeWritingToItself()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var source = await Folder(fixture, admin, root, "并发来源"); await Folder(fixture, admin, source, "后代");
        var original = await Rows(fixture, "WorkspaceFolder");
        var input = new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = source, name = "自身内新结构" };
        await Command(fixture, admin, "workspace.folder.pasteFolderStructure", "/api/workspace/content/folders/copy-structure", input);
        Assert.Equal(original.Length + 2, (await Rows(fixture, "WorkspaceFolder")).Length);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Send(fixture, admin, "workspace.folder.pasteFolderStructure", "folders/copy-structure", new { input.mode, input.sourcePosition, input.linkIds, targetPosition = root, name = "同名并发" })));
        try { Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK); Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.Single(await Rows(fixture, "WorkspaceFolder"), row => Equals(row["ENAME"], "同名并发"));
        Assert.Equal(original.Length + 6, (await Rows(fixture, "WorkspaceFolder")).Length);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实源图移除及排序事务占锁时，结构复制等待并使用提交后的完整新图")]
    public async Task StructureCopy_ReplansAfterConcurrentRemovalAndReorder()
    {
        foreach (var reorder in new[] { false, true })
        {
            var control = new PasteCommitControl(); var graph = new FolderCopyLockClock();
            await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(control, graph)), includeWorkspace: true);
            using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin); var source = await Folder(fixture, admin, root, "源图");
            var a = await Folder(fixture, admin, source, "A"); var b = await Folder(fixture, admin, source, "B");
            control.Pause = true;
            async Task ChangeSource()
            {
                if (!reorder) { await Command(fixture, admin, "workspace.folder.removeSelected", "/api/workspace/content/references/remove", new { position = source, linkIds = new[] { a.LinkIds[^1] } }); return; }
                var point = Assert.Single(fixture.FeatureCatalog, item => item.BehaviorKey == "workspace.folder.reorder" && item.Key == "moveUp" && item.ActionScope == "list.custom");
                using var response = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Post, "/api/workspace/content/tree-order", new { parentPosition = new { spaceId = source.SpaceId, rootFolderId = source.RootFolderId, linkIds = source.LinkIds }, linkId = b.LinkIds[^1], direction = "up" });
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            var changing = ChangeSource(); Task<JsonElement>? copying = null;
            try
            {
                await control.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20)); graph.Begin();
                copying = Command(fixture, admin, "workspace.folder.pasteFolderStructure", "/api/workspace/content/folders/copy-structure", new { mode = "copy", sourcePosition = root, linkIds = new[] { source.LinkIds[^1] }, targetPosition = root, name = "重取结构" });
                await graph.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(20)); control.Pause = false; control.Resume.TrySetResult(); await changing; await copying;
            }
            finally { control.Pause = false; control.Resume.TrySetResult(); await changing; if (copying is not null) await copying; }
            var folders = await Rows(fixture, "WorkspaceFolder"); var clone = Assert.Single(folders, row => Equals(row["ENAME"], "重取结构"));
            var children = (await Rows(fixture, "WorkspaceFolderContent")).Where(row => Equals(row["PEID"], clone["EID"])).OrderBy(row => row["TreeOrdinal"])
                .Select(row => (string)folders.Single(folder => Equals(folder["EID"], row["CEID"]))["ENAME"]!).ToArray();
            Assert.Equal(reorder ? ["B", "A"] : new[] { "B" }, children);
        }
    }
}
