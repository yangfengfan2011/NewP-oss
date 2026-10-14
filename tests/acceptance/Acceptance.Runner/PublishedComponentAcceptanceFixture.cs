using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICP.Platform.Testing;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.PluginPlatform.Lifecycle;
using Microsoft.Data.SqlClient;

namespace ICP.Platform.Acceptance;

internal sealed record PublishedWorkspaceTarget(WorkspaceAcceptanceActor Actor, string ShellOrigin, int HostPort, string? EntryPosition);

internal sealed record WorkspaceDeliveryInput(JsonElement Candidate, string BaselineHostRoot, string HostRoot,
    string ShellRoot, string PackagePath, string HostIdentity, string ShellIdentity, string PackageSha256);

// 正式 baseline 建立旧数据；裁剪的固定发行物通过原生包消费同一 catalog。
internal sealed partial class PublishedComponentAcceptanceFixture(
    string repositoryRoot, string runDirectory, string runId,
    string baselineHostRoot, string hostRoot, string shellRoot, string packagePath,
    bool workspaceDelivery = false, WorkspaceDeliveryInput? deliveryInput = null, bool exerciseCompositionLifecycle = true,
    bool exerciseUninstallRecovery = false, bool exerciseFileRecovery = false, bool exerciseFileRecoveryMechanisms = true)
{
    private readonly Dictionary<string, string?> _environment = new();
    private ReusableSqlServerDatabase? _database;
    private OwnedProcess? _host;
    private OwnedProcess? _shell;
    private string? _ownership;
    private Func<string?, string> _sanitize = value => value ?? string.Empty;
    private int _hostPort;
    private string Resources => Path.Combine(runDirectory, "resources");
    private string InstallRoot => Path.Combine(Resources, "plugins");
    private string StorageRoot => Path.Combine(Resources, "storage");
    private string HostOrigin => $"http://127.0.0.1:{_hostPort}";
    internal string ExistingCode => "ACC-OLD-P-" + runId[..8];
    internal string ExistingName => "独立安装旧零组件 " + runId[..8];
    internal string ExistingFolderName => "归组旧文件夹 " + runId[..8];
    private string ExistingDocumentCode => "ACC-OLD-D-" + runId[..8];
    private string ExistingDocumentName => "归组旧图文档 " + runId[..8];
    private string? _oldDocumentId;
    private string? _oldFolderId;
    private string? _baselineOwners;
    private WorkspaceAcceptanceActor? _administrator;
    internal string HostIdentity => $"published:{_host?.Process.Id}:{runId}";

    internal static PublishedComponentAcceptanceFixture FromWorkspaceDelivery(string repositoryRoot, string runDirectory, string runId,
        bool exerciseCompositionLifecycle = true, bool exerciseUninstallRecovery = false, bool exerciseFileRecovery = false, bool exerciseFileRecoveryMechanisms = true)
    {
        var path = RequiredPath("NEWP_ACCEPTANCE_WORKSPACE_DELIVERY_INPUT");
        var input = JsonSerializer.Deserialize<WorkspaceDeliveryInput>(File.ReadAllText(path), AcceptanceJson.Options)
            ?? throw new InvalidOperationException("Workspace delivery input was empty.");
        return new(repositoryRoot, runDirectory, runId, input.BaselineHostRoot, input.HostRoot,
            input.ShellRoot, input.PackagePath, workspaceDelivery: true, deliveryInput: input,
            exerciseCompositionLifecycle: exerciseCompositionLifecycle, exerciseUninstallRecovery: exerciseUninstallRecovery,
            exerciseFileRecovery: exerciseFileRecovery, exerciseFileRecoveryMechanisms: exerciseFileRecoveryMechanisms);
    }

    // 只登记受控构建的输入与来源；不产出裁决，也不授予包信任。Gate 仍走正式安装/Host。
    internal static async Task RecordWorkspaceDeliveryAsync(string repositoryRoot, string baselineHostRoot,
        string hostRoot, string shellRoot, string packagePath, string output, CancellationToken token)
    {
        var fixture = new PublishedComponentAcceptanceFixture(repositoryRoot, Path.GetDirectoryName(output)!, "input",
            Path.GetFullPath(baselineHostRoot), Path.GetFullPath(hostRoot), Path.GetFullPath(shellRoot), Path.GetFullPath(packagePath), true);
        var identity = fixture.ReadTargetIdentity();
        var candidate = await ReadCandidateAsync(repositoryRoot, token);
        await AcceptanceInfrastructure.WriteJsonAsync(output, new WorkspaceDeliveryInput(candidate, Path.GetFullPath(baselineHostRoot),
            Path.GetFullPath(hostRoot), Path.GetFullPath(shellRoot), Path.GetFullPath(packagePath), identity.Host, identity.Shell, FileHash(packagePath)), token);
    }

    internal static PublishedComponentAcceptanceFixture FromEnvironment(string repositoryRoot, string runDirectory, string runId) => new(
        repositoryRoot, runDirectory, runId,
        RequiredPath("NEWP_ACCEPTANCE_NATIVE_BASELINE_HOST"), RequiredPath("NEWP_ACCEPTANCE_NATIVE_HOST"),
        RequiredPath("NEWP_ACCEPTANCE_NATIVE_SHELL"), RequiredPath("NEWP_ACCEPTANCE_NATIVE_PACKAGE"));

    private static string RequiredPath(string variable) => Path.GetFullPath(Environment.GetEnvironmentVariable(variable)
        ?? throw new InvalidOperationException($"固定发行物验收需要 {variable}。"));

    // 真实 baseline 先建立业务；Document 按已确认开发清理前提升级，其余 owner 保持旧事实。
    internal async Task<PublishedWorkspaceTarget> PrepareAsync(Action<string> registerSecret,
        Action<string, string?> addConnectionSecrets, Func<string?, string> sanitize, CancellationToken token)
    {
        _sanitize = sanitize;
        var before = ReadTargetIdentity();
        if (deliveryInput is not null)
        {
            var actual = await ReadCandidateAsync(repositoryRoot, token);
            if (!JsonElement.DeepEquals(deliveryInput.Candidate, actual) || deliveryInput.HostIdentity != before.Host
                || deliveryInput.ShellIdentity != before.Shell || deliveryInput.PackageSha256 != FileHash(packagePath))
            {
                await WriteAsync("delivery-input-mismatch.json", new { expectedCandidate = deliveryInput.Candidate, actualCandidate = actual,
                    hostEqual = deliveryInput.HostIdentity == before.Host, shellEqual = deliveryInput.ShellIdentity == before.Shell,
                    packageEqual = deliveryInput.PackageSha256 == FileHash(packagePath) }, token);
                throw new InvalidOperationException("Workspace delivery 输入与当前候选或固定发行资产不一致；读取 delivery-input-mismatch.json。");
            }
        }
        await WriteAsync("target-before.json", before, token);
        await WriteCandidateAsync("candidate-before.json", token);
        _database = await ReusableSqlServerDatabase.AcquireAsync(repositoryRoot, token);
        addConnectionSecrets(_database.ConnectionString, new SqlConnectionStringBuilder(_database.ConnectionString).Password);
        Directory.CreateDirectory(Resources);
        _environment["ConnectionStrings__SqlServer"] = _database.ConnectionString;
        _environment["Platform__PluginInstallRoot"] = InstallRoot;
        _environment["Platform__ExternalPluginsEnabled"] = "false";
        _environment["Platform__FrontendPluginBaseUrl"] = "/plugins/runtime/assets";
        _environment["Authentication__Host__KeyRingPath"] = Path.Combine(Resources, "key-ring");
        _environment["Files__StorageRoot"] = StorageRoot;
        _environment["Authentication__Host__CookieName"] = WorkspaceAcceptanceFixture.CookieName;
        _environment["Authentication__Host__AllowInsecureDevelopmentCookie"] = "true";
        _environment["Identity__LocalAdministrator__ECODE"] = "admin";
        _environment["ASPNETCORE_ENVIRONMENT"] = "Acceptance";
        var bootstrap = await FormalIdentityBootstrap.RunPublishedAsync(repositoryRoot,
            Path.Combine(baselineHostRoot, "HostApi.dll"), baselineHostRoot,
            Path.Combine(Resources, "bootstrap"), _environment, token);
        registerSecret(bootstrap.TemporaryCredential);
        _ownership = await ReadSqlJsonAsync("SELECT * FROM dbo.__NewPDatabaseOwnership FOR JSON PATH;", null, token);
        _hostPort = ReservePort();
        await StartHostAsync(baselineHostRoot, "baseline-host", token);
        using var administrator = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        var administratorPassword = "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        registerSecret(administratorPassword);
        await WorkspaceAcceptanceFixture.LoginAndChangePasswordAsync(administrator, "admin", bootstrap.TemporaryCredential,
            administratorPassword, token);
        _administrator = new("local-admin", "admin", administratorPassword);
        var features = await AcceptanceFeatureCatalog.LoadAsync(administrator, token);
        var part = features.Behavior("Part", "part.part.read");
        var granted = new List<FeaturePoint> { part, features.Action(part, "create", "list.pageActions") };
        FeaturePoint? workspace = null;
        if (workspaceDelivery)
        {
            workspace = features.Behavior("Workspace", "workspace.spaces.access");
            granted.Add(workspace);
            foreach (var key in new[] { "create", "createPart", "createDocument", "deleteObjects" })
                granted.Add(features.Action(workspace, key, "list.pageActions", "folder"));
            foreach (var key in new[] { "edit", "delete", "remove" })
                granted.Add(features.Action(workspace, key, "list.rowActions", "folder"));
            var document = features.Behavior("Document", "document.document.read");
            granted.Add(document);
            granted.Add(features.Action(document, "create", "list.pageActions"));
            await WorkspaceAcceptanceFixture.SendAsync(administrator, HttpMethod.Post, "/api/workspace/spaces/public/initialize", null, workspace.Position, token);
        }
        var actor = await WorkspaceAcceptanceFixture.PrepareAsync(administrator, features, runId, registerSecret, granted, token);
        using var ordinary = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        await WorkspaceAcceptanceFixture.LoginAsync(ordinary, actor, token);
        string oldId;
        if (workspaceDelivery)
        {
            await WorkspaceAcceptanceFixture.SendAsync(ordinary, HttpMethod.Post, "/api/workspace/spaces/personal/ensure", null, workspace!.Position, token);
            var roots = await WorkspaceAcceptanceFixture.SendAsync(ordinary, HttpMethod.Get, "/api/workspace/spaces", null, workspace.Position, token);
            var root = roots.EnumerateArray().Single(item => string.Equals(item.GetProperty("spaceKind").GetString(), "public", StringComparison.OrdinalIgnoreCase));
            var position = new WorkspaceFolderPosition(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
            _oldFolderId = (await WorkspaceAcceptanceFixture.CreateFolderAtPositionAsync(ordinary, features, position, ExistingFolderName, token)).FolderId;
            oldId = await CreateWorkspaceObjectAsync(ordinary, features, root, position, "Part", "parts", ExistingCode, ExistingName, token);
            _oldDocumentId = await CreateWorkspaceObjectAsync(ordinary, features, root, position, "Document", "documents", ExistingDocumentCode, ExistingDocumentName, token);
            _baselineOwners = await ReadModelOwnersAsync(ordinary, token);
        }
        else oldId = await WorkspaceAcceptanceFixture.CreateMetadataObjectAsync(ordinary, features,
            "Part", "part.part.read", "/api/part/parts", ExistingCode, ExistingName, token);
        var baseline = await ReadContinuityAsync(oldId, token);
        await WriteAsync("sql-before.json", baseline, token);
        await StopHostAsync(token);

        await RunOfflineAsync("install-native", ["--package", packagePath], token);
        if (workspaceDelivery) await RunOfflineAsync("enable-bundle", ["--package", packagePath], token);
        else
        {
            await RunOfflineAsync("enable", ["--module-id", "InforCenter", "--version", "1.0.0"], token);
            await RunOfflineAsync("enable", ["--module-id", "Part", "--version", "1.0.0"], token);
        }
        if (workspaceDelivery)
        {
            await VerifyDocumentUpgradeGuardAsync(oldId, baseline, token);
            await ClearOwnedLegacyDocumentAsync(token);
            baseline = await ReadContinuityAsync(oldId, token);
            await WriteAsync("sql-after-explicit-document-cleanup.json", baseline, token);
        }
        await StartHostAsync(hostRoot, "installed-host", token);
        var after = await ReadContinuityAsync(oldId, token);
        await WriteAsync("sql-after.json", after, token);
        VerifyPreservedPredecessor(baseline, after, "Document", "Files");
        using var installed = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        await WorkspaceAcceptanceFixture.LoginAsync(installed, actor, token);
        var installedFeatures = await AcceptanceFeatureCatalog.LoadAsync(installed, token);
        if (workspaceDelivery)
        {
            granted.Add(installedFeatures.Behavior("Document", "document.document.download"));
            VerifyDocumentModelEvolution(_baselineOwners!, await ReadModelOwnersAsync(installed, token));
            _baselineOwners = await ReadModelOwnersAsync(installed, token);
            var roots = await WorkspaceAcceptanceFixture.SendAsync(installed, HttpMethod.Get, "/api/workspace/spaces", null,
                installedFeatures.Behavior("Workspace", "workspace.spaces.access").Position, token);
            var root = roots.EnumerateArray().Single(item => string.Equals(item.GetProperty("spaceKind").GetString(), "public", StringComparison.OrdinalIgnoreCase));
            var position = new WorkspaceFolderPosition(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
            _oldDocumentId = await CreateWorkspaceObjectAsync(installed, installedFeatures, root, position, "Document", "documents", ExistingDocumentCode, ExistingDocumentName, token);
            baseline = await ReadContinuityAsync(oldId, token);
            await WriteAsync("sql-post-upgrade-document.json", baseline, token);
        }
        var detail = await ReadOldPartAsync(installed, installedFeatures, oldId, token);
        var runtime = await WorkspaceAcceptanceFixture.SendAsync(installed, HttpMethod.Get,
            "/plugins/active-module-runtime.json", null, null, token);
        var modules = runtime.GetProperty("modules").EnumerateArray().ToArray();
        var parent = modules.Single(module => module.GetProperty("moduleId").GetString() == "InforCenter");
        var expectedMembers = workspaceDelivery ? new[] { "Document", "Part", "Workspace" } : ["Part"];
        if (!parent.GetProperty("compositionMembers").EnumerateArray().Select(member => member.GetString()).Order(StringComparer.Ordinal).SequenceEqual(expectedMembers))
            throw new InvalidOperationException("首片未使用只含 Part 的正式 InforCenter 包。");
        if (workspaceDelivery)
            await VerifyWorkspaceApiContinuityAsync(installed, installedFeatures, oldId, "workspace-api-continuity.json", token);
        await WriteAsync("api-continuity.json", new { oldId, detail, runtime, externalPluginsEnabled = false }, token);
        if (workspaceDelivery && exerciseUninstallRecovery) await ExerciseUninstallRecoveryAsync(oldId, baseline, token);
        if (workspaceDelivery)
        {
            if (exerciseCompositionLifecycle)
                await VerifyCompositionLifecycleAsync(actor, oldId, installedFeatures.Behavior("Part", "part.part.read").Position, baseline, token);
            // 新 download 位置不来自旧角色授权；生命周期删除后也不重放旧授权，页面使用明确有限的新角色。
            using var pageAdministrator = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
            await WorkspaceAcceptanceFixture.LoginAsync(pageAdministrator, _administrator!, token);
            var pageFeatures = await AcceptanceFeatureCatalog.LoadAsync(pageAdministrator, token);
            var pageWorkspace = pageFeatures.Behavior("Workspace", "workspace.spaces.access");
            foreach (var key in new[] { "edit", "remove" })
                granted.Add(pageFeatures.Action(pageWorkspace, key, "contextMenu", "folder"));
            var previousActorId = actor.AccountId;
            actor = await WorkspaceAcceptanceFixture.PrepareAsync(pageAdministrator, pageFeatures, "page" + runId, registerSecret, granted, token);
            await WriteAsync("page-actor-preparation.json", new { previousActorId, pageActorId = actor.AccountId,
                oldGrantsRestored = false, preparation = "formal-limited-role", grantedPositions = pageFeatures.GrantSet(granted.ToArray()) }, token);
        }
        if (exerciseFileRecovery) await VerifyFileProcessRecoveryAsync(actor, token);
        var targetAfter = ReadTargetIdentity();
        await WriteAsync("target-after.json", targetAfter, token);
        if (before != targetAfter) throw new InvalidOperationException("安装期间固定 Host/Shell 发行物发生变化。");
        var readyFile = Path.Combine(Resources, "shell-ready.json");
        _shell = StartProcess("node", [Path.Combine(repositoryRoot, "scripts", "performance", "production-shell-server.mjs"),
            "--assets-root", shellRoot, "--host-origin", HostOrigin, "--port", "0", "--run-id", runId, "--ready-file", readyFile],
            repositoryRoot, "production-shell");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!File.Exists(readyFile))
        {
            if (_shell.Process.HasExited) throw new InvalidOperationException("固定 Shell 服务在 ready 前退出。");
            await Task.Delay(100, timeout.Token);
        }
        using var ready = JsonDocument.Parse(await File.ReadAllTextAsync(readyFile, token));
        if (ready.RootElement.GetProperty("runId").GetString() != runId || ready.RootElement.GetProperty("pid").GetInt32() != _shell.Process.Id)
            throw new InvalidOperationException("固定 Shell ready 归属不匹配。");
        var shellOrigin = $"http://127.0.0.1:{ready.RootElement.GetProperty("port").GetInt32()}";
        await WriteAsync("initialization.json", new { early271 = !workspaceDelivery, catalog = ReusableSqlServerDatabase.Catalog,
            hostOrigin = HostOrigin, shellOrigin, actorId = actor.AccountId, packagePath,
            packageSha256 = FileHash(packagePath), baselineHostRoot, hostRoot, shellRoot }, token);
        return new(actor, shellOrigin, _hostPort, workspaceDelivery ? installedFeatures.Behavior("Workspace", "workspace.spaces.access").Position : null);
    }

    private async Task<string> CreateWorkspaceObjectAsync(HttpClient client, AcceptanceFeatureCatalog features, JsonElement root,
        WorkspaceFolderPosition position, string model, string route, string code, string name, CancellationToken token)
    {
        var entry = features.Behavior("Workspace", "workspace.spaces.access");
        await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Post, "/api/workspace/content/" + route,
            new { position = position.CommandPosition, values = new { ECODE = code, ENAME = name } },
            features.Action(entry, "create" + model, "list.pageActions", "folder").Position, token);
        var contents = await WorkspaceAcceptanceFixture.ReadRootContentsAsync(client, root, entry.Position, token);
        return contents.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("fields").GetProperty("Name").GetString() == name)
            .GetProperty("associatedObject").GetProperty("id").GetString()!;
    }

    private async Task<string> ReadModelOwnersAsync(HttpClient client, CancellationToken token)
    {
        var runtime = await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Get, "/plugins/active-module-runtime.json", null, null, token);
        return JsonSerializer.Serialize(runtime.GetProperty("modules").EnumerateArray()
            .Where(module => new[] { "Workspace", "Part", "Document", "Files" }.Contains(module.GetProperty("moduleId").GetString()))
            .Select(module => new { owner = module.GetProperty("moduleId").GetString(), models = module.GetProperty("modelKeys").EnumerateArray().Select(key => key.GetString()).Order(StringComparer.Ordinal).ToArray() })
            .OrderBy(module => module.owner, StringComparer.Ordinal));
    }

    private async Task<JsonElement> ReadOldPartAsync(HttpClient client, AcceptanceFeatureCatalog features, string oldId, CancellationToken token)
    {
        var detail = await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Get, "/api/part/parts/" + Uri.EscapeDataString(oldId),
            null, features.Behavior("Part", "part.part.read").Position, token);
        var fields = detail.GetProperty("fields");
        if (fields.GetProperty("EID").GetString() != oldId || fields.GetProperty("ECODE").GetString() != ExistingCode
            || fields.GetProperty("ENAME").GetString() != ExistingName)
            throw new InvalidOperationException("正式 Part API 未返回 baseline 的旧身份与字段。");
        return detail;
    }

    private async Task VerifyWorkspaceApiContinuityAsync(HttpClient client, AcceptanceFeatureCatalog features, string oldId,
        string evidenceName, CancellationToken token)
    {
        if (_baselineOwners != await ReadModelOwnersAsync(client, token)) throw new InvalidOperationException("三成员的模型 owner 发生漂移。");
        var oldDocument = await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Get, "/api/document/documents/" + _oldDocumentId,
            null, features.Behavior("Document", "document.document.read").Position, token);
        if (oldDocument.GetProperty("fields").GetProperty("ECODE").GetString() != ExistingDocumentCode)
            throw new InvalidOperationException("正式 Document API 未读取旧对象。");
        var roots = await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Get, "/api/workspace/spaces", null,
            features.Behavior("Workspace", "workspace.spaces.access").Position, token);
        var root = roots.EnumerateArray().Single(item => string.Equals(item.GetProperty("spaceKind").GetString(), "public", StringComparison.OrdinalIgnoreCase));
        var contents = await WorkspaceAcceptanceFixture.ReadRootContentsAsync(client, root, features.Behavior("Workspace", "workspace.spaces.access").Position, token);
        var ids = contents.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("associatedObject").GetProperty("id").GetString()).Order(StringComparer.Ordinal);
        if (!ids.SequenceEqual(new[] { oldId, _oldDocumentId, _oldFolderId }.Order(StringComparer.Ordinal)))
            throw new InvalidOperationException("三成员旧内容 Link 未由正式 Workspace 查询连续读取。");
        await WriteAsync(evidenceName, new { oldDocument, roots, contents, modelOwners = _baselineOwners }, token);
    }

    // 卸载只退出代码库存；原成功恢复输入和 SQL 前沿继续由正式 Host 的持久化链持有。
    private async Task ExerciseUninstallRecoveryAsync(string oldId, Continuity baseline, CancellationToken token)
    {
        string[] ids = ["InforCenter", "Workspace", "Part", "Document"];
        using var beforeClient = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        var registeredEndpoints = await ReadBusinessEndpointsAsync(beforeClient, token);
        RequireBusinessEndpoints(registeredEndpoints);
        await StopHostAsync(token);
        var retained = ReadSuccessfulAssets().Concat(ReadFiles(Path.Combine(InstallRoot, "native-receipts"))).ToArray();
        var installedBefore = ReadInstalledIds();
        var before = HashTree(InstallRoot, _ => true);
        await WriteAsync("uninstall-before.json", new { installed = installedBefore, registeredEndpoints, retainedAssets = retained, fileIdentity = before }, token);
        var partSelection = Path.Combine(Resources, "uninstall-part.json");
        await AcceptanceInfrastructure.WriteJsonAsync(partSelection, new[] { "Part" }, token);
        await RunOfflineAsync("uninstall", ["--file", partSelection], token, "MODULE_COMPOSITION_MEMBER_MISSING");
        if (HashTree(InstallRoot, _ => true) != before)
            throw new InvalidOperationException("拒绝不完整卸载集合时改变了安装根的代码、选择、活动或持久化字节。");
        await WriteAsync("uninstall-rejected.json", new { code = "MODULE_COMPOSITION_MEMBER_MISSING", fileIdentityUnchanged = true }, token);

        var selection = Path.Combine(Resources, "uninstall-components.json");
        await AcceptanceInfrastructure.WriteJsonAsync(selection, ids, token);
        await RunOfflineAsync("uninstall", ["--file", selection], token);
        foreach (var id in ids)
            if (File.Exists(Path.Combine(InstallRoot, "state", id + ".json"))
                || Directory.Exists(Path.Combine(InstallRoot, "packages", id)) || Directory.Exists(Path.Combine(InstallRoot, ".pruned", "packages", id)))
                throw new InvalidOperationException($"显式卸载后 {id} 的代码或库存仍然存在。");
        var installed = ReadInstalledIds();
        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(InstallRoot, "lifecycle", "v2", "state.json")));
        if (!installed.SequenceEqual(installedBefore.Where(id => !ids.Contains(id)))
            || state.RootElement.GetProperty("desiredModules").EnumerateArray().Any(module => ids.Contains(module.GetProperty("moduleId").GetString())))
            throw new InvalidOperationException("保留的恢复材料被当作已安装库存或 desired 选择。");
        await WriteAsync("uninstalled-inventory.json", new { installed, explicitlyRemoved = ids }, token);
        RequireRetainedAssets(retained);
        await StartHostAsync(hostRoot, "uninstalled-host", token);
        var uninstalledContinuity = await ReadContinuityAsync(oldId, token);
        await WriteAsync("uninstall-sql-preserved.json", uninstalledContinuity, token);
        if (uninstalledContinuity != baseline)
            throw new InvalidOperationException("卸载后的固定 Host 改变了旧数据、Link、物理映射或迁移历史。");
        using (var client = WorkspaceAcceptanceFixture.CreateClient(_hostPort))
        {
            await WorkspaceAcceptanceFixture.LoginAsync(client, _administrator!, token);
            var runtime = await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Get, "/plugins/active-module-runtime.json", null, null, token);
            var endpoints = await ReadBusinessEndpointsAsync(client, token);
            await WriteAsync("uninstalled-runtime.json", new { runtime, endpoints, installed = ReadInstalledIds(), modelOwners = await ReadModelOwnersAsync(client, token) }, token);
            if (ReadInstalledIds().Intersect(ids, StringComparer.Ordinal).Any()
                || endpoints.Length != 0 || runtime.GetProperty("modules").EnumerateArray().Any(module => ids.Contains(module.GetProperty("moduleId").GetString())))
                throw new InvalidOperationException("已卸载组件仍在业务活动目录中注册。");
            var responses = new List<object>();
            foreach (var route in new[] { "/api/part/parts/" + oldId, "/api/document/documents/" + _oldDocumentId, "/api/workspace/spaces" })
            {
                using var response = await client.GetAsync(route, token);
                var body = _sanitize(await response.Content.ReadAsStringAsync(token));
                responses.Add(new { route, status = (int)response.StatusCode, body });
                await WriteAsync("uninstalled-endpoints.json", responses, token);
                using var problem = JsonDocument.Parse(body);
                // 既有 Host deny-all fallback 对未匹配路径也返回 AUTH_FORBIDDEN；注册缺失由正式 Swagger 同时证明。
                if (response.StatusCode != HttpStatusCode.Forbidden || problem.RootElement.GetProperty("code").GetString() != "AUTH_FORBIDDEN")
                    throw new InvalidOperationException($"卸载后旧入口 {route} 未遵循正式 fallback 拒绝契约：HTTP {(int)response.StatusCode}。");
            }
        }
        RequireRetainedAssets(retained);
        await StopHostAsync(token);
        await RunOfflineAsync("install-native", ["--package", packagePath], token);
        await RunOfflineAsync("enable-bundle", ["--package", packagePath], token);
        await StartHostAsync(hostRoot, "uninstall-restored-host", token);
        var restoredContinuity = await ReadContinuityAsync(oldId, token);
        await WriteAsync("uninstall-sql-restored.json", restoredContinuity, token);
        if (restoredContinuity != baseline)
            throw new InvalidOperationException("重新安装后旧数据、Link、物理映射或迁移历史未保持。");
        using var restored = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        await WorkspaceAcceptanceFixture.LoginAsync(restored, _administrator!, token);
        var restoredEndpoints = await ReadBusinessEndpointsAsync(restored, token);
        RequireBusinessEndpoints(restoredEndpoints);
        if (!restoredEndpoints.SequenceEqual(registeredEndpoints)) throw new InvalidOperationException("重装后真实三成员 endpoint 集合发生漂移。");
        var features = await AcceptanceFeatureCatalog.LoadAsync(restored, token);
        var detail = await ReadOldPartAsync(restored, features, oldId, token);
        await VerifyWorkspaceApiContinuityAsync(restored, features, oldId, "uninstall-restored-workspace-api.json", token);
        RequireRetainedAssets(retained);
        var restoredInventory = ReadInstalledIds();
        if (!restoredInventory.SequenceEqual(installedBefore)) throw new InvalidOperationException("重新安装未恢复原完整库存集合。");
        await WriteAsync("uninstall-recovery.json", new { explicitlyRemoved = ids, restoredInventory, restoredEndpoints, detail,
            retainedAssets = retained, continuityPreserved = true, ordinaryGrantsReplayed = false, consumer = "local-admin" }, token);
    }

    private string[] ReadInstalledIds() => InstalledPluginInventoryAdapter.Create(new InstalledPluginStateStore(InstallRoot))
        .Select(item => item.ModuleId).Order(StringComparer.Ordinal).ToArray();

    private static async Task<string[]> ReadBusinessEndpointsAsync(HttpClient client, CancellationToken token)
    {
        var swagger = await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Get, "/swagger/v1/swagger.json", null, null, token);
        return swagger.GetProperty("paths").EnumerateObject().Select(path => path.Name)
            .Where(path => new[] { "/api/part/", "/api/document/", "/api/workspace/" }.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal).ToArray();
    }

    private static void RequireBusinessEndpoints(string[] endpoints)
    {
        if (!new[] { "/api/part/", "/api/document/", "/api/workspace/" }.All(prefix => endpoints.Any(path => path.StartsWith(prefix, StringComparison.Ordinal))))
            throw new InvalidOperationException("正式 Swagger 未注册完整的三成员业务 endpoint。");
    }

    private CandidateFile[] ReadFiles(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(path => new CandidateFile(Path.GetRelativePath(InstallRoot, path), FileHash(path))).OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();

    private CandidateFile[] ReadSuccessfulAssets()
    {
        var assets = new List<CandidateFile>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(InstallRoot, "persistence"), "state.json", SearchOption.AllDirectories))
        {
            using var state = JsonDocument.Parse(File.ReadAllText(path));
            var digest = state.RootElement.GetProperty("successful").GetString()
                ?? throw new InvalidOperationException("卸载前缺少正式成功持久化资产。");
            var asset = Path.Combine(Path.GetDirectoryName(path)!, digest + ".json");
            using var recovery = JsonDocument.Parse(File.ReadAllText(asset));
            var owners = recovery.RootElement.GetProperty("plan").GetProperty("models").EnumerateArray()
                .Select(model => model.GetProperty("ownerModuleId").GetString()).ToHashSet(StringComparer.Ordinal);
            if (!new[] { "Workspace", "Part", "Document" }.All(owners.Contains))
                throw new InvalidOperationException("卸载前成功恢复资产未持有三成员的模型 owner。");
            assets.Add(new(Path.GetRelativePath(InstallRoot, asset), FileHash(asset)));
        }
        if (assets.Count == 0) throw new InvalidOperationException("卸载前缺少成功数据库部署输入。");
        return assets.ToArray();
    }

    private void RequireRetainedAssets(IEnumerable<CandidateFile> assets)
    {
        foreach (var asset in assets)
        {
            var path = Path.Combine(InstallRoot, asset.Path);
            if (!File.Exists(path) || FileHash(path) != asset.Sha256)
                throw new InvalidOperationException($"卸载/恢复改变了必要可信资产 {asset.Path}。");
        }
    }

    internal async Task RestartAsync(CancellationToken token)
    {
        await StopHostAsync(token);
        await StartHostAsync(hostRoot, "restarted-host-" + Guid.NewGuid().ToString("N"), token);
    }

    // 用合法完整候选验证父屏蔽；先停用依赖者 Workspace，再停用 Document，保留 Part 自身启用。
    private async Task VerifyCompositionLifecycleAsync(WorkspaceAcceptanceActor actor, string oldId, string partPosition,
        Continuity baseline, CancellationToken token)
    {
        var allEnabled = ReadMemberChoices();
        await StopHostAsync(token);
        await RunOfflineAsync("disable", ["--module-id", "Workspace"], token);
        await RunOfflineAsync("disable", ["--module-id", "Document"], token);
        var ownChoices = ReadMemberChoices();
        if (ownChoices == allEnabled) throw new InvalidOperationException("正式成员停用未形成自身选择。");
        await StartHostAsync(hostRoot, "members-selected", token);
        await RequirePartReadAsync(actor, oldId, partPosition, true, token);
        await StopHostAsync(token);
        await RunOfflineAsync("disable", ["--module-id", "InforCenter"], token);
        if (ReadMemberChoices() != ownChoices) throw new InvalidOperationException("父停用改写了成员自身选择。");
        await StartHostAsync(hostRoot, "parent-masked", token);
        await RequirePartReadAsync(actor, oldId, partPosition, false, token);
        if (await ReadContinuityAsync(oldId, token) != baseline) throw new InvalidOperationException("父屏蔽改变旧数据或历史。");
        await StopHostAsync(token);
        await RunOfflineAsync("enable", ["--module-id", "InforCenter", "--version", "1.0.0"], token);
        if (ReadMemberChoices() != ownChoices) throw new InvalidOperationException("父恢复覆盖了停用成员。");
        await StartHostAsync(hostRoot, "parent-restored", token);
        await RequirePartReadAsync(actor, oldId, partPosition, true, token);
        await StopHostAsync(token);
        await RunOfflineAsync("enable", ["--module-id", "Document", "--version", "1.0.0"], token);
        await RunOfflineAsync("enable", ["--module-id", "Workspace", "--version", "1.0.0"], token);
        await StartHostAsync(hostRoot, "members-restored", token);
        if (ReadMemberChoices() != allEnabled || await ReadContinuityAsync(oldId, token) != baseline)
            throw new InvalidOperationException("明确恢复成员后，选择或旧数据/历史未保持。");
        await WriteAsync("composition-lifecycle.json", new { ownChoices, parentMaskPreservedChoices = true,
            parentRestorePreservedChoices = true, restoredMembers = allEnabled, continuityPreserved = true }, token);
    }

    private string ReadMemberChoices()
    {
        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(InstallRoot, "lifecycle", "v2", "state.json")));
        return JsonSerializer.Serialize(state.RootElement.GetProperty("desiredModules").EnumerateArray()
            .Where(module => module.GetProperty("moduleId").GetString() != "InforCenter")
            .OrderBy(module => module.GetProperty("moduleId").GetString(), StringComparer.Ordinal));
    }

    private async Task RequirePartReadAsync(WorkspaceAcceptanceActor actor, string oldId, string position, bool available, CancellationToken token)
    {
        using var client = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        var login = await WorkspaceAcceptanceFixture.LoginAsync(client, actor, token);
        var runtime = await WorkspaceAcceptanceFixture.SendAsync(client, HttpMethod.Get, "/plugins/active-module-runtime.json", null, null, token);
        var active = runtime.GetProperty("modules").EnumerateArray().Any(module => module.GetProperty("moduleId").GetString() == "Part");
        var currentPosition = active ? (await AcceptanceFeatureCatalog.LoadAsync(client, token)).Behavior("Part", "part.part.read").Position : null;
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/part/parts/" + oldId);
        request.Headers.Add("X-NewP-Feature", position);
        using var response = await client.SendAsync(request, token);
        var body = _sanitize(await response.Content.ReadAsStringAsync(token));
        using var administrator = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        var adminLogin = await WorkspaceAcceptanceFixture.LoginAsync(administrator, _administrator!, token);
        using var adminRequest = new HttpRequestMessage(HttpMethod.Get, "/api/part/parts/" + oldId);
        adminRequest.Headers.Add("X-NewP-Feature", position);
        using var adminResponse = await administrator.SendAsync(adminRequest, token);
        await WriteAsync(_host!.LogName + "-part-read.json", new { actorId = actor.AccountId, actorCode = actor.ECode,
            requestedPosition = position, currentPosition, active, expectedAvailable = available, status = (int)response.StatusCode,
            body, featurePositions = login.GetProperty("featurePositions"), administratorStatus = (int)adminResponse.StatusCode,
            administratorFeaturePositions = adminLogin.GetProperty("featurePositions"),
            administratorBody = _sanitize(await adminResponse.Content.ReadAsStringAsync(token)) }, token);
        var administratorBody = await adminResponse.Content.ReadAsStringAsync(token);
        using var problem = available ? null : JsonDocument.Parse(administratorBody);
        var rejectionMatches = !available && adminResponse.StatusCode == HttpStatusCode.Forbidden
            && problem!.RootElement.GetProperty("code").GetString() == "AUTH_FORBIDDEN";
        var adminHasFeature = adminLogin.GetProperty("featurePositions").EnumerateArray().Any(value => value.GetString() == position);
        if (active != available || (available ? !adminResponse.IsSuccessStatusCode || !adminHasFeature || currentPosition != position
                : !rejectionMatches || adminHasFeature || currentPosition is not null))
            throw new InvalidOperationException($"父/成员组合下已授权消费者的 Part 可用性不符：HTTP {(int)adminResponse.StatusCode}，期望 {available}。");
        if (_host.LogName == "parent-restored" && (response.StatusCode != HttpStatusCode.Forbidden
                || login.GetProperty("featurePositions").EnumerateArray().Any(value => value.GetString() == position)))
            throw new InvalidOperationException("父恢复意外重放已删除的普通角色功能授权。");
    }

    private sealed record Continuity(string PhysicalMapping, string PartRow, string History, string DocumentRow = "",
        string WorkspaceFolders = "", string WorkspaceSpaces = "", string Links = "", string IdentityAccounts = "");

    // Files 依赖可能先升级；只允许本轮明确 owner 的新尾，旧历史与既有业务数据仍逐行保持。
    private static void VerifyPreservedPredecessor(Continuity before, Continuity after, params string[] allowedNewOwners)
    {
        if (before with { History = after.History } != after)
            throw new InvalidOperationException("图文档升级改变了清理范围外的数据或物理映射。");
        using var oldHistory = JsonDocument.Parse(before.History);
        using var newHistory = JsonDocument.Parse(after.History);
        var previous = oldHistory.RootElement.EnumerateArray().ToArray();
        var current = newHistory.RootElement.EnumerateArray().ToArray();
        if (current.Length < previous.Length || previous.Where((row, index) => !JsonElement.DeepEquals(row, current[index])).Any()
            || current.Skip(previous.Length).Any(row => !allowedNewOwners.Contains(row.GetProperty("OwnerModuleId").GetString(), StringComparer.Ordinal)))
            throw new InvalidOperationException("图文档升级改写了已发布迁移历史或追加了其他 owner。");
    }

    // Files 与 Document 新模型分别归自身；Part 与 Workspace 的旧装配不能随之演进。
    private static void VerifyDocumentModelEvolution(string before, string after)
    {
        using var oldOwners = JsonDocument.Parse(before); using var newOwners = JsonDocument.Parse(after);
        foreach (var oldOwner in oldOwners.RootElement.EnumerateArray())
        {
            var owner = oldOwner.GetProperty("owner").GetString();
            var current = newOwners.RootElement.EnumerateArray().Single(row => row.GetProperty("owner").GetString() == owner);
            if (owner == "Document")
            {
                if (!current.GetProperty("models").EnumerateArray().Select(row => row.GetString()).SequenceEqual(["Document", "DocumentFileLink", "DocumentIteration", "DocumentVersion"]))
                    throw new InvalidOperationException("Document 初版模型装配不完整。");
            }
            else if (owner == "Files") continue;
            else if (!JsonElement.DeepEquals(oldOwner, current)) throw new InvalidOperationException("非 Document 模型 owner 发生漂移。");
        }
        var files = newOwners.RootElement.EnumerateArray().Single(row => row.GetProperty("owner").GetString() == "Files");
        if (!files.GetProperty("models").EnumerateArray().Select(row => row.GetString()).SequenceEqual(["FileRecord", "FileUploadLease", "FileUsage"]))
            throw new InvalidOperationException("Files 正式模型装配不完整。");
    }

    // 使用真实旧 Document 证明空表 guard 在该 step 的 DDL/history 前拒绝；失败后仍可精确清理并重试。
    private async Task VerifyDocumentUpgradeGuardAsync(string partId, Continuity baseline, CancellationToken token)
    {
        _environment["ASPNETCORE_URLS"] = HostOrigin;
        _host = StartProcess("dotnet", [Path.Combine(hostRoot, "HostApi.dll")], hostRoot, "document-nonempty-guard");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(180));
        await _host.Process.WaitForExitAsync(timeout.Token);
        var output = _sanitize(await _host.Output + await _host.Error);
        if (_host.Process.ExitCode == 0 || !output.Contains("PERSISTENCE_MIGRATION_TABLE_NOT_EMPTY", StringComparison.Ordinal))
            throw new InvalidOperationException("非空旧 Document 未由正式迁移空表前提拒绝；读取脱敏 Host 日志。");
        await StopHostAsync(CancellationToken.None);
        var rejected = await ReadContinuityAsync(partId, token);
        // Document step 必须零推进；已完成的 Files 依赖 stream 允许合法追加，不放宽旧前缀检查。
        VerifyPreservedPredecessor(baseline, rejected, "Files");
        var schema = await ReadSqlJsonAsync("SELECT COL_LENGTH(N'dbo.Document',N'LatestVersionId') pointerLength, OBJECT_ID(N'dbo.DocumentVersion') versionTable, OBJECT_ID(N'dbo.DocumentIteration') iterationTable FOR JSON PATH, INCLUDE_NULL_VALUES;", null, token);
        using var document = JsonDocument.Parse(schema);
        if (document.RootElement[0].EnumerateObject().Any(property => property.Value.ValueKind != JsonValueKind.Null))
            throw new InvalidOperationException("空表前提失败后 Document 新 DDL 已发生。");
        await WriteAsync("document-empty-table-rejection.json", new { rejected, schema, failure = "PERSISTENCE_MIGRATION_TABLE_NOT_EMPTY" }, token);
    }

    // 仅删除此 fixture 创建且已获准丢弃的旧 Document 与对应入向引用；不重置 catalog 或其他业务数据。
    private async Task ClearOwnedLegacyDocumentAsync(CancellationToken token)
    {
        await using var connection = new SqlConnection(_database!.ConnectionString); await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand(); command.Transaction = (SqlTransaction)transaction;
        command.CommandText = "DELETE FROM dbo.WorkspaceFolderContent WHERE ChildModelKey=N'Document' AND CEID=@id; DELETE FROM dbo.Document WHERE EID=@id AND ECODE=@code;";
        command.Parameters.AddWithValue("@id", _oldDocumentId!); command.Parameters.AddWithValue("@code", ExistingDocumentCode);
        if (await command.ExecuteNonQueryAsync(token) != 2) throw new InvalidOperationException("精确旧 Document 清理未命中自身一对象一引用。");
        await transaction.CommitAsync(token);
    }

    private async Task<Continuity> ReadContinuityAsync(string oldId, CancellationToken token)
    {
        var mapping = await ReadSqlJsonAsync("SELECT SCHEMA_NAME(schema_id) AS schemaName, name AS tableName FROM sys.tables WHERE name IN (" +
            (workspaceDelivery ? "N'Part',N'Document',N'WorkspaceFolder',N'WorkspaceSpace',N'WorkspaceFolderContent'" : "N'Part'") + ") ORDER BY schemaName, tableName FOR JSON PATH;", null, token);
        var row = await ReadSqlJsonAsync("SELECT * FROM dbo.Part WHERE EID=@id FOR JSON PATH;", oldId, token);
        var history = await ReadSqlJsonAsync("SELECT * FROM dbo.__NewPMigrationHistory ORDER BY Position FOR JSON PATH;", null, token);
        using var rows = JsonDocument.Parse(row);
        using var ledger = JsonDocument.Parse(history);
        if (rows.RootElement.GetArrayLength() != 1 || !ledger.RootElement.EnumerateArray()
                .Any(item => item.GetProperty("OwnerModuleId").GetString() == "Part"))
            throw new InvalidOperationException("正式 baseline 未形成旧 Part 行与真实迁移历史。");
        if (!workspaceDelivery) return new(mapping, row, history);
        foreach (var owner in new[] { "Workspace", "Part", "Document" })
            if (!ledger.RootElement.EnumerateArray().Any(item => item.GetProperty("OwnerModuleId").GetString() == owner))
                throw new InvalidOperationException($"正式 baseline 缺少 {owner} 迁移历史。");
        return new(mapping, row, history,
            await ReadSqlJsonAsync("SELECT * FROM dbo.Document WHERE EID=@id FOR JSON PATH;", _oldDocumentId, token),
            await ReadSqlJsonAsync("SELECT * FROM dbo.WorkspaceFolder ORDER BY EID FOR JSON PATH;", null, token),
            await ReadSqlJsonAsync("SELECT * FROM dbo.WorkspaceSpace ORDER BY EID FOR JSON PATH;", null, token),
            await ReadSqlJsonAsync("SELECT * FROM dbo.WorkspaceFolderContent ORDER BY LID FOR JSON PATH;", null, token),
            await ReadSqlJsonAsync("SELECT * FROM dbo.IdentityAccount ORDER BY EID FOR JSON PATH;", null, token));
    }

    // SQL FOR JSON 会拆成多行传输；完整拼接后才比较，不能用 ExecuteScalar 丢弃后续账本。
    private async Task<string> ReadSqlJsonAsync(string sql, string? id, CancellationToken token)
    {
        await using var connection = new SqlConnection(_database!.ConnectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (id is not null) command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(token);
        var json = new StringBuilder();
        while (await reader.ReadAsync(token)) json.Append(reader.GetString(0));
        return json.Length == 0 ? "[]" : json.ToString();
    }

    private sealed record TargetIdentity(string Host, string Shell);

    private TargetIdentity ReadTargetIdentity()
    {
        if (!File.Exists(Path.Combine(hostRoot, "HostApi.dll")) || !File.Exists(Path.Combine(shellRoot, "index.html")))
            throw new InvalidOperationException("固定 Host/Shell 发行物缺失，验收不自动重新构建。");
        var ids = workspaceDelivery ? new[] { "Workspace", "Part", "Document" } : ["Part"];
        var deps = File.ReadAllText(Path.Combine(hostRoot, "HostApi.deps.json"));
        if (ids.Any(id => Directory.EnumerateFiles(hostRoot, id + "Module.dll", SearchOption.AllDirectories).Any()
            || Directory.Exists(Path.Combine(hostRoot, "modules", id)) || deps.Contains(id + "Module/", StringComparison.Ordinal))
            || Directory.Exists(Path.Combine(hostRoot, "modules", "InforCenter")))
            throw new InvalidOperationException("目标 Host 已预装 Part 或旧 InforCenter。");
        var manifest = File.ReadAllText(Path.Combine(shellRoot, ".vite", "manifest.json"));
        if (ids.Any(id => manifest.Contains("/" + id.ToLowerInvariant() + "/src/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("目标 Shell 仍含构建期 Part 实现。");
        return new(HashTree(hostRoot, _ => true),
            HashTree(shellRoot, _ => true));
    }

    private static string HashTree(string root, Func<string, bool> include) => string.Join('\n',
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(include)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/') + ":" + FileHash(path)).Order(StringComparer.Ordinal));

    private static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private async Task StartHostAsync(string root, string logName, CancellationToken token)
    {
        _environment["ASPNETCORE_URLS"] = HostOrigin;
        _host = StartProcess("dotnet", [Path.Combine(root, "HostApi.dll")], root, logName);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(180));
        while (!timeout.IsCancellationRequested)
        {
            if (_host.Process.HasExited) throw new InvalidOperationException($"固定 Host {logName} 在就绪前退出；读取本轮脱敏日志。");
            try
            {
                using var response = await client.GetAsync(HostOrigin + "/api/health", timeout.Token);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(200, timeout.Token);
        }
        timeout.Token.ThrowIfCancellationRequested();
    }

    private async Task RunOfflineAsync(string operation, IReadOnlyList<string> arguments, CancellationToken token, string? expectedFailureCode = null)
    {
        var command = new[] { Path.Combine(hostRoot, "HostApi.dll"), "plugin-deploy", operation }.Concat(arguments).ToArray();
        var process = StartProcess("dotnet", command, hostRoot, operation + "-" + Guid.NewGuid().ToString("N"));
        var exitCode = -1;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(180));
            await process.Process.WaitForExitAsync(timeout.Token);
            exitCode = process.Process.ExitCode;
        }
        finally { await StopProcessAsync(process, CancellationToken.None); }
        await WriteAsync(operation + "-command-" + Guid.NewGuid().ToString("N") + ".json", new { file = "dotnet", arguments = command, exitCode, expectedFailureCode }, token);
        if (expectedFailureCode is null ? exitCode != 0 : exitCode != 1 || !(await process.Output + await process.Error).Contains(expectedFailureCode, StringComparison.Ordinal))
            throw new InvalidOperationException($"正式 plugin-deploy {operation} 返回 {exitCode}，期望 {expectedFailureCode ?? "成功"}。");
    }

    private sealed record OwnedProcess(Process Process, Task<string> Output, Task<string> Error, string LogName, ConcurrentQueue<string> Lines);

    private OwnedProcess StartProcess(string file, IReadOnlyList<string> arguments, string cwd, string logName)
    {
        var start = new ProcessStartInfo(file) { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var pair in _environment) start.Environment[pair.Key] = pair.Value;
        var process = Process.Start(start) ?? throw new InvalidOperationException($"无法启动固定发行物 {file}。");
        var lines = new ConcurrentQueue<string>();
        return new(process, CaptureOutputAsync(process.StandardOutput, lines, _sanitize), CaptureOutputAsync(process.StandardError, lines, _sanitize),
            string.Concat(logName.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_')), lines);
    }

    // 同一输出既用于最终脱敏日志，也让故障旅程等待已发生的事件，而不是靠固定延时猜测。
    private static async Task<string> CaptureOutputAsync(StreamReader reader, ConcurrentQueue<string> lines, Func<string?, string> sanitize)
    {
        var output = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            output.AppendLine(line);
            var safe = sanitize(line);
            lines.Enqueue(safe.Length <= 4096 ? safe : safe[..4096]);
            while (lines.Count > 256) lines.TryDequeue(out _);
        }
        return output.ToString();
    }

    private async Task StopHostAsync(CancellationToken token)
    {
        if (_host is null) return;
        await StopProcessAsync(_host, token);
        _host = null;
    }

    // Process 对象仅来自本 fixture 的 CreateProcess；不按进程名、端口或裸 PID 清理其他会话。
    private async Task StopProcessAsync(OwnedProcess owned, CancellationToken token)
    {
        try
        {
            if (!owned.Process.HasExited) owned.Process.Kill(entireProcessTree: true);
            await owned.Process.WaitForExitAsync(token);
            await File.WriteAllTextAsync(Path.Combine(runDirectory, owned.LogName + ".stdout.log"), _sanitize(await owned.Output), token);
            await File.WriteAllTextAsync(Path.Combine(runDirectory, owned.LogName + ".stderr.log"), _sanitize(await owned.Error), token);
        }
        finally { owned.Lines.Clear(); owned.Process.Dispose(); }
    }

    internal async Task<AcceptanceCleanupResult> TeardownAsync(CancellationToken token)
    {
        try
        {
            if (_shell is not null) { await StopProcessAsync(_shell, token); _shell = null; }
            await StopHostAsync(token);
            if (_database is not null)
            {
                if (_ownership is not null && _ownership != await ReadSqlJsonAsync("SELECT * FROM dbo.__NewPDatabaseOwnership FOR JSON PATH;", null, token))
                    return new("failed", AcceptanceFailureCodes.OwnershipMismatch);
                await _database.DisposeAsync();
                _database = null;
            }
            if (Directory.Exists(Resources))
            {
                foreach (var directory in Directory.EnumerateDirectories(Resources))
                { AcceptanceInfrastructure.EnsureOwnedResourceRoot(runDirectory, directory); Directory.Delete(directory, true); }
                foreach (var file in Directory.EnumerateFiles(Resources)) File.Delete(file);
                Directory.Delete(Resources);
            }
            await WriteCandidateAsync("candidate-after.json", token);
            var targetAfter = ReadTargetIdentity();
            await WriteAsync("target-after.json", targetAfter, token);
            if (deliveryInput is not null && !JsonElement.DeepEquals(deliveryInput.Candidate, await ReadCandidateAsync(repositoryRoot, token)))
                throw new InvalidOperationException("Workspace 验收运行期间候选变化，证据无法归属。");
            await WriteAsync("cleanup.json", new { catalog = ReusableSqlServerDatabase.Catalog, schemaCleaned = true,
                ownedProcessesStopped = true, resourcesRemoved = true }, token);
            if (deliveryInput is not null && (deliveryInput.HostIdentity != targetAfter.Host || deliveryInput.ShellIdentity != targetAfter.Shell))
                throw new InvalidOperationException("Workspace 验收运行期间固定 Host/Shell 发生变化。");
            return new("completed", null);
        }
        finally
        {
            if (_database is not null) { await _database.ReleaseAsync(); _database = null; }
        }
    }

    private Task WriteAsync<T>(string name, T value, CancellationToken token) =>
        AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(runDirectory, name), value, token);

    private async Task WriteCandidateAsync(string name, CancellationToken token) => await WriteAsync(name, await ReadCandidateAsync(repositoryRoot, token), token);

    private static async Task<JsonElement> ReadCandidateAsync(string repositoryRoot, CancellationToken token) => JsonSerializer.SerializeToElement(new
    {
        head = await AcceptanceInfrastructure.RunGitAsync(repositoryRoot, ["rev-parse", "HEAD"], token),
        tree = await AcceptanceInfrastructure.RunGitAsync(repositoryRoot, ["rev-parse", "HEAD^{tree}"], token),
        index = await AcceptanceInfrastructure.RunGitAsync(repositoryRoot, ["write-tree"], token),
        status = await AcceptanceInfrastructure.RunGitRawAsync(repositoryRoot, ["status", "--porcelain=v2"], token),
        trackedDiffSha256 = await ReadTrackedDiffHashAsync(repositoryRoot, token),
        tracked = await ReadCandidateFilesAsync(repositoryRoot, false, token),
        untracked = await ReadCandidateFilesAsync(repositoryRoot, true, token)
    }, AcceptanceJson.Options);

    private sealed record CandidateFile(string Path, string Sha256);

    // status 只能证明未跟踪路径存在；内容摘要同时覆盖本片新增资产与尚未提交的产品文件。
    private static async Task<CandidateFile[]> ReadCandidateFilesAsync(string repositoryRoot, bool untracked, CancellationToken token)
    {
        var files = await AcceptanceInfrastructure.RunGitRawAsync(repositoryRoot,
            untracked ? ["ls-files", "--others", "--exclude-standard", "-z"] : ["ls-files", "-z"], token);
        return files.Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal)
            .Where(path => File.Exists(Path.Combine(repositoryRoot, path)))
            .Select(path => new CandidateFile(path, FileHash(Path.Combine(repositoryRoot, path)))).ToArray();
    }

    // CLI 与 VSTest 可以有不同 console 编码；候选差异必须按 Git 实际输出字节哈希。
    private static async Task<string> ReadTrackedDiffHashAsync(string repositoryRoot, CancellationToken token)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = repositoryRoot, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in new[] { "diff", "--binary", "--no-color", "--no-ext-diff", "--no-textconv", "HEAD" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法读取 Git 候选差异。");
        try
        {
            var error = process.StandardError.ReadToEndAsync(token);
            var hash = await SHA256.HashDataAsync(process.StandardOutput.BaseStream, token);
            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0) throw new InvalidOperationException(await error);
            await error;
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
        }
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
