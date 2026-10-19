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
    /// <summary>真实普通管理主体从当前Workspace混合行选择进入标准批量Review并保存一次。</summary>
    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通Workspace管理主体在混合Part/Document上打开批量授权、编辑主体并一次保存")]
    public async Task NativeWorkspaceBatchAuthorization_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-batch-authorization");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease, prepareBatchActor: true);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                token.ThrowIfCancellationRequested();
                var driver = new WorkspacePageDriver(page);
                await driver.OpenAsync();
                await driver.SelectTreePathAsync("公共空间", fixture.ExistingFolderName);
                await driver.SaveBatchAuthorizationAsync(fixture.ExistingName, fixture.ExistingDocumentName, Path.Combine(root, "batch-authorization-input.png"));
                token.ThrowIfCancellationRequested();
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通所属人标准转交一次，接收人真实登录进入个人目录/通知并打开原对象")]
    public async Task NativeWorkspaceOwnershipTransfer_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-ownership-transfer");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease, prepareTransferActors: true);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                token.ThrowIfCancellationRequested();
                var owner = new WorkspacePageDriver(page);
                await owner.OpenAsync();
                await owner.SelectTreePathAsync("公共空间", fixture.TransferFolderName);
                await owner.TransferOwnershipAsync(fixture.TransferTargetName, fixture.TransferRecipient.ECode);
                await page.GetByRole(AriaRole.Button, new() { Name = ShellPageDriver.SignOutLabel, Exact = true }).ClickAsync();
                await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/login");
                var recipient = fixture.TransferRecipient;
                await new ShellPageDriver(page, new Uri(page.Url).GetLeftPart(UriPartial.Authority)).LoginAsync(recipient.ECode, recipient.Password);
                await new WorkspacePageDriver(page).OpenReceivedTransferAsync(fixture.TransferTargetName);
                token.ThrowIfCancellationRequested();
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "仅既有Workspace浏览资格打开Part来源属性/权限，再由独立管理账号修改草稿并标准保存一次")]
    public async Task NativeWorkspacePartPermissions_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-part-permissions");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease, preparePermissionActors: true);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                token.ThrowIfCancellationRequested();
                var reader = new WorkspacePageDriver(page);
                await reader.OpenAsync();
                await reader.SelectTreePathAsync("公共空间", fixture.ExistingFolderName);
                var sourceAddress = await reader.OpenPermissionViewerAsync(fixture.ExistingName);
                await reader.InspectPermissionTabAsync();
                await reader.RestorePositionAsync(sourceAddress, "公共空间", fixture.ExistingFolderName);
                await page.GetByRole(AriaRole.Button, new() { Name = ShellPageDriver.SignOutLabel, Exact = true }).ClickAsync();
                await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/login");
                var manager = fixture.PermissionManager;
                await new ShellPageDriver(page, new Uri(page.Url).GetLeftPart(UriPartial.Authority)).LoginAsync(manager.ECode, manager.Password);
                var writer = new WorkspacePageDriver(page);
                await writer.OpenAsync();
                await writer.SelectTreePathAsync("公共空间", fixture.ExistingFolderName);
                await writer.AddReadPermissionAndSaveAsync(fixture.ExistingName);
            }).RunAsync(CancellationToken.None);
    }

    /// <summary>普通Workspace主体经真实Part来源读取履历、详情及选中输出，不借独立Part或管理员审计资格。</summary>
    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通Workspace读者通过真实Part来源查看履历和详情、下载XLSX并调用真实打印")]
    public async Task NativeWorkspacePartHistory_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-part-history");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease, prepareHistoryActor: true);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                token.ThrowIfCancellationRequested();
                var reader = new WorkspacePageDriver(page);
                await reader.OpenAsync();
                await reader.SelectTreePathAsync("公共空间", fixture.ExistingFolderName);
                await reader.OpenPermissionViewerAsync(fixture.ExistingName);
                await reader.InspectObjectHistoryAndOutputAsync(Path.Combine(root, "history-selected.xlsx"));
                await reader.OpenAsync();
                await reader.SelectTreePathAsync("公共空间", fixture.ExistingFolderName);
                await reader.OpenPermissionViewerAsync(fixture.ExistingName);
                await fixture.DenyHistorySourceReadAsync(token);
                await reader.RejectObjectHistoryEntryAsync();
                token.ThrowIfCancellationRequested();
            }).RunAsync(CancellationToken.None);
    }

    private const string RunEnvironmentVariable = "NEWP_RUN_ACCEPTANCE";

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "管理员显式组织初始化及三模型缺失归属的正式页面、批次和人工补齐最短路径")]
    public async Task NativeWorkspaceOwnership_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-ownership");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, publishedFixture: fixture, browserAsAdministrator: true,
            prepareJourney: (_, _, _, token) => fixture.PrepareOwnershipLegacyRowsAsync(token),
            browserJourney: async (page, token) =>
            {
                var crud = new StandardCrudPageDriver(page);
                var driver = new WorkspacePageDriver(page);
                Task Capture(string name) => AcceptancePresentation.CaptureAsync(page, Path.Combine(root, name));
                await crud.OpenOrganizationTabAsync("公司", "companies");
                await Capture("organization-initialization-zhs.png");
                var initialization = page.WaitForResponseAsync(response => response.Request.Method == "POST"
                    && new Uri(response.Url).AbsolutePath == "/api/organization/initialization");
                await crud.ClickPageActionAsync("初始化默认组织");
                Assert.Equal(200, (await initialization).Status);
                await crud.WaitForTableReadyAsync();
                if (AcceptancePresentation.Enabled)
                {
                    await driver.SelectLanguageAsync("English");
                    await page.GetByRole(AriaRole.Button, new() { Name = "Initialize Default Organization", Exact = true })
                        .WaitForAsync(new() { State = WaitForSelectorState.Visible });
                    await Capture("organization-initialization-en.png");
                    await driver.SelectLanguageAsync("简体中文");
                }
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                var properties = await driver.OpenFolderPropertiesAsync(fixture.ExistingFolderName);
                await Capture("ownership-folder-properties.png");
                await driver.ClosePropertiesAsync(properties);
                var dialog = await driver.OpenOwnershipInitializationAsync(fixture.ExistingFolderName);
                await Capture("ownership-folder-initialize.png");
                await crud.ConfirmAsync(dialog, new("POST", "/api/workspace/content/", 200, "文件夹归属补齐", PathIsPrefix: true));
                var address = await driver.SelectTreePathAsync("公共空间", fixture.ExistingFolderName);
                dialog = await driver.OpenOwnershipInitializationAsync();
                await dialog.GetByRole(AriaRole.Button, new() { Name = "选择当前许可页", Exact = true }).ClickAsync();
                await Capture("ownership-selected-batch.png");
                await crud.ConfirmAsync(dialog, new("POST", "/api/workspace/ownership/initialize", 200, "所选旧对象归属补齐"));
                if (AcceptancePresentation.Enabled)
                {
                    await driver.SelectLanguageAsync("English");
                    dialog = await driver.OpenOwnershipInitializationAsync(fixture.ExistingDocumentName, english: true);
                    await Capture("ownership-input-en.png");
                    await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
                    await driver.SelectLanguageAsync("简体中文");
                }
                dialog = await driver.OpenOwnershipInitializationAsync(fixture.ExistingDocumentName);
                await driver.ChooseOwnershipAccountAsync(dialog, "admin");
                await Capture("ownership-manual-account.png");
                await crud.ConfirmAsync(dialog, new("POST", "/api/workspace/content/", 200, "未知创建账号的人工归属补齐", PathIsPrefix: true));
                await driver.OpenOwnedPropertiesAsync(fixture.ExistingName, "/parts");
                await Capture("ownership-part-properties.png");
                await driver.RestorePositionAsync(address, "公共空间", fixture.ExistingFolderName);
                await driver.OpenOwnedPropertiesAsync(fixture.ExistingDocumentName, "/documents");
                await Capture("ownership-document-properties.png");
                await driver.RestorePositionAsync(address, "公共空间", fixture.ExistingFolderName);
                await driver.EditObjectAsync(fixture.ExistingDocumentName, fixture.ExistingDocumentName + " 已保存");
                await crud.WaitForRowAsync(fixture.ExistingDocumentName + " 已保存");
                await Capture("ownership-ordinary-save.png");
                token.ThrowIfCancellationRequested();
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "一次性 SQL Server catalog、dev supervisor 与真实浏览器")]
    public async Task ExampleRequiredField_CompletesEveryScenario()
    {
        var (root, runId) = PrepareRun("example-required-field");
        await new ExampleRequiredFieldAcceptanceRunner(RepositoryRoot, root, runId).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "共享专用 SQL Server 租约与正式 Example 页面创建、编辑和删除声明接线")]
    public async Task ExampleDataExecutionCrud_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("example-data-execution");
        await new ExampleRequiredFieldAcceptanceRunner(RepositoryRoot, root, runId, declarationCrudOnly: true).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "共享SQL租约与真实Example到通用对象授权的查看、草稿和保存接线")]
    public async Task ExampleObjectAuthorization_UsesFormalCrossModuleBinding()
    {
        var (root, runId) = PrepareRun("example-authorization-execution");
        await new ExampleRequiredFieldAcceptanceRunner(RepositoryRoot, root, runId, declarationAuthorizationOnly: true).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "共享SQL租约与同一正式宿主的Example、Person页面current/all下载接线")]
    public async Task StandardCrudExport_UsesFormalExampleAndPersonBindings()
    {
        var (root, runId) = PrepareRun("standard-crud-export");
        await new ExampleRequiredFieldAcceptanceRunner(RepositoryRoot, root, runId, declarationExportOnly: true).RunAsync(CancellationToken.None);
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

    [EnvironmentGatedFact(RunEnvironmentVariable, "当前固定发行物的Folder/Part/Document代表性页面接线，不包含历史升级或恢复演练")]
    public async Task WorkspaceSmoke_UsesFixedRelease()
    {
        var (root, runId) = PrepareRun("workspace-smoke");
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId,
            publishedFixture: PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
                exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease)).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "当前正式Host同库重启后保持空间、根和混合引用身份，不重放页面或历史升级")]
    public async Task NativeWorkspaceProcessRestart_UsesFixedRelease()
    {
        var (root, runId) = PrepareRun("native-workspace-process-restart");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: true,
            publishedFixture: fixture, includeBrowser: false).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "仅验证正式Document批次复制的进程中断与恢复，不重放Part或页面旅程")]
    public async Task NativeWorkspaceDocumentBatchRecovery_UsesFormalProcesses()
    {
        var (root, runId) = PrepareRun("native-workspace-document-batch-recovery");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, exerciseDocumentBatchRecovery: true, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, publishedFixture: fixture,
            includeBrowser: false).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "三成员固定发行物与旧 Workspace/Part/Document 行、Link、历史连续性")]
    public async Task NativeWorkspaceApi_UsesFixedRelease()
    {
        var (root, runId) = PrepareRun("native-workspace-api");
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false,
            publishedFixture: PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId),
            includeBrowser: false).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "仅验证父组件屏蔽与恢复成员自身选择，不重放页面或文件故障恢复")]
    public async Task NativeWorkspaceCompositionLifecycle_UsesFixedRelease()
    {
        var (root, runId) = PrepareRun("native-workspace-composition");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: true);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, publishedFixture: fixture,
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
            exerciseCompositionLifecycle: false, exerciseFileRecovery: true, exerciseFileRecoveryMechanisms: true, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false,
            publishedFixture: fixture, includeBrowser: false).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "正式 Host 同库同 root 重启保留主文件与附件集合身份及逐文件下载")]
    public async Task NativeWorkspaceFileCollection_RestartsWithSavedIdentityAndBytes()
    {
        var (root, runId) = PrepareRun("native-workspace-file-collection");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, exerciseFileRecovery: true, exerciseFileRecoveryMechanisms: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false,
            publishedFixture: fixture, includeBrowser: false).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "三成员包直接初始化当前发行物，通过正式 Workspace driver 打开本轮内容、属性并创建一次对象")]
    public async Task NativeWorkspace_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                Task CaptureAsync(string name) => AcceptancePresentation.CaptureAsync(page, Path.Combine(root, name));
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                await new StandardCrudPageDriver(page).WaitForRowAsync(fixture.ExistingFolderName);
                await CaptureAsync("workspace-content.png");
                var properties = await driver.OpenFolderPropertiesAsync(fixture.ExistingFolderName);
                await CaptureAsync("workspace-properties.png");
                if (AcceptancePresentation.Enabled)
                {
                    await page.SetViewportSizeAsync(900, 1000);
                    await CaptureAsync("workspace-properties-narrow.png");
                }
                await driver.ClosePropertiesAsync(properties);
                if (AcceptancePresentation.Enabled)
                {
                    await page.SetViewportSizeAsync(1440, 1000);
                }
                var folderAddress = await driver.SelectTreePathAsync("公共空间", fixture.ExistingFolderName);
                var attachments = WorkspacePageDriver.SmallAttachmentFiles();
                await driver.CreateDocumentWithFilesAsync("ACC-WS-D-" + runId[..8], "归组图文档旅程 " + runId[..8], WorkspacePageDriver.SmallMainFile(), AcceptancePresentation.Enabled ? async _ =>
                {
                    await CaptureAsync("workspace-create.png");
                    await page.SetViewportSizeAsync(900, 1000);
                    await CaptureAsync("workspace-create-narrow.png");
                    await page.SetViewportSizeAsync(1440, 1000);
                } : null, attachments: attachments);
                await CaptureAsync("workspace-created-content.png");
                var documentName = "归组图文档旅程 " + runId[..8];
                await driver.OpenOwnedPropertiesAsync(documentName, "/documents");
                foreach (var file in new[] { WorkspacePageDriver.SmallMainFile() }.Concat(attachments))
                    await driver.DownloadFileAsync(file.Name, Path.Combine(root, "download-" + file.Name));
                await CaptureAsync("workspace-document-properties.png");
                if (AcceptancePresentation.Enabled)
                {
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
                }
                await driver.RestorePositionAsync(folderAddress, "公共空间", fixture.ExistingFolderName);
                await driver.EditObjectAsync(documentName, documentName + " 已编辑");
                await CaptureAsync("workspace-document-edited.png");
                await driver.DeleteSelectedAsync(documentName + " 已编辑");
                await CaptureAsync("workspace-document-deleted.png");
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { url = page.Url, actor = "ordinary", journey = "workspace-published-components" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "正式Host进程在Document复制receiving/完成/准备阶段强制中断并重启，确认内容跨停用恢复且测量真实文件成本")]
    public async Task NativeWorkspaceDocumentCopyRecovery_UsesRealProcessesAndFiles()
    {
        var (root, runId) = PrepareRun("native-workspace-document-copy-recovery");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, exerciseDocumentCopyRecovery: true, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture, includeBrowser: false)
            .RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "复用既有中断阶段证据，只定向核对真实文件副本提交后的进程重启、停用恢复与成本")]
    public async Task NativeWorkspaceDocumentCopyCommittedRecovery_UsesFormalProcesses()
    {
        var (root, runId) = PrepareRun("native-workspace-document-copy-committed-recovery");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, exerciseDocumentCopyRecovery: true, exerciseDocumentCopyInterruptions: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture, includeBrowser: false)
            .RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "正式Document完整文件副本通过Workspace driver确认、查看下载、编辑删除，按需补双语呈现并清理本轮资源")]
    public async Task NativeWorkspaceDocumentCopy_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-document-copy");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page); var suffix = runId[..8];
                var name = "图文档副本来源 " + suffix; var copied = "完整图文档副本 " + suffix; var target = "图文档副本目标 " + suffix;
                Task Capture(string file) => AcceptancePresentation.CaptureAsync(page, Path.Combine(root, file));
                await driver.OpenAsync(); await driver.SelectTreePathAsync("公共空间", fixture.ExistingFolderName); var sourceAddress = page.Url;
                await driver.CreateDocumentWithFilesAsync("ACC-DOC-SOURCE-" + suffix, name, WorkspacePageDriver.SmallMainFile(),
                    AcceptancePresentation.Enabled ? _ => Capture("document-create-reference.png") : null, WorkspacePageDriver.SmallAttachmentFiles());
                await driver.CopyReferenceFromContextMenuAsync(name);
                await driver.SelectRootAsync("公共空间"); await driver.CreateFolderAsync(target);
                var targetAddress = await driver.SelectTreePathAsync("公共空间", target);
                await driver.PasteObjectCopyAsync("ACC-DOC-COPY-" + suffix, copied, AcceptancePresentation.Enabled ? async _ => {
                    await Capture("document-copy-input-zhs.png");
                    await page.SetViewportSizeAsync(900, 1000);
                    await Capture("document-copy-input-zhs-narrow.png");
                    await page.SetViewportSizeAsync(1440, 1000);
                } : null);
                await Capture("document-copy-created.png");
                if (AcceptancePresentation.Enabled)
                {
                    await driver.SelectLanguageAsync("English"); var english = await driver.OpenObjectCopyAsync(english: true);
                    await Capture("document-copy-input-en.png");
                    await page.SetViewportSizeAsync(900, 1000);
                    await Capture("document-copy-input-en-narrow.png");
                    await page.SetViewportSizeAsync(1440, 1000);
                    await new StandardCrudPageDriver(page).CloseDrawerAsync(english); await driver.SelectLanguageAsync("简体中文");
                }
                await driver.OpenOwnedPropertiesAsync(copied, "/documents"); await driver.DownloadFileAsync(WorkspacePageDriver.SmallMainFile().Name, Path.Combine(root, "copy-main.pdf")); await Capture("document-copy-properties.png");
                await driver.RestorePositionAsync(targetAddress, "公共空间", target); await driver.EditObjectAsync(copied, copied + " 已编辑"); await driver.DeleteSelectedAsync(copied + " 已编辑");
                await driver.RestorePositionAsync(sourceAddress, "公共空间", fixture.ExistingFolderName); await driver.OpenOwnedPropertiesAsync(name, "/documents");
                await driver.DownloadFileAsync(WorkspacePageDriver.SmallMainFile().Name, Path.Combine(root, "source-main.pdf"));
                await driver.RestorePositionAsync(sourceAddress, "公共空间", fixture.ExistingFolderName); await driver.DeleteSelectedAsync(name);
                await driver.SelectRootAsync("公共空间"); await driver.DeleteSelectedAsync(target);
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"), new { url = page.Url, journey = "document-object-copy", actor = "ordinary" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_ACCEPTANCE_PRESENTATION", "仅补结果呈现采样：实际多文档正式提交结果沿同一driver等待动画并清理，不重放OS或完整旅程")]
    public async Task NativeWorkspaceDocumentBatchResult_PresentsFormalModal()
    {
        var (root, runId) = PrepareRun("native-workspace-document-batch-result");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) => {
                var driver = new WorkspacePageDriver(page); var suffix = runId[..8];
                var source = "结果呈现来源 " + suffix; var first = "结果呈现副本一 " + suffix; var second = "结果呈现副本二 " + suffix;
                await driver.OpenAsync(); await driver.SelectTreePathAsync("公共空间", fixture.ExistingFolderName);
                await driver.CreateDocumentWithFilesAsync("RESULT-SOURCE-" + suffix, source, WorkspacePageDriver.SmallMainFile());
                await driver.CopySelectedAsync(fixture.ExistingDocumentName, source);
                await driver.PasteDocumentCopiesAsync([(fixture.ExistingDocumentName, "RESULT-A-" + suffix, first), (source, "RESULT-B-" + suffix, second)],
                    captureResult: AcceptancePresentation.Enabled ? () => AcceptancePresentation.CaptureAsync(page, Path.Combine(root, "document-batch-result-stable.png")) : null);
                await driver.DeleteSelectedAsync(first, second, source);
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"), new { actor = "ordinary", journey = "document-batch-result-presentation" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "仅验证正式多Document页面片段及后续清理；进程恢复使用独立入口")]
    public Task NativeWorkspaceDocumentBatch_UsesFormalDriver() => RunDocumentBatchAsync();

    // 两个定向入口复用批次片段，由调用者显式选择是否组合 Part 和进程恢复。
    private async Task RunDocumentBatchAsync()
    {
        var (root, runId) = PrepareRun("native-workspace-document-batch");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) => {
                var driver = new WorkspacePageDriver(page); var suffix = runId[..8];
                var document = "批次旅程图文档 " + suffix; var target = "批次旅程目标 " + suffix;
                Task Capture(string name) => AcceptancePresentation.CaptureAsync(page, Path.Combine(root, name));
                await driver.OpenAsync(); await driver.SelectRootAsync("公共空间"); await driver.CreateFolderAsync(target);
                await driver.SelectTreePathAsync("公共空间", fixture.ExistingFolderName); var sourceAddress = page.Url;
                await driver.CreateDocumentWithFilesAsync("BATCH-DOC-" + suffix, document, WorkspacePageDriver.SmallMainFile(), attachments: WorkspacePageDriver.SmallAttachmentFiles());
                await driver.DocumentBatchCopyJourneyAsync(sourceAddress, fixture.ExistingFolderName, target, "BATCH-DOC-" + suffix, document, Path.Combine(root, "batch-copy-download.pdf"), Capture);
                await driver.DeleteSelectedAsync(document); await driver.SelectRootAsync("公共空间"); await driver.DeleteSelectedAsync(target);
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"), new { url = page.Url, actor = "ordinary", journey = "document-batch-copy" }, token);
            }).RunAsync(CancellationToken.None);
    }
    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "正式三成员包的单Part真副本、查看、独立编辑删除沿唯一Workspace driver贯通，按需补双语呈现")]
    public async Task NativeWorkspaceObjectCopy_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-object-copy");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                var name = "真实副本来源 " + runId[..8]; var copied = "真实独立副本 " + runId[..8]; var target = "对象副本目标 " + runId[..8];
                Task Capture(string file) => AcceptancePresentation.CaptureAsync(page, Path.Combine(root, file));
                await driver.OpenAsync(); await driver.SelectTreePathAsync("公共空间", fixture.ExistingFolderName);
                var sourceAddress = page.Url;
                await driver.CreateObjectAsync("零组件", "ACC-COPY-SOURCE-" + runId[..8], name, AcceptancePresentation.Enabled ? _ => Capture("part-create-reference.png") : null);
                await driver.CopyReferenceFromContextMenuAsync(name);
                await driver.SelectRootAsync("公共空间"); await driver.CreateFolderAsync(target);
                var targetAddress = await driver.SelectTreePathAsync("公共空间", target);
                await driver.PastePartCopyAsync("ACC-COPY-TARGET-" + runId[..8], copied, AcceptancePresentation.Enabled ? async _ =>
                {
                    await Capture("object-copy-input-zhs.png");
                    await page.SetViewportSizeAsync(900, 1000);
                    await Capture("object-copy-input-zhs-narrow.png");
                    await page.SetViewportSizeAsync(1440, 1000);
                } : null);
                await Capture("object-copy-created.png");
                // 复用copy来源的双语确认应在当前页面实例完成；进入独立详情和Goto恢复会按合同清空剪贴板。
                if (AcceptancePresentation.Enabled)
                {
                    await driver.SelectLanguageAsync("English");
                    var english = await driver.OpenObjectCopyAsync(english: true);
                    await Capture("object-copy-input-en.png");
                    await page.SetViewportSizeAsync(900, 1000);
                    await Capture("object-copy-input-en-narrow.png");
                    await page.SetViewportSizeAsync(1440, 1000);
                    await new StandardCrudPageDriver(page).CloseDrawerAsync(english);
                    await driver.SelectLanguageAsync("简体中文");
                }
                await driver.OpenOwnedPropertiesAsync(copied, "/parts"); await Capture("object-copy-properties.png");
                await driver.RestorePositionAsync(targetAddress, "公共空间", target);
                await driver.EditObjectAsync(copied, copied + " 已编辑"); await driver.DeleteSelectedAsync(copied + " 已编辑");
                await driver.RestorePositionAsync(sourceAddress, "公共空间", fixture.ExistingFolderName);
                await driver.OpenOwnedPropertiesAsync(name, "/parts");
                await driver.RestorePositionAsync(sourceAddress, "公共空间", fixture.ExistingFolderName);
                await driver.DeleteSelectedAsync(name);
                await driver.SelectRootAsync("公共空间"); await driver.DeleteSelectedAsync(target);
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"), new { url = page.Url, journey = "part-object-copy", actor = "ordinary" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通Folder新建及两种复制在原输入中选择父授权初始化并看到结果，语义由真实API承担")]
    public async Task NativeWorkspaceFolderParentAuthorization_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-folder-parent-authorization");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                token.ThrowIfCancellationRequested();
                var driver = new WorkspacePageDriver(page);
                var source = "父授权源 " + runId[..8];
                await driver.OpenAsync();
                await driver.SelectTreePathAsync("公共空间", fixture.ExistingFolderName);
                await driver.CreateFolderAsync(source, copyParentAuthorization: false);
                await driver.SelectTreePathAsync("公共空间", fixture.ExistingFolderName, source);
                await driver.CopyFolderFromTreeContextMenuAsync(source, selectedOnly: true);
                await driver.SelectTreePathAsync("公共空间", fixture.ExistingFolderName);
                await driver.PasteFolderCopyAsync("父授权浅副本 " + runId[..8], copyParentAuthorization: true);
                await driver.PasteFolderStructureAsync("父授权结构 " + runId[..8],
                    capture: () => AcceptancePresentation.CaptureAsync(page, Path.Combine(root, "parent-authorization-input.png")), copyParentAuthorization: true);
                // 新行随本轮受保护schema由既有fixture清理，不增加删除UI旅程。
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { url = page.Url, journey = "folder-parent-authorization", actor = "ordinary" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "两种文件夹副本使用同一固定发行物旅程和正式driver完成选源到结果的最短路径")]
    public async Task NativeWorkspaceFolderCopy_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-folder-copy");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                await driver.OpenAsync(); await driver.SelectTreePathAsync("公共空间", fixture.ExistingFolderName);
                await driver.CopyFolderFromTreeContextMenuAsync(fixture.ExistingFolderName, selectedOnly: true);
                await driver.SelectRootAsync("公共空间");
                var copyTarget = "副本目标 " + runId[..8];
                await driver.CreateFolderAsync(copyTarget);
                await driver.SelectTreePathAsync("公共空间", copyTarget);
                await driver.PasteFolderCopyAsync("浅副本 " + runId[..8], AcceptancePresentation.Enabled ? async () =>
                {
                    await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, "copy-input-zhs.png"));
                    await page.SetViewportSizeAsync(900, 1000);
                    await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, "copy-input-zhs-narrow.png"));
                    await page.SetViewportSizeAsync(1440, 1000);
                } : null);
                await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, "copy-result.png"));
                await driver.PasteFolderStructureAsync("结构副本 " + runId[..8], AcceptancePresentation.Enabled ? async () =>
                {
                    await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, "structure-input-zhs.png"));
                    await page.SetViewportSizeAsync(900, 1000);
                    await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, "structure-input-zhs-narrow.png"));
                    await page.SetViewportSizeAsync(1440, 1000);
                } : null);
                await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, "structure-result.png"));
                if (AcceptancePresentation.Enabled)
                {
                    await driver.SelectLanguageAsync("English");
                    await new StandardCrudPageDriver(page).ClickPageActionAsync("Paste folder copy");
                    var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Paste folder copy", Exact = true });
                    await Assertions.Expect(dialog.GetByRole(AriaRole.Textbox, new() { Name = "New folder name", Exact = true })).ToBeEnabledAsync();
                    await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, "copy-input-en.png"));
                    await page.SetViewportSizeAsync(900, 1000);
                    await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, "copy-input-en-narrow.png"));
                    await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
                    await page.SetViewportSizeAsync(1440, 1000);
                    await new StandardCrudPageDriver(page).ClickPageActionAsync("Paste folder structure");
                    dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Paste folder structure", Exact = true });
                    await Assertions.Expect(dialog.GetByRole(AriaRole.Textbox, new() { Name = "New folder name", Exact = true })).ToBeEnabledAsync();
                    await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, "structure-input-en.png"));
                    await page.SetViewportSizeAsync(900, 1000);
                    await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, "structure-input-en-narrow.png"));
                    await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
                    await driver.SelectLanguageAsync("简体中文");
                }
                await driver.RemoveSelectedReferencesAsync(["浅副本 " + runId[..8], "结构副本 " + runId[..8]]);
                await driver.SelectRootAsync("公共空间");
                await driver.DeleteSelectedAsync(copyTarget);
                await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, "copy-target-deleted.png"));
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通账号定位已有零组件，编辑后通过正式地址重新读取")]
    public Task NativeWorkspacePartEdit_UsesFormalDriver() => RunWorkspaceEditAsync("part");

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通账号定位已有图文档，编辑后通过正式地址重新读取")]
    public Task NativeWorkspaceDocumentEdit_UsesFormalDriver() => RunWorkspaceEditAsync("document");

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通账号定位已有文件夹，编辑后通过正式地址重新读取")]
    public Task NativeWorkspaceFolderEdit_UsesFormalDriver() => RunWorkspaceEditAsync("folder");

    // 每种对象独立运行最短编辑路径，复用当前发行物种子与同一普通账号。
    private async Task RunWorkspaceEditAsync(string kind)
    {
        var (root, runId) = PrepareRun("native-workspace-" + kind + "-edit");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                var labels = kind == "folder" ? new[] { "公共空间" } : new[] { "公共空间", fixture.ExistingFolderName };
                var address = kind == "folder" ? page.Url : await driver.SelectTreePathAsync(labels);
                var name = kind switch
                {
                    "part" => fixture.ExistingName,
                    "document" => fixture.ExistingDocumentName,
                    "folder" => fixture.ExistingFolderName,
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                };
                var editedName = name + " 已编辑";
                await driver.EditObjectAsync(name, editedName);
                await driver.RestorePositionAsync(address, labels);
                await new StandardCrudPageDriver(page).WaitForRowAsync(editedName);
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { url = page.Url, actor = "ordinary", journey = kind + "-edit" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通账号通过固定发行物的树和列表右键菜单编辑真实文件夹与图文档")]
    public async Task NativeWorkspaceContextMenus_UseFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-context-menus");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                Task CaptureAsync(string name) => AcceptancePresentation.CaptureAsync(page, Path.Combine(root, name));
                var folder = "菜单文件夹 " + runId[..8];
                var document = "菜单图文档 " + runId[..8];
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                await driver.CreateFolderAsync(folder);
                await driver.ExpandRootToFolderAsync("公共空间", folder);
                await driver.SelectTreePathAsync("公共空间", folder);
                await driver.CreateDocumentWithFilesAsync("MENU-D-" + runId[..8], document, WorkspacePageDriver.SmallMainFile());
                await driver.EditObjectFromContextMenuAsync(document, document + " 已编辑", AcceptancePresentation.Enabled ? () => CaptureAsync("list-context-menu.png") : null);
                await driver.EditFolderFromTreeContextMenuAsync(folder, folder + " 已编辑", AcceptancePresentation.Enabled ? () => CaptureAsync("tree-context-menu.png") : null);
                await CaptureAsync("context-menu-saved.png");
                if (AcceptancePresentation.Enabled)
                {
                    await page.SetViewportSizeAsync(900, 1000);
                    await CaptureAsync("context-menu-saved-narrow.png");
                }
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { url = page.Url, actor = "ordinary", journey = "workspace-context-menus" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通账号在固定发行物中通过正式树菜单单选上下移并看到刷新")]
    public async Task NativeWorkspaceTreeOrder_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-tree-order");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                Task CaptureAsync(string name) => AcceptancePresentation.CaptureAsync(page, Path.Combine(root, name));
                var previous = "排序相邻文件夹 " + runId[..8];
                var target = "排序目标文件夹 " + runId[..8];
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                await driver.CreateFolderAsync(previous);
                await driver.CreateFolderAsync(target);
                await driver.ExpandRootToFolderAsync("公共空间", target);
                await driver.ReorderFolderAsync(target, "up", AcceptancePresentation.Enabled ? () => CaptureAsync("tree-order-menu.png") : null);
                await driver.ReorderFolderAsync(target, "down");
                await CaptureAsync("tree-order-saved.png");
                if (AcceptancePresentation.Enabled)
                {
                    await page.SetViewportSizeAsync(900, 1000);
                    await CaptureAsync("tree-order-saved-narrow.png");
                }
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { url = page.Url, actor = "ordinary", journey = "workspace-tree-order" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通账号通过正式声明接通树复制、异构选择、剪切粘贴和直接移除")]
    public async Task WorkspaceClipboardExecution_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("workspace-clipboard-execution");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var requests = new System.Collections.Concurrent.ConcurrentQueue<object>();
                page.Request += (_, request) =>
                {
                    var path = new Uri(request.Url).AbsolutePath;
                    if (request.Method is not ("POST" or "DELETE") || !path.StartsWith("/api/workspace/content/", StringComparison.Ordinal)) return;
                    request.Headers.TryGetValue("x-newp-data-declaration", out var declaration);
                    request.Headers.TryGetValue("x-newp-feature", out var feature);
                    requests.Enqueue(new { request.Method, path, declaration, feature });
                };
                var driver = new WorkspacePageDriver(page);
                var source = "声明来源 " + runId[..8];
                var target = "声明复制目标 " + runId[..8];
                var moved = "声明移动目标 " + runId[..8];
                var folder = "声明树文件夹 " + runId[..8];
                var part = "声明零组件 " + runId[..8];
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                foreach (var name in new[] { source, target, moved }) await driver.CreateFolderAsync(name);
                await driver.SelectTreePathAsync("公共空间", source);
                await driver.CreateFolderAsync(folder);
                await driver.CreateObjectAsync("零组件", "DECL-P-" + runId[..8], part);
                await driver.SelectTreePathAsync("公共空间", source, folder);
                await driver.CopyFolderFromTreeContextMenuAsync(folder, selectedOnly: true);
                await driver.SelectTreePathAsync("公共空间", target);
                await driver.PasteReferencesAsync(folder);
                await driver.RemoveReferenceAsync(folder);
                await driver.SelectTreePathAsync("公共空间", source);
                await driver.CopySelectedAsync(folder, part);
                await driver.SelectTreePathAsync("公共空间", target);
                await driver.PasteReferencesAsync(folder, part);
                await driver.CutSelectedAsync(folder, part);
                await driver.SelectTreePathAsync("公共空间", moved);
                await driver.PasteCutReferencesAsync(folder, part);
                await driver.RemoveSelectedReferencesAsync([folder, part]);
                await driver.SelectTreePathAsync("公共空间", source, folder);
                await driver.CutFolderFromTreeContextMenuAsync(folder, selectedOnly: true);
                await driver.SelectTreePathAsync("公共空间", target);
                await driver.PasteCutReferencesAsync(folder);
                await driver.RemoveReferenceAsync(folder);
                await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, "clipboard-execution.png"));
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { url = page.Url, actor = "ordinary", journey = "workspace-clipboard-execution", declarationRequests = requests.ToArray() }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通账号通过正式树/行菜单和当前页工具栏复制并连续粘贴引用")]
    public async Task NativeWorkspaceCopyPaste_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-copy-paste");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                Task CaptureAsync(string name) => AcceptancePresentation.CaptureAsync(page, Path.Combine(root, name));
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
                await driver.CopyFolderFromTreeContextMenuAsync(copiedFolder, AcceptancePresentation.Enabled ? () => CaptureAsync("clipboard-tree-menu.png") : null);
                await driver.SelectTreePathAsync("公共空间", targetB);
                await driver.PasteReferencesAsync(copiedFolder);
                await driver.SelectTreePathAsync("公共空间", source);
                await driver.CreateDocumentWithFilesAsync("COPY-D-" + runId[..8], document, WorkspacePageDriver.SmallMainFile(),
                    attachments: WorkspacePageDriver.SmallAttachmentFiles());
                await driver.CreateObjectAsync("零组件", "COPY-P-" + runId[..8], part);
                await driver.CopyReferenceFromContextMenuAsync(document, AcceptancePresentation.Enabled ? () => CaptureAsync("clipboard-row-menu.png") : null);
                await driver.SelectTreePathAsync("公共空间", targetB);
                await driver.PasteReferencesAsync(document);
                await driver.SelectTreePathAsync("公共空间", targetC);
                await driver.PasteReferencesAsync(document);
                await driver.SelectTreePathAsync("公共空间", source);
                await driver.CopySelectedAsync(part);
                await driver.SelectTreePathAsync("公共空间", targetC);
                await driver.PasteReferencesAsync(part);
                await CaptureAsync("clipboard-pasted.png");
                if (AcceptancePresentation.Enabled)
                {
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
                }
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { url = page.Url, actor = "ordinary", journey = "workspace-copy-paste" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通账号通过正式树和工具栏剪切移动、确认批量移除引用并删除对象")]
    public async Task NativeWorkspaceCutAndRemove_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-cut-remove");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                Task CaptureAsync(string name) => AcceptancePresentation.CaptureAsync(page, Path.Combine(root, name));
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
                await driver.CutFolderFromTreeContextMenuAsync(folder, AcceptancePresentation.Enabled ? () => CaptureAsync("cut-tree-menu.png") : null);
                await driver.SelectTreePathAsync("公共空间", targetB);
                await CaptureAsync("cut-pending.png");
                await driver.PasteCutReferencesAsync(folder);
                await driver.SelectTreePathAsync("公共空间", targetB, folder);
                await driver.CutSelectedAsync(document, part);
                await driver.SelectTreePathAsync("公共空间", targetC);
                await driver.PasteCutReferencesAsync(document, part);
                await CaptureAsync("cut-pasted.png");
                await driver.RemoveSelectedReferencesAsync([document, part], AcceptancePresentation.Enabled ? async dialog =>
                {
                    await dialog.EvaluateAsync("async element => { await new Promise(requestAnimationFrame); await Promise.all(element.getAnimations({subtree:true}).map(animation => animation.finished.catch(() => {}))); }");
                    await CaptureAsync("remove-references-confirm.png");
                } : null);
                var deletedFolder = "工具栏删除验证 " + runId[..8];
                await driver.CreateFolderAsync(deletedFolder);
                await driver.DeleteSelectedAsync(deletedFolder);
                if (AcceptancePresentation.Enabled)
                {
                    var localizedRead = page.WaitForResponseAsync(response => response.Request.Method == "GET"
                        && new Uri(response.Url).AbsolutePath == "/api/links/workspace.content");
                    await driver.SelectLanguageAsync("English");
                    var localizedResponse = await localizedRead;
                    if (!localizedResponse.Ok) throw new InvalidOperationException($"切换语言后的列表读取失败：{localizedResponse.Status}");
                    await localizedResponse.FinishedAsync();
                    await page.GetByRole(AriaRole.Button, new() { Name = "Remove", Exact = true })
                        .WaitForAsync(new() { State = WaitForSelectorState.Visible });
                    await Assertions.Expect(page.Locator(".np-tree-content .ant-spin-spinning")).ToHaveCountAsync(0);
                    await CaptureAsync("references-removed-en.png");
                }
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { url = page.Url, actor = "ordinary", journey = "workspace-cut-remove" }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact("NEWP_RUN_NATIVE_WORKSPACE_ACCEPTANCE", "普通账号搜索全部可读出现路径并使用正式driver定位同名文件夹")]
    public async Task NativeWorkspaceSearch_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("native-workspace-search");
        var fixture = PublishedComponentAcceptanceFixture.FromWorkspaceDelivery(RepositoryRoot, root, runId,
            exerciseCompositionLifecycle: false, preparation: PublishedWorkspacePreparation.CurrentRelease);
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false, publishedFixture: fixture,
            browserJourney: async (page, token) =>
            {
                var driver = new WorkspacePageDriver(page);
                Task CaptureAsync(string name) => AcceptancePresentation.CaptureAsync(page, Path.Combine(root, name));
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
                    AcceptancePresentation.Enabled ? () => CaptureAsync("search-distinct-paths.png") : null);
                await new StandardCrudPageDriver(page).WaitForRowAsync(fixture.ExistingDocumentName);
                await CaptureAsync("search-located-content.png");
                // 两条同名出现位置已展开；正式动作明确使用搜索选中的那条。
                await driver.CopyFolderFromTreeContextMenuAsync(folder, selectedOnly: true);
                if (AcceptancePresentation.Enabled)
                {
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
                }
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
                    if (!AcceptancePresentation.Enabled) return;
                    // Modal 外容器先可见，等待其内容动画完成后才能留下可核对的截图。
                    if (surface is not null) await surface.EvaluateAsync("async element => { await new Promise(requestAnimationFrame); await Promise.all(element.getAnimations({subtree:true}).map(animation => animation.finished.catch(() => {}))); }");
                    await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, stage + ".png"));
                    await page.SetViewportSizeAsync(900, 1000);
                    await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, stage + "-narrow.png"));
                    await page.SetViewportSizeAsync(1440, 1000);
                }
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                await driver.CreateFolderAsync(name);
                await driver.ExpandRootToFolderAsync("公共空间", name);
                await CapturePairAsync("folder-reference-list");
                await driver.RemoveReferenceAsync(name, AcceptancePresentation.Enabled ? dialog => CapturePairAsync("remove-reference", dialog) : null);
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
                    if (!AcceptancePresentation.Enabled) return;
                    if (surface is not null) await surface.EvaluateAsync("async element => { await new Promise(requestAnimationFrame); await Promise.all(element.getAnimations({subtree:true}).map(animation => animation.finished.catch(() => {}))); }");
                    await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, stage + ".png"));
                    await page.SetViewportSizeAsync(900, 1000);
                    await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, stage + "-narrow.png"));
                    await page.SetViewportSizeAsync(1440, 1000);
                }
                await driver.OpenAsync();
                await driver.SelectRootAsync("公共空间");
                var folderAddress = await driver.SelectTreePathAsync("公共空间", folderName);
                await driver.CreateFolderAsync(folderName + " 子文件夹");
                await driver.CreateObjectAsync("零组件", partCode, partName, AcceptancePresentation.Enabled ? dialog => CapturePairAsync("part-create", dialog) : null);
                var attachments = WorkspacePageDriver.SmallAttachmentFiles();
                await driver.CreateDocumentWithFilesAsync(documentCode, documentName, WorkspacePageDriver.SmallMainFile(), AcceptancePresentation.Enabled ? dialog => CapturePairAsync("document-create", dialog) : null, attachments);
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
                await driver.RemoveReferenceAsync(partName, AcceptancePresentation.Enabled ? dialog => CapturePairAsync("mixed-remove-reference", dialog) : null);
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
                    if (!AcceptancePresentation.Enabled) return;
                    await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, stage + ".png"));
                    await page.SetViewportSizeAsync(900, 1000);
                    await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, stage + "-narrow.png"));
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
                    if (!AcceptancePresentation.Enabled) return;
                    await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, stage + ".png"));
                    await page.SetViewportSizeAsync(900, 1000);
                    await AcceptancePresentation.CaptureAsync(page, Path.Combine(root, stage + "-narrow.png"));
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
                Task CaptureAsync(string name) => AcceptancePresentation.CaptureAsync(page, Path.Combine(root, name));
                await driver.OpenAsync();
                await CaptureAsync("part-existing-list.png");
                var oldProperties = await driver.OpenPropertiesAsync(fixture.ExistingCode, fixture.ExistingName);
                await CaptureAsync("part-existing-properties.png");
                if (AcceptancePresentation.Enabled)
                {
                    await page.SetViewportSizeAsync(900, 1000);
                    await CaptureAsync("part-existing-properties-narrow.png");
                    await page.SetViewportSizeAsync(1440, 1000);
                }
                await driver.OpenFullDetailAsync(oldProperties, fixture.ExistingName);
                await CaptureAsync("part-existing-detail.png");
                if (AcceptancePresentation.Enabled)
                {
                    await page.SetViewportSizeAsync(900, 1000);
                    await CaptureAsync("part-existing-detail-narrow.png");
                    await page.SetViewportSizeAsync(1440, 1000);
                }
                await driver.OpenAsync();
                var create = await driver.OpenCreateAsync();
                await CaptureAsync("part-create.png");
                if (AcceptancePresentation.Enabled)
                {
                    await page.SetViewportSizeAsync(900, 1000);
                    await CaptureAsync("part-create-narrow.png");
                    await page.SetViewportSizeAsync(1440, 1000);
                }
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
        Task CaptureAsync(string file) => AcceptancePresentation.CaptureAsync(page, Path.Combine(root, file));
        await driver.OpenAsync();
        var form = await driver.OpenCreateAsync();
        await CaptureAsync($"{prefix}-create.png");
        if (AcceptancePresentation.Enabled)
        {
            await page.SetViewportSizeAsync(900, 1000);
            await CaptureAsync($"{prefix}-create-narrow.png");
            await page.SetViewportSizeAsync(1440, 1000);
        }
        await driver.SaveCreateAsync(form, code, name);
        await CaptureAsync($"{prefix}-list.png");
        if (AcceptancePresentation.Enabled)
        {
            await page.SetViewportSizeAsync(900, 1000);
            await CaptureAsync($"{prefix}-list-narrow.png");
            await page.SetViewportSizeAsync(1440, 1000);
        }
        var properties = await driver.OpenPropertiesAsync(code, name);
        await CaptureAsync($"{prefix}-properties.png");
        if (AcceptancePresentation.Enabled)
        {
            await page.SetViewportSizeAsync(900, 1000);
            await CaptureAsync($"{prefix}-properties-narrow.png");
            await page.SetViewportSizeAsync(1440, 1000);
        }
        await driver.OpenFullDetailAsync(properties, name);
        await CaptureAsync($"{prefix}-detail.png");
        if (AcceptancePresentation.Enabled)
        {
            await page.SetViewportSizeAsync(900, 1000);
            await CaptureAsync($"{prefix}-detail-narrow.png");
        }
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
