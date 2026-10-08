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
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 Link 迁移、普通账号创建文件夹与共享树/列表/属性 driver")]
    public async Task WorkspaceFolderCreation_UsesFormalDriver()
    {
        var (root, runId) = PrepareRun("workspace-folder");
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "个人来源孤立 Folder 的正式 API 准备与公共空间添加/移除/候选重加 driver")]
    public async Task WorkspaceReferences_UseFormalDriver()
    {
        var (root, runId) = PrepareRun("workspace-references");
        var name = $"来源文件夹旅程 {runId[..8]}";
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId, includeSpaceLifecycle: false,
            selectFeatures: features =>
            {
                var entry = features.Behavior("Workspace", "workspace.spaces.access");
                return [entry, features.Action(entry, "create", "list.pageActions", "folder"),
                    features.Action(entry, "add", "list.pageActions", "folder"), features.Action(entry, "remove", "list.rowActions", "folder")];
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
                await driver.AddExistingFolderAsync(name, dialog => CapturePairAsync("folder-candidate", dialog));
                await driver.ExpandRootToFolderAsync("公共空间", name);
                await CapturePairAsync("folder-reference-list");
                await driver.RemoveReferenceAsync(name, dialog => CapturePairAsync("remove-reference", dialog));
                await driver.AddExistingFolderAsync(name);
                await driver.ExpandRootToFolderAsync("公共空间", name);
                var properties = await driver.OpenFolderPropertiesAsync(name);
                await CapturePairAsync("shared-folder-properties");
                await driver.ClosePropertiesAsync(properties);
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "browser.json"),
                    new { url = page.Url, actor = "ordinary", readdedFolder = name }, token);
            }).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "普通账号三类型混合内容、所属属性及独立候选移除重加")]
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
                    features.Action(entry, "add", "list.pageActions", "folder"), features.Action(entry, "remove", "list.rowActions", "folder"),
                    part, features.Action(part, "create", "list.pageActions"), document, features.Action(document, "create", "list.pageActions")];
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
                    new { position, target = new { modelKey = "Workspace/WorkspaceFolder", id = folderId } },
                    features.Action(entry, "add", "list.pageActions", "folder").Position, token);
                var partId = await WorkspaceAcceptanceFixture.CreateMetadataObjectAsync(ordinary, features, "Part", "part.part.read", "/api/part/parts", partCode, partName, token);
                var documentId = await WorkspaceAcceptanceFixture.CreateMetadataObjectAsync(ordinary, features, "Document", "document.document.read", "/api/document/documents", documentCode, documentName, token);
                await AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(root, "fixture-mixed.json"), new { folderId, partId, documentId, actorId = actor.AccountId }, token);
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
                await driver.AddExistingObjectAsync("零组件", partName, $"{partCode} - {partName}", dialog => CapturePairAsync("part-candidate", dialog));
                await driver.AddExistingObjectAsync("图文档", documentName, $"{documentCode} - {documentName}", dialog => CapturePairAsync("document-candidate", dialog));
                await new StandardCrudPageDriver(page).WaitForRowAsync(folderName);
                await CapturePairAsync("mixed-content");
                await driver.OpenOwnedPropertiesAsync(partName, "/parts");
                await CapturePairAsync("mixed-part-properties");
                await driver.ReturnToPublicContentAsync();
                await driver.OpenOwnedPropertiesAsync(documentName, "/documents");
                await CapturePairAsync("mixed-document-properties");
                await driver.ReturnToPublicContentAsync();
                await driver.RemoveReferenceAsync(partName, dialog => CapturePairAsync("mixed-remove-reference", dialog));
                await driver.AddExistingObjectAsync("零组件", partName, $"{partCode} - {partName}");
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
                    features.Action(entry, "add", "list.pageActions", "folder"), features.Action(entry, "remove", "list.rowActions", "folder"),
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
