using System.Security.Cryptography;
using ICP.Platform.Acceptance;
using ICP.Platform.Testing;
using Microsoft.Playwright;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

/// <summary>
/// 模块闸门就是普通集成测试：失败直接是异常与栈，不再经过 event/verdict/failureCode
/// 那层无人消费的举证协议。与仓库既有的 SQL Server 集成测试一致，通过环境变量显式
/// 启用，未启用时报为跳过，因此日常 <c>dotnet test</c> 可以安全包含它们。
/// </summary>
[Collection(AcceptanceGateCollection.Name)]
public sealed class AcceptanceGateTests
{
    private const string RunEnvironmentVariable = "NEWP_RUN_ACCEPTANCE";

    [EnvironmentGatedFact(RunEnvironmentVariable, "一次性 SQL Server catalog、dev supervisor 与真实浏览器")]
    public async Task ExampleRequiredField_CompletesEveryScenario()
    {
        var (root, runId) = PrepareRun("example-required-field");
        await new ExampleRequiredFieldAcceptanceRunner(RepositoryRoot, root, runId).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "一次性 SQL Server catalog、dev supervisor 与真实浏览器")]
    public async Task IdentityFoundation_CompletesEveryScenario()
    {
        var (root, runId) = PrepareRun("identity-foundation");
        await new IdentityFoundationAcceptanceRunner(RepositoryRoot, root, runId).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "共享专用 SQL Server catalog、dev supervisor 与真实浏览器")]
    public async Task Personnel_CompletesEveryScenario()
    {
        var (root, runId) = PrepareRun("personnel");
        await new PersonnelAcceptanceRunner(RepositoryRoot, root, runId).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "功能授权真实启动、角色入口跳转与整组保存")]
    public async Task Authorization_CompletesEveryScenario()
    {
        var (root, runId) = PrepareRun("authorization");
        await new AuthorizationAcceptanceRunner(RepositoryRoot, root, runId).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "工作空间双根入口、普通账号与同库真实 Host 进程重启")]
    public async Task Workspace_CompletesEveryScenario()
    {
        var (root, runId) = PrepareRun("workspace");
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId,
            publishedFixture: PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId, exerciseFileRecovery: true, exerciseFileRecoveryMechanisms: true)).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "三成员固定发行物与旧 Workspace/Part/Document 行、Link、历史连续性")]
    public async Task NativeWorkspaceApi_UsesFixedRelease()
    {
        var (root, runId) = PrepareRun("native-workspace-api");
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false,
            publishedFixture: PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId),
            includeBrowser: false).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "显式四组件卸载及同一固定 Host 恢复旧三成员行、Link和历史")]
    public async Task NativeWorkspaceUninstall_RecoversExistingBusiness()
    {
        var (root, runId) = PrepareRun("native-workspace-uninstall");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, exerciseUninstallRecovery: true);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false,
            publishedFixture: fixture, includeBrowser: false).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "正式 Host 在同 SQL/root 重启后保留文件使用并恢复 receiving/staged/pending")]
    public async Task NativeWorkspaceFiles_RestartsAndRecoversRegisteredContent()
    {
        var (root, runId) = PrepareRun("native-workspace-files");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, exerciseFileRecovery: true, exerciseFileRecoveryMechanisms: true);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false,
            publishedFixture: fixture, includeBrowser: false).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "正式 Host 同库同 root 重启保留主文件与附件集合身份及逐文件下载")]
    public async Task NativeWorkspaceFileCollection_RestartsWithSavedIdentityAndBytes()
    {
        var (root, runId) = PrepareRun("native-workspace-file-collection");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, exerciseFileRecovery: true, exerciseFileRecoveryMechanisms: false);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false,
            publishedFixture: fixture, includeBrowser: false).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "三成员包通过正式 Workspace driver 打开旧内容、属性并创建一次对象")]
    public async Task NativeWorkspace_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId, exerciseCompositionLifecycle: false);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                Task CaptureAsync(string name) => page.ScreenshotAsync(new()
                { Path = Path.Combine(root, name), FullPage = true, Animations = ScreenshotAnimations.Disabled });
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                await new StandardCrudPageDriver(page).WaitForRowAsync(fixture.ExistingFolderName);
                await CaptureAsync("workspace-content.png");
                var properties = await driver.OpenFolderPropertiesAsync(fixture.ExistingFolderName);
                await CaptureAsync("workspace-properties.png");
                await page.SetViewportSizeAsync(900, 1000);
                await CaptureAsync("workspace-properties-narrow.png");
                await driver.ClosePropertiesAsync(properties);
                await page.SetViewportSizeAsync(1440, 1000);
                var folderAddress = await driver.SelectTreePathAsync("公共空间", fixture.ExistingFolderName);
                var attachments = WorkspacePageDriver.SmallAttachmentFiles();
                await driver.CreateDocumentWithFilesAsync("ACC-WS-D-" + runId[..8], "归组图文档旅程 " + runId[..8], WorkspacePageDriver.SmallMainFile(), async _ =>
                {
                    await CaptureAsync("workspace-create.png");
                    await page.SetViewportSizeAsync(900, 1000);
                    await CaptureAsync("workspace-create-narrow.png");
                    await page.SetViewportSizeAsync(1440, 1000);
                }, attachments: attachments);
                await CaptureAsync("workspace-created-content.png");
                var documentName = "归组图文档旅程 " + runId[..8];
                await driver.OpenOwnedPropertiesAsync(documentName, "/documents");
                foreach (var file in new[] { WorkspacePageDriver.SmallMainFile() }.Concat(attachments))
                    await driver.DownloadFileAsync(file.Name, Path.Combine(root, "download-" + file.Name));
                await CaptureAsync("workspace-document-properties.png");
                // 完整详情无遮挡时使用既有 Shell 语言控件，轻量核对集合面板的两种语言布局。
                await driver.SelectLanguageAsync("English");
                await page.GetByRole(AriaRole.Region, new() { Name = "Files", Exact = true })
                    .GetByRole(AriaRole.Button, new() { Name = "Download", Exact = true }).First
                    .WaitForAsync(new() { State = WaitForSelectorState.Visible });
                await CaptureAsync("workspace-document-properties-en.png");
                await page.SetViewportSizeAsync(900, 1000);
                await CaptureAsync("workspace-document-properties-en-narrow.png");
                await page.SetViewportSizeAsync(1440, 1000);
                await driver.SelectLanguageAsync("简体中文");
                await page.GetByRole(AriaRole.Region, new() { Name = "文件", Exact = true })
                    .WaitForAsync(new() { State = WaitForSelectorState.Visible });
                await driver.RestorePositionAsync(folderAddress, "公共空间", fixture.ExistingFolderName);
                await driver.EditObjectAsync(documentName, documentName + " 已编辑");
                await CaptureAsync("workspace-document-edited.png");
                await driver.DeleteSelectedAsync(documentName + " 已编辑");
                await CaptureAsync("workspace-document-deleted.png");
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { url = page.Url, actor = "ordinary", journey = "workspace-published-components" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通账号通过固定发行物的树和列表右键菜单编辑真实文件夹与图文档")]
    public async Task NativeWorkspaceContextMenus_UseFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-context-menus");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId, exerciseCompositionLifecycle: false);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                Task CaptureAsync(string name) => page.ScreenshotAsync(new()
                { Path = Path.Combine(root, name), FullPage = true, Animations = ScreenshotAnimations.Disabled });
                var folder = "菜单文件夹 " + runId[..8];
                var document = "菜单图文档 " + runId[..8];
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                await driver.CreateFolderAsync(folder);
                await driver.ExpandRootToFolderAsync("公共空间", folder);
                await driver.SelectTreePathAsync("公共空间", folder);
                await driver.CreateDocumentWithFilesAsync("MENU-D-" + runId[..8], document, WorkspacePageDriver.SmallMainFile());
                await driver.EditObjectFromContextMenuAsync(document, document + " 已编辑", () => CaptureAsync("list-context-menu.png"));
                await driver.EditFolderFromTreeContextMenuAsync(folder, folder + " 已编辑", () => CaptureAsync("tree-context-menu.png"));
                await CaptureAsync("context-menu-saved.png");
                await page.SetViewportSizeAsync(900, 1000);
                await CaptureAsync("context-menu-saved-narrow.png");
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { url = page.Url, actor = "ordinary", journey = "workspace-context-menus" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通账号在固定发行物中通过正式树菜单单选上下移并看到刷新")]
    public async Task NativeWorkspaceTreeOrder_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-tree-order");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId, exerciseCompositionLifecycle: false);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                Task CaptureAsync(string name) => page.ScreenshotAsync(new()
                { Path = Path.Combine(root, name), FullPage = true, Animations = ScreenshotAnimations.Disabled });
                var previous = "排序相邻文件夹 " + runId[..8];
                var target = "排序目标文件夹 " + runId[..8];
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                await driver.CreateFolderAsync(previous);
                await driver.CreateFolderAsync(target);
                await driver.ExpandRootToFolderAsync("公共空间", target);
                await driver.ReorderFolderAsync(target, "up", () => CaptureAsync("tree-order-menu.png"));
                await driver.ReorderFolderAsync(target, "down");
                await CaptureAsync("tree-order-saved.png");
                await page.SetViewportSizeAsync(900, 1000);
                await CaptureAsync("tree-order-saved-narrow.png");
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { url = page.Url, actor = "ordinary", journey = "workspace-tree-order" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通账号通过正式树/行菜单和当前页工具栏复制并连续粘贴引用")]
    public async Task NativeWorkspaceCopyPaste_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-copy-paste");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId, exerciseCompositionLifecycle: false);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                Task CaptureAsync(string name) => page.ScreenshotAsync(new()
                { Path = Path.Combine(root, name), FullPage = true, Animations = ScreenshotAnimations.Disabled });
                var source = "复制来源 A " + runId[..8];
                var targetB = "粘贴目录 B " + runId[..8];
                var targetC = "粘贴目录 C " + runId[..8];
                var copiedFolder = "树复制文件夹 " + runId[..8];
                var document = "复制图文档 " + runId[..8];
                var part = "工具栏复制零组件 " + runId[..8];
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                foreach (var name in new[] { source, targetB, targetC, copiedFolder }) await driver.CreateFolderAsync(name);
                await driver.ExpandRootToFolderAsync("公共空间", copiedFolder);
                await driver.CopyFolderFromTreeContextMenuAsync(copiedFolder, () => CaptureAsync("clipboard-tree-menu.png"));
                await driver.SelectTreePathAsync("公共空间", targetB);
                await driver.PasteReferencesAsync(copiedFolder);
                await driver.SelectTreePathAsync("公共空间", source);
                await driver.CreateDocumentWithFilesAsync("COPY-D-" + runId[..8], document, WorkspacePageDriver.SmallMainFile(),
                    attachments: WorkspacePageDriver.SmallAttachmentFiles());
                await driver.CreateObjectAsync("零组件", "COPY-P-" + runId[..8], part);
                await driver.CopyReferenceFromContextMenuAsync(document, () => CaptureAsync("clipboard-row-menu.png"));
                await driver.SelectTreePathAsync("公共空间", targetB);
                await driver.PasteReferencesAsync(document);
                await driver.SelectTreePathAsync("公共空间", targetC);
                await driver.PasteReferencesAsync(document);
                await driver.SelectTreePathAsync("公共空间", source);
                await driver.CopySelectedAsync(part);
                await driver.SelectTreePathAsync("公共空间", targetC);
                await driver.PasteReferencesAsync(part);
                await CaptureAsync("clipboard-pasted.png");
                var localizedRead = page.WaitForResponseAsync(response => response.Request.Method == "GET"
                    && new Uri(response.Url).AbsolutePath == "/api/links/workspace.content");
                await driver.SelectLanguageAsync("English");
                var localizedResponse = await localizedRead;
                if (!localizedResponse.Ok) throw new InvalidOperationException($"切换语言后的列表读取失败：{localizedResponse.Status}");
                await localizedResponse.FinishedAsync();
                await page.GetByRole(AriaRole.Button, new() { Name = "Paste references", Exact = true })
                    .WaitForAsync(new() { State = WaitForSelectorState.Visible });
                await new StandardCrudPageDriver(page).WaitForRowAsync(document);
                await new StandardCrudPageDriver(page).WaitForRowAsync(part);
                await Assertions.Expect(page.Locator(".np-tree-content .ant-spin-spinning")).ToHaveCountAsync(0);
                await CaptureAsync("clipboard-pasted-en.png");
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { url = page.Url, actor = "ordinary", journey = "workspace-copy-paste" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通账号通过正式树和工具栏剪切移动并确认批量移除引用")]
    public async Task NativeWorkspaceCutAndRemove_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-cut-remove");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId, exerciseCompositionLifecycle: false);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                Task CaptureAsync(string name) => page.ScreenshotAsync(new()
                { Path = Path.Combine(root, name), FullPage = true, Animations = ScreenshotAnimations.Disabled });
                var folder = "待移动文件夹 " + runId[..8];
                var targetB = "移动目录 B " + runId[..8];
                var targetC = "移动目录 C " + runId[..8];
                var document = "移动图文档 " + runId[..8];
                var part = "移动零组件 " + runId[..8];
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                foreach (var name in new[] { folder, targetB, targetC }) await driver.CreateFolderAsync(name);
                await driver.SelectTreePathAsync("公共空间", folder);
                await driver.CreateDocumentWithFilesAsync("CUT-D-" + runId[..8], document, WorkspacePageDriver.SmallMainFile(),
                    attachments: WorkspacePageDriver.SmallAttachmentFiles());
                await driver.CreateObjectAsync("零组件", "CUT-P-" + runId[..8], part);
                await driver.CutFolderFromTreeContextMenuAsync(folder, () => CaptureAsync("cut-tree-menu.png"));
                await driver.SelectTreePathAsync("公共空间", targetB);
                await CaptureAsync("cut-pending.png");
                await driver.PasteCutReferencesAsync(folder);
                await driver.SelectTreePathAsync("公共空间", targetB, folder);
                await driver.CutSelectedAsync(document, part);
                await driver.SelectTreePathAsync("公共空间", targetC);
                await driver.PasteCutReferencesAsync(document, part);
                await CaptureAsync("cut-pasted.png");
                await driver.RemoveSelectedReferencesAsync([document, part], async dialog =>
                {
                    await dialog.EvaluateAsync("async element => { await new Promise(requestAnimationFrame); await Promise.all(element.getAnimations({subtree:true}).map(animation => animation.finished.catch(() => {}))); }");
                    await CaptureAsync("remove-references-confirm.png");
                });
                var localizedRead = page.WaitForResponseAsync(response => response.Request.Method == "GET"
                    && new Uri(response.Url).AbsolutePath == "/api/links/workspace.content");
                await driver.SelectLanguageAsync("English");
                var localizedResponse = await localizedRead;
                if (!localizedResponse.Ok) throw new InvalidOperationException($"切换语言后的列表读取失败：{localizedResponse.Status}");
                await localizedResponse.FinishedAsync();
                await page.GetByRole(AriaRole.Button, new() { Name = "Remove selected references", Exact = true })
                    .WaitForAsync(new() { State = WaitForSelectorState.Visible });
                await Assertions.Expect(page.Locator(".np-tree-content .ant-spin-spinning")).ToHaveCountAsync(0);
                await CaptureAsync("references-removed-en.png");
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { url = page.Url, actor = "ordinary", journey = "workspace-cut-remove" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通账号搜索全部可读出现路径并使用正式driver定位同名文件夹")]
    public async Task NativeWorkspaceSearch_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-search");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId, exerciseCompositionLifecycle: false);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                Task CaptureAsync(string name) => page.ScreenshotAsync(new()
                { Path = Path.Combine(root, name), FullPage = true, Animations = ScreenshotAnimations.Disabled });
                var target = "搜索目标目录 " + runId[..8];
                var folder = fixture.ExistingFolderName;
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                await driver.CreateFolderAsync(target);
                await driver.SearchAndSelectFolderAsync(folder, ["公共空间", folder]);
                await driver.CopyFolderFromTreeContextMenuAsync(folder, selectedOnly: true);
                await driver.SelectTreePathAsync("公共空间", target);
                await driver.PasteReferencesAsync(folder);
                var address = await driver.SearchAndSelectFolderAsync(folder, ["公共空间", target, folder],
                    () => CaptureAsync("search-distinct-paths.png"));
                await new StandardCrudPageDriver(page).WaitForRowAsync(fixture.ExistingDocumentName);
                await CaptureAsync("search-located-content.png");
                // 两条同名出现位置已展开；正式动作明确使用搜索选中的那条。
                await driver.CopyFolderFromTreeContextMenuAsync(folder, selectedOnly: true);
                var localizedRead = page.WaitForResponseAsync(response => response.Request.Method == "GET"
                    && new Uri(response.Url).AbsolutePath == "/api/links/workspace.content");
                await driver.SelectLanguageAsync("English");
                var localizedResponse = await localizedRead;
                if (!localizedResponse.Ok) throw new InvalidOperationException($"切换语言后的列表读取失败：{localizedResponse.Status}");
                await localizedResponse.FinishedAsync();
                await new StandardCrudPageDriver(page).WaitForRowAsync(fixture.ExistingDocumentName);
                var englishSearch = page.GetByRole(AriaRole.Textbox, new() { Name = "Search nodes", Exact = true });
                await englishSearch.FillAsync("");
                var searchRead = page.WaitForResponseAsync(response => response.Request.Method == "GET"
                    && new Uri(response.Url).AbsolutePath == "/api/trees/workspace.structure/search");
                await englishSearch.FillAsync(folder);
                var searchResponse = await searchRead;
                if (!searchResponse.Ok) throw new InvalidOperationException($"英文搜索读取失败：{searchResponse.Status}");
                await searchResponse.FinishedAsync();
                await page.Locator("[aria-label='Search results']")
                    .GetByRole(AriaRole.Button, new() { Name = "Locate node " + folder, Exact = true }).First
                    .WaitForAsync(new() { State = WaitForSelectorState.Visible });
                await CaptureAsync("search-distinct-paths-en.png");
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { url = address, actor = "ordinary", journey = "workspace-search-path" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 Link 迁移、普通账号创建文件夹与共享树/列表/属性 driver")]
    public async Task WorkspaceFolderCreation_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("workspace-folder");
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "公共空间文件夹创建、移除引用与再次新建 driver")]
    public async Task WorkspaceReferences_UseFormalDriver()
    {
        var (root, runId) = PrepareRun("workspace-references");
        var name = $"来源文件夹旅程 {runId[..8]}";
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false,
            selectFeatures: features =>
            {
                var entry = features.Behavior("Workspace", "workspace.spaces.access");
                return [entry, features.Action(entry, "create", "list.pageActions", "folder"),
                    features.Action(entry, "add", "list.custom", "folder"), features.Action(entry, "remove", "list.rowActions", "folder")];
            },
            prepareJourney: async (administrator, features, actor, token) =>
            {
                var entry = features.Behavior("Workspace", "workspace.spaces.access");
                await WorkspaceAcceptanceFixture.SendAsync(administrator, HttpMethod.Post,
                    "/api/workspace/spaces/public/initialize", null, entry.Position, token);
                using var ordinary = WorkspaceAcceptanceFixture.CreateClient(administrator.BaseAddress!.Port);
                await WorkspaceAcceptanceFixture.LoginAsync(ordinary, actor, token);
                var folderId = await WorkspaceAcceptanceFixture.PrepareOrphanFolderAsync(ordinary, features, name, token);
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "fixture-folder.json"), new { folderId, actorId = actor.AccountId, name }, token);
            },
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                async Task CapturePairAsync(string stage, ILocator? surface = null)
                {
                    // Modal 外容器先可见，等待其内容动画完成后才能留下可核对的截图。
                    if (surface is not null) await surface.EvaluateAsync("async element => { await new Promise(requestAnimationFrame); await Promise.all(element.getAnimations({subtree:true}).map(animation => animation.finished.catch(() => {}))); }");
                    await page.ScreenshotAsync(new() { Path = Path.Combine(root, stage + ".png"), FullPage = true, Animations = ScreenshotAnimations.Disabled });
                    await page.SetViewportSizeAsync(900, 1000);
                    await page.ScreenshotAsync(new() { Path = Path.Combine(root, stage + "-narrow.png"), FullPage = true, Animations = ScreenshotAnimations.Disabled });
                    await page.SetViewportSizeAsync(1440, 1000);
                }
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                await driver.CreateFolderAsync(name);
                await driver.ExpandRootToFolderAsync("公共空间", name);
                await CapturePairAsync("folder-reference-list");
                await driver.RemoveReferenceAsync(name, dialog => CapturePairAsync("remove-reference", dialog));
                await driver.CreateFolderAsync(name);
                await driver.ExpandRootToFolderAsync("公共空间", name);
                var properties = await driver.OpenFolderPropertiesAsync(name);
                await CapturePairAsync("shared-folder-properties");
                await driver.ClosePropertiesAsync(properties);
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { url = page.Url, actor = "ordinary", readdedFolder = name }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "普通账号文件夹内新建零组件和图文档、所属属性及移除引用")]
    public async Task WorkspaceMixedContent_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("workspace-mixed");
        var suffix = runId[..8];
        var folderName = $"混合文件夹旅程 {suffix}";
        var partCode = $"WS-P-{suffix}";
        var partName = $"混合零组件旅程 {suffix}";
        var documentCode = $"WS-D-{suffix}";
        var documentName = $"混合图文档旅程 {suffix}";
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false,
            selectFeatures: features =>
            {
                var entry = features.Behavior("Workspace", "workspace.spaces.access");
                var part = features.Behavior("Part", "part.part.read");
                var document = features.Behavior("Document", "document.document.read");
                return [entry, features.Action(entry, "create", "list.pageActions", "folder"),
                    features.Action(entry, "add", "list.custom", "folder"), features.Action(entry, "remove", "list.rowActions", "folder"),
                    part, document, features.Behavior("Document", "document.document.download"),
                    features.Action(entry, "createPart", "list.pageActions", "folder"), features.Action(entry, "createDocument", "list.pageActions", "folder")];
            },
            prepareJourney: async (administrator, features, actor, token) =>
            {
                var entry = features.Behavior("Workspace", "workspace.spaces.access");
                await WorkspaceAcceptanceFixture.SendAsync(administrator, HttpMethod.Post, "/api/workspace/spaces/public/initialize", null, entry.Position, token);
                using var ordinary = WorkspaceAcceptanceFixture.CreateClient(administrator.BaseAddress!.Port);
                await WorkspaceAcceptanceFixture.LoginAsync(ordinary, actor, token);
                var folderId = await WorkspaceAcceptanceFixture.PrepareOrphanFolderAsync(ordinary, features, folderName, token);
                var spaces = await WorkspaceAcceptanceFixture.SendAsync(ordinary, HttpMethod.Get, "/api/workspace/spaces", null, entry.Position, token);
                var publicRoot = spaces.EnumerateArray().Single(space => space.GetProperty("spaceKind").GetString() == "Public");
                var position = new { spaceId = publicRoot.GetProperty("spaceId").GetString(), rootFolderId = publicRoot.GetProperty("rootFolderId").GetString(), linkIds = Array.Empty<string>() };
                await WorkspaceAcceptanceFixture.SendAsync(ordinary, HttpMethod.Post, "/api/workspace/content/references",
                    new { position, target = new { modelKey = "WorkspaceFolder", id = folderId } },
                    features.Action(entry, "add", "list.custom", "folder").Position, token);
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "fixture-mixed.json"), new { folderId, actorId = actor.AccountId }, token);
                await WorkspaceAcceptanceFixture.VerifyPartCreationAsync(ordinary, features, publicRoot, runId, token);
            },
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                async Task CapturePairAsync(string stage, ILocator? surface = null)
                {
                    if (surface is not null) await surface.EvaluateAsync("async element => { await new Promise(requestAnimationFrame); await Promise.all(element.getAnimations({subtree:true}).map(animation => animation.finished.catch(() => {}))); }");
                    await page.ScreenshotAsync(new() { Path = Path.Combine(root, stage + ".png"), FullPage = true, Animations = ScreenshotAnimations.Disabled });
                    await page.SetViewportSizeAsync(900, 1000);
                    await page.ScreenshotAsync(new() { Path = Path.Combine(root, stage + "-narrow.png"), FullPage = true, Animations = ScreenshotAnimations.Disabled });
                    await page.SetViewportSizeAsync(1440, 1000);
                }
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                var folderAddress = await driver.SelectTreePathAsync("公共空间", folderName);
                await driver.CreateFolderAsync(folderName + " 子文件夹");
                await driver.CreateObjectAsync("零组件", partCode, partName, dialog => CapturePairAsync("part-create", dialog));
                var attachments = WorkspacePageDriver.SmallAttachmentFiles();
                await driver.CreateDocumentWithFilesAsync(documentCode, documentName, WorkspacePageDriver.SmallMainFile(), dialog => CapturePairAsync("document-create", dialog), attachments);
                await new StandardCrudPageDriver(page).WaitForRowAsync(folderName + " 子文件夹");
                await CapturePairAsync("mixed-content");
                await driver.OpenOwnedPropertiesAsync(partName, "/parts");
                await CapturePairAsync("mixed-part-properties");
                await driver.RestorePositionAsync(folderAddress, "公共空间", folderName);
                await driver.OpenOwnedPropertiesAsync(documentName, "/documents");
                foreach (var file in new[] { WorkspacePageDriver.SmallMainFile() }.Concat(attachments))
                    await driver.DownloadFileAsync(file.Name, Path.Combine(root, "download-" + file.Name));
                await CapturePairAsync("mixed-document-properties");
                await driver.RestorePositionAsync(folderAddress, "公共空间", folderName);
                await driver.RemoveReferenceAsync(partName, dialog => CapturePairAsync("mixed-remove-reference", dialog));
                await driver.CreateObjectAsync("零组件", partCode + "-NEXT", partName + " 新建");
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"), new { url = page.Url, actor = "ordinary", readdedObject = partName }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "完整出现位置实际地址恢复、失效回退及共享组织树只读呈现")]
    public async Task WorkspaceOccurrenceRecovery_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("workspace-occurrence");
        WorkspaceOccurrenceFixture? graph = null;
        WorkspaceAcceptanceActor? preparedActor = null;
        AcceptanceFeatureCatalog? preparedFeatures = null;
        var hostPort = 0;
        var companyName = $"工作空间验收公司 {runId[..8]}";
        var departmentName = $"工作空间验收部门 {runId[..8]}";
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false,
            selectFeatures: features =>
            {
                var entry = features.Behavior("Workspace", "workspace.spaces.access");
                var organization = features.OrganizationTab("structure");
                return [entry, features.Action(entry, "create", "list.pageActions", "folder"),
                    features.Action(entry, "add", "list.custom", "folder"), features.Action(entry, "remove", "list.rowActions", "folder"),
                    organization, features.NodeTab(organization, "company", "information"), features.NodeTab(organization, "department", "information")];
            },
            prepareJourney: async (administrator, features, actor, token) =>
            {
                hostPort = administrator.BaseAddress!.Port;
                preparedActor = actor;
                preparedFeatures = features;
                var entry = features.Behavior("Workspace", "workspace.spaces.access");
                await WorkspaceAcceptanceFixture.SendAsync(administrator, HttpMethod.Post, "/api/workspace/spaces/public/initialize", null, entry.Position, token);
                using var ordinary = WorkspaceAcceptanceFixture.CreateClient(hostPort);
                await WorkspaceAcceptanceFixture.LoginAsync(ordinary, actor, token);
                graph = await WorkspaceAcceptanceFixture.PrepareOccurrenceGraphAsync(ordinary, features, runId, token);
                // 先用同普通账号正式树 API 核对读取资格，失败直接定位端点，不留到 UI 控件超时。
                var organization = features.OrganizationTab("structure");
                var companies = await WorkspaceAcceptanceFixture.SendAsync(ordinary, HttpMethod.Get,
                    "/api/trees/organization.structure/roots?scope=global", null, organization.Position, token);
                var company = companies.EnumerateArray().Single(node => node.GetProperty("label").GetString() == companyName);
                var children = await WorkspaceAcceptanceFixture.SendAsync(ordinary, HttpMethod.Get,
                    $"/api/trees/organization.structure/children?scope=global&nodeKey={Uri.EscapeDataString(company.GetProperty("nodeKey").GetString()!)}", null, organization.Position, token);
                if (!children.EnumerateArray().Any(node => node.GetProperty("label").GetString() == departmentName))
                    throw new InvalidOperationException("普通主体正式组织树未返回既有准备的部门。");
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "fixture-occurrence.json"), new { graph, companyName, departmentName }, token);
            },
            browserJourney: async (page, token) =>
            {
                var fixture = graph ?? throw new InvalidOperationException("出现位置样本尚未准备。");
                var driver = new WorkspacePageDriver(page);
                var tree = new StandardTreePageDriver(page);
                async Task CapturePairAsync(string stage)
                {
                    await page.ScreenshotAsync(new() { Path = Path.Combine(root, stage + ".png"), FullPage = true, Animations = ScreenshotAnimations.Disabled });
                    await page.SetViewportSizeAsync(900, 1000);
                    await page.ScreenshotAsync(new() { Path = Path.Combine(root, stage + "-narrow.png"), FullPage = true, Animations = ScreenshotAnimations.Disabled });
                    await page.SetViewportSizeAsync(1440, 1000);
                }
                await driver.OpenAsync();
                var address = await driver.SelectTreePathAsync(fixture.Labels);
                await new StandardCrudPageDriver(page).WaitForRowAsync(fixture.SampleName);
                await tree.ClickAncestorAsync(fixture.Labels[^2]);
                await new StandardCrudPageDriver(page).WaitForRowAsync(fixture.Labels[^1]);
                await driver.SelectRootAsync("个人空间");
                await driver.RestorePositionAsync(address, fixture.Labels);
                await new StandardCrudPageDriver(page).WaitForRowAsync(fixture.SampleName);
                await CapturePairAsync("restored-occurrence");
                using (var ordinary = WorkspaceAcceptanceFixture.CreateClient(hostPort))
                {
                    await WorkspaceAcceptanceFixture.LoginAsync(ordinary, preparedActor!, token);
                    await WorkspaceAcceptanceFixture.InvalidateOccurrenceAsync(ordinary, preparedFeatures!, fixture, token);
                }
                await driver.RestoreMissingPositionAsync(address);
                var fallbackUrl = page.Url;
                await CapturePairAsync("missing-occurrence");
                await new StandardCrudPageDriver(page).OpenOrganizationTabAsync("组织结构", "structure");
                await tree.SelectPathAsync(companyName, departmentName);
                var content = page.GetByRole(AriaRole.Region, new() { Name = "节点内容", Exact = true });
                await content.GetByText("基本信息", new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
                await Assertions.Expect(content.Locator(".ant-spin-spinning:visible")).ToHaveCountAsync(0, new() { Timeout = 30_000 });
                await CapturePairAsync("organization-breadcrumb");
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"), new { address, fallbackUrl, organizationUrl = page.Url, actor = "ordinary" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "Folder Drawer与Part独立详情的反向标准列表和嵌套父Folder属性")]
    public async Task WorkspaceDirectReferences_UseFormalDriver()
    {
        var (root, runId) = PrepareRun("workspace-direct-references");
        var folderName = $"反向文件夹旅程 {runId[..8]}";
        var partCode = $"REV-P-{runId[..8]}";
        var partName = $"反向零组件旅程 {runId[..8]}";
        var parentName = "公共空间";
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false,
            selectFeatures: features =>
            {
                var entry = features.Behavior("Workspace", "workspace.spaces.access");
                var part = features.Behavior("Part", "part.part.read");
                return [entry, features.Action(entry, "create", "list.pageActions", "folder"),
                    features.Action(entry, "add", "list.custom", "folder"), part, features.Action(part, "create", "list.pageActions")];
            },
            prepareJourney: async (administrator, features, actor, token) =>
            {
                var entry = features.Behavior("Workspace", "workspace.spaces.access");
                await WorkspaceAcceptanceFixture.SendAsync(administrator, HttpMethod.Post, "/api/workspace/spaces/public/initialize", null, entry.Position, token);
                using var ordinary = WorkspaceAcceptanceFixture.CreateClient(administrator.BaseAddress!.Port);
                await WorkspaceAcceptanceFixture.LoginAsync(ordinary, actor, token);
                var spaces = await WorkspaceAcceptanceFixture.SendAsync(ordinary, HttpMethod.Get, "/api/workspace/spaces", null, entry.Position, token);
                var publicRoot = spaces.EnumerateArray().Single(space => space.GetProperty("spaceKind").GetString() == "Public");
                parentName = publicRoot.GetProperty("name").GetString()!;
                var rootId = publicRoot.GetProperty("rootFolderId").GetString()!;
                var position = new WorkspaceFolderPosition(publicRoot.GetProperty("spaceId").GetString()!, rootId, rootId, []);
                var folder = await WorkspaceAcceptanceFixture.CreateFolderAtPositionAsync(ordinary, features, position, folderName, token);
                var partId = await WorkspaceAcceptanceFixture.CreateMetadataObjectAsync(ordinary, features, "Part", "part.part.read", "/api/part/parts", partCode, partName, token);
                await WorkspaceAcceptanceFixture.SendAsync(ordinary, HttpMethod.Post, "/api/workspace/content/references",
                    new { position = position.CommandPosition, target = new { modelKey = "Part", id = partId } },
                    features.Action(entry, "add", "list.custom", "folder").Position, token);
                // 先证明普通主体按来源位置能读反向与父对象，避免以UI超时诊断权限/锚点准备错误。
                foreach (var (model, id) in new[] { ("WorkspaceFolder", folder.FolderId), ("Part", partId) })
                {
                    var references = await WorkspaceAcceptanceFixture.ReadDirectReferencesAsync(ordinary, features, model, id, token);
                    if (!references.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("associatedObject").GetProperty("id").GetString() == rootId))
                        throw new InvalidOperationException("正式反向读取未返回已准备的直接父文件夹。");
                }
                await WorkspaceAcceptanceFixture.SendAsync(ordinary, HttpMethod.Get, $"/api/workspace/folders/{Uri.EscapeDataString(rootId)}", null, entry.Position, token);
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "fixture-references.json"), new { folderId = folder.FolderId, partId, parentId = rootId, parentName }, token);
            },
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                async Task CapturePairAsync(string stage)
                {
                    await page.ScreenshotAsync(new() { Path = Path.Combine(root, stage + ".png"), FullPage = true, Animations = ScreenshotAnimations.Disabled });
                    await page.SetViewportSizeAsync(900, 1000);
                    await page.ScreenshotAsync(new() { Path = Path.Combine(root, stage + "-narrow.png"), FullPage = true, Animations = ScreenshotAnimations.Disabled });
                    await page.SetViewportSizeAsync(1440, 1000);
                }
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                var detail = await driver.OpenFolderPropertiesAsync(folderName);
                await CapturePairAsync("folder-reference-entry");
                var references = await driver.OpenDirectReferencesAsync(detail, parentName);
                await CapturePairAsync("folder-direct-references");
                var parent = await driver.OpenReferencingFolderPropertiesAsync(references, parentName);
                await CapturePairAsync("referencing-folder-properties");
                await driver.ClosePropertiesAsync(parent);
                await driver.CloseDirectReferencesAsync(references);
                await driver.ClosePropertiesAsync(detail);
                await driver.OpenOwnedPropertiesAsync(partName, "/parts");
                var partAddress = page.Url;
                await CapturePairAsync("part-reference-entry");
                references = await driver.OpenDirectReferencesAsync(page.GetByRole(AriaRole.Main), parentName);
                await CapturePairAsync("part-direct-references");
                parent = await driver.OpenReferencingFolderPropertiesAsync(references, parentName);
                await driver.ClosePropertiesAsync(parent);
                await driver.CloseDirectReferencesAsync(references);
                await Assertions.Expect(page).ToHaveURLAsync(partAddress);
                await page.GetByText("审计信息", new() { Exact = true }).WaitForAsync(new() { State = WaitForSelectorState.Visible });
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"), new { url = page.Url, actor = "ordinary", entryForms = new[] { "FolderDrawer", "PartDetail" } }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "正式 Part 迁移、普通账号标准创建及 Drawer/独立只读属性 driver")]
    public async Task PartCreation_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("part");
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false,
            selectFeatures: features =>
            {
                var entry = features.Behavior("Part", "part.part.read");
                return [entry, features.Action(entry, "create", "list.pageActions")];
            },
            browserJourney: (page, token) => RunMetadataPageJourneyAsync(page, token, root, runId,
                "part", "零组件旅程", new(page, "零组件", "/parts", "/api/part/parts", "新建零组件")))
            .RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_COMPONENT_ACCEPTANCE", "固定原生发行物、正式旧 Part SQL 输入与安装后连续性")]
    public async Task NativeComponentPartApi_UsesFixedRelease()
    {
        var (root, runId) = PrepareRun("native-component-part-api");
        var fixture = PublishedComponentAcceptanceFixture.FromEnvironment(RepositoryRoot, root, runId);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false,
            publishedFixture: fixture, includeBrowser: false).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_COMPONENT_ACCEPTANCE", "固定原生发行物使用同一正式 Part 页面驱动")]
    public async Task NativeComponentPart_UsesFixedReleaseAndFormalDriver()
    {
        var (root, runId) = PrepareRun("native-component-part");
        var fixture = PublishedComponentAcceptanceFixture.FromEnvironment(RepositoryRoot, root, runId);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false,
            publishedFixture: fixture, browserJourney: async (page, token) =>
            {
                var driver = new StandardMetadataPageDriver(page, "零组件", "/parts", "/api/part/parts", "新建零组件");
                Task CaptureAsync(string name) => page.ScreenshotAsync(new()
                { Path = Path.Combine(root, name), FullPage = true, Animations = ScreenshotAnimations.Disabled });
                await driver.OpenAsync();
                await CaptureAsync("part-existing-list.png");
                var oldProperties = await driver.OpenPropertiesAsync(fixture.ExistingCode, fixture.ExistingName);
                await CaptureAsync("part-existing-properties.png");
                await page.SetViewportSizeAsync(900, 1000);
                await CaptureAsync("part-existing-properties-narrow.png");
                await page.SetViewportSizeAsync(1440, 1000);
                await driver.OpenFullDetailAsync(oldProperties, fixture.ExistingName);
                await CaptureAsync("part-existing-detail.png");
                await page.SetViewportSizeAsync(900, 1000);
                await CaptureAsync("part-existing-detail-narrow.png");
                await page.SetViewportSizeAsync(1440, 1000);
                await driver.OpenAsync();
                var create = await driver.OpenCreateAsync();
                await CaptureAsync("part-create.png");
                await page.SetViewportSizeAsync(900, 1000);
                await CaptureAsync("part-create-narrow.png");
                await page.SetViewportSizeAsync(1440, 1000);
                var code = "ACC-NATIVE-P-" + runId[..8];
                var name = "独立安装页面零组件 " + runId[..8];
                await driver.SaveCreateAsync(create, code, name);
                await CaptureAsync("part-created-list.png");
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { early271 = true, url = page.Url, actor = "ordinary", createdObject = code }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "正式 Document 迁移、普通账号标准创建及 Drawer/独立只读属性 driver")]
    public async Task DocumentCreation_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("document");
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false,
            selectFeatures: features =>
            {
                var entry = features.Behavior("Document", "document.document.read");
                return [entry, features.Action(entry, "create", "list.pageActions")];
            },
            browserJourney: (page, token) => RunMetadataPageJourneyAsync(page, token, root, runId,
                "document", "图文档旅程", new(page, "图文档", "/documents", "/api/document/documents", "新建图文档")))
            .RunAsync(CancellationToken.None);
    }

    // 两个真实元数据模块共用同一标准旅程，差异只来自各自正式入口和 API 声明。
    private static async Task RunMetadataPageJourneyAsync(IPage page, CancellationToken token, string root, string runId,
        string prefix, string namePrefix, StandardMetadataPageDriver driver)
    {
        var code = $"ACC-{prefix.ToUpperInvariant()}-{runId[..8]}";
        var name = $"{namePrefix} {runId[..8]}";
        Task CaptureAsync(string file) => page.ScreenshotAsync(new()
        {
            Path = Path.Combine(root, file), FullPage = true, Animations = ScreenshotAnimations.Disabled
        });
        await driver.OpenAsync();
        var form = await driver.OpenCreateAsync();
        await CaptureAsync($"{prefix}-create.png");
        await page.SetViewportSizeAsync(900, 1000);
        await CaptureAsync($"{prefix}-create-narrow.png");
        await page.SetViewportSizeAsync(1440, 1000);
        await driver.SaveCreateAsync(form, code, name);
        await CaptureAsync($"{prefix}-list.png");
        await page.SetViewportSizeAsync(900, 1000);
        await CaptureAsync($"{prefix}-list-narrow.png");
        await page.SetViewportSizeAsync(1440, 1000);
        var properties = await driver.OpenPropertiesAsync(code, name);
        await CaptureAsync($"{prefix}-properties.png");
        await page.SetViewportSizeAsync(900, 1000);
        await CaptureAsync($"{prefix}-properties-narrow.png");
        await page.SetViewportSizeAsync(1440, 1000);
        await driver.OpenFullDetailAsync(properties, name);
        await CaptureAsync($"{prefix}-detail.png");
        await page.SetViewportSizeAsync(900, 1000);
        await CaptureAsync($"{prefix}-detail-narrow.png");
        await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
            new { url = page.Url, actor = "ordinary", createdObject = code }, token);
    }
    // run id 参与一次性 catalog 名与资源根命名，必须是固定长度的十六进制。
    private static (string RunDirectory, string RunId) PrepareRun(string gate)
    {
        var runId = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var runDirectory = Path.Combine(RepositoryRoot, ".codex-temp", "acceptance", gate, runId);
        Directory.CreateDirectory(runDirectory);
        return (runDirectory, runId);
    }

    private static string RepositoryRoot { get; } = LocateRepositoryRoot();

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ICP-Platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}

// 闸门共享 bin/Release 输出、.codex-temp/dev/state.json 与本机端口，因此必须串行。
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AcceptanceGateCollection
{
    public const string Name = "acceptance-gates";
}
