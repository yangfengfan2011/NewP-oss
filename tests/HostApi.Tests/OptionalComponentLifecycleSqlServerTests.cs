using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.HostApi.PluginActivation;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class OptionalComponentLifecycleSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "初次停用的真实业务组件不建表；新选版在父级屏蔽期间不执行迁移")]
    public async Task InitiallyMaskedNewVersion_DoesNotCreateBusinessTablesOrRunItsMigration()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([], beforeFirstStartup: prepared =>
        {
            SelectUnmigratableNewPartVersion(prepared, enabled: true);
            SetEnabled(prepared, "InforCenter", false);
            return Task.CompletedTask;
        });
        Assert.DoesNotContain(fixture.Catalog, point => point.ModuleId is "Workspace" or "Part" or "Document");
        Assert.Equal(0, await Scalar(fixture, "SELECT COUNT(*) FROM sys.tables WHERE name IN (N'Part',N'Document',N'WorkspaceFolder',N'WorkspaceFolderContent',N'WorkspaceSpace');"));
        Assert.Equal(0, await Scalar(fixture, "SELECT COUNT(*) FROM dbo.__NewPMigrationHistory WHERE OwnerModuleId IN (N'Part',N'Document',N'Workspace');"));
        var runtime = await fixture.Administrator.GetFromJsonAsync<JsonElement>("/plugins/active-module-runtime.json");
        var part = Assert.Single(runtime.GetProperty("compositionGraph").EnumerateArray(), node => node.GetProperty("moduleId").GetString() == "Part");
        Assert.Equal("1.1.0", part.GetProperty("version").GetString());
        await fixture.StopAsync();
        SetEnabled(fixture, "InforCenter", true);
        await fixture.StartAsync(expectFailure: true);
        Assert.Contains("PERSISTENCE_ARTIFACT_PATH_INVALID", fixture.LastError);
        Assert.Equal(0, await Scalar(fixture, "SELECT COUNT(*) FROM sys.tables WHERE name=N'Part';"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式候选业务依赖不闭合在资产与DDL前拒绝，不覆盖成功部署")]
    public async Task ActiveWorkspaceWithDisabledPart_RejectsBeforeDatabaseChangeOrDeploymentPublication()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        await fixture.StopAsync();
        var statePath = Assert.Single(Directory.GetFiles(Path.Combine(fixture.PluginRoot, "persistence"), "state.json", SearchOption.AllDirectories));
        var state = File.ReadAllBytes(statePath);
        var before = await Scalar(fixture, "SELECT COUNT(*) FROM dbo.__NewPMigrationHistory;");
        SetEnabled(fixture, "Part", false);
        await fixture.StartAsync(expectFailure: true);
        Assert.Contains("MODULE_DEPENDENCY_MISSING", fixture.LastError);
        Assert.Equal(state, File.ReadAllBytes(statePath));
        Assert.Equal(before, await Scalar(fixture, "SELECT COUNT(*) FROM dbo.__NewPMigrationHistory;"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "首个Host数据库屏障前已有完整恢复资产，屏蔽重试保留可信模型与迁移输入且不记成功")]
    public async Task FirstHostDatabaseBarrierFailure_PreservesRecoveryInputsAcrossMaskedRetry()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([], async prepared =>
            await Execute(prepared, "CREATE TABLE dbo.UnknownFirst262 (ID int NOT NULL);"), expectInitialStartupFailure: true);
        Assert.Contains("PERSISTENCE_DATABASE_REBUILD_REQUIRED", fixture.LastError);
        Assert.Equal(0, await Scalar(fixture, "SELECT COUNT(*) FROM sys.tables WHERE name IN (N'Part',N'Document',N'WorkspaceFolder',N'__NewPMigrationHistory');"));
        var statePath = Assert.Single(Directory.GetFiles(Path.Combine(fixture.PluginRoot, "persistence"), "state.json", SearchOption.AllDirectories));
        var stateBytes = File.ReadAllBytes(statePath);
        var state = JsonNode.Parse(stateBytes)!;
        Assert.Null(state["successful"]);
        var pending = state["pending"]!.GetValue<string>();
        var assetPath = Path.Combine(Path.GetDirectoryName(statePath)!, pending + ".json");
        var assetBytes = File.ReadAllBytes(assetPath);
        var asset = JsonNode.Parse(assetBytes)!;
        foreach (var owner in new[] { "Part", "Document", "Workspace" })
        {
            Assert.Contains(asset["plan"]!["models"]!.AsArray(), model => model!["ownerModuleId"]!.GetValue<string>() == owner);
            var image = Assert.Single(asset["images"]!.AsArray(), image => image!["moduleId"]!.GetValue<string>() == owner)!;
            Assert.NotEmpty(Convert.FromBase64String(image["assembly"]!.GetValue<string>()));
            Assert.NotEmpty(Convert.FromBase64String(image["manifest"]!.GetValue<string>()));
        }
        SetEnabled(fixture, "InforCenter", false);
        await fixture.StartAsync(expectFailure: true);
        Assert.Contains("PERSISTENCE_DATABASE_REBUILD_REQUIRED", fixture.LastError);
        Assert.Equal(0, await Scalar(fixture, "SELECT COUNT(*) FROM sys.tables WHERE name IN (N'Part',N'Document',N'WorkspaceFolder',N'__NewPMigrationHistory',N'__EFMigrationsHistory');"));
        var retryState = JsonNode.Parse(File.ReadAllBytes(statePath))!;
        Assert.Null(retryState["successful"]);
        Assert.Null(retryState["previousSuccessful"]);
        var retryAssetPath = Path.Combine(Path.GetDirectoryName(statePath)!, retryState["pending"]!.GetValue<string>() + ".json");
        var retryAsset = JsonNode.Parse(File.ReadAllBytes(retryAssetPath))!;
        // 既有恢复规则允许合法候选重绑定 pending，但不允许覆写已归档身份、模型或不可变迁移前缀。
        Assert.Equal(asset["targetIdentity"]!.GetValue<string>(), retryAsset["targetIdentity"]!.GetValue<string>());
        Assert.Equal(asset["plan"]!["provider"]!.ToJsonString(), retryAsset["plan"]!["provider"]!.ToJsonString());
        Assert.Equal(asset["plan"]!["models"]!.ToJsonString(), retryAsset["plan"]!["models"]!.ToJsonString());
        Assert.Equal(asset["plan"]!["streams"]!.AsArray().OrderBy(item => item!["moduleId"]!.GetValue<string>(), StringComparer.Ordinal).Select(item => item!.ToJsonString()),
            retryAsset["plan"]!["streams"]!.AsArray().OrderBy(item => item!["moduleId"]!.GetValue<string>(), StringComparer.Ordinal).Select(item => item!.ToJsonString()));
        Assert.Equal(asset["images"]!.AsArray().OrderBy(item => item!["moduleId"]!.GetValue<string>(), StringComparer.Ordinal).Select(item => item!.ToJsonString()),
            retryAsset["images"]!.AsArray().OrderBy(item => item!["moduleId"]!.GetValue<string>(), StringComparer.Ordinal).Select(item => item!.ToJsonString()));
        Assert.Equal(new[] { "Document", "Part", "Workspace" }, retryAsset["plan"]!["retainedModuleIds"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.Equal(assetBytes, File.ReadAllBytes(assetPath));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "父级恢复保持成员显式停用；保留部署不采纳停用成员的新迁移资产")]
    public async Task ParentRestore_LeavesExplicitMembersDisabledAndPreservesTheirOldDeployment()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        var before = await Scalar(fixture, "SELECT COUNT(*) FROM dbo.__NewPMigrationHistory;");
        await fixture.StopAsync();
        SetEnabled(fixture, "Workspace", false);
        SetEnabled(fixture, "Part", false);
        SetEnabled(fixture, "InforCenter", false);
        SelectUnmigratableNewPartVersion(fixture, enabled: false);
        await fixture.StartAsync();
        Assert.DoesNotContain(fixture.Catalog, point => point.ModuleId is "Workspace" or "Part" or "Document");
        Assert.Equal(before, await Scalar(fixture, "SELECT COUNT(*) FROM dbo.__NewPMigrationHistory;"));
        await fixture.StopAsync();
        SetEnabled(fixture, "InforCenter", true);
        await fixture.StartAsync();
        Assert.Contains(fixture.Catalog, point => point.ModuleId == "Document");
        Assert.DoesNotContain(fixture.Catalog, point => point.ModuleId is "Workspace" or "Part");
        Assert.Equal(before, await Scalar(fixture, "SELECT COUNT(*) FROM dbo.__NewPMigrationHistory;"));
        Assert.Equal(1, await Scalar(fixture, "SELECT COUNT(*) FROM sys.tables WHERE name=N'Part';"));
        var runtime = await fixture.Administrator.GetFromJsonAsync<JsonElement>("/plugins/active-module-runtime.json");
        var part = Assert.Single(runtime.GetProperty("compositionGraph").EnumerateArray(), node => node.GetProperty("moduleId").GetString() == "Part");
        Assert.Equal("1.1.0", part.GetProperty("version").GetString());
        Assert.False(part.GetProperty("enabled").GetBoolean());
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "停用保留时未知表仍阻断正式Host")]
    public Task RetainedDeployment_UnknownTableStillBlocks() => VerifyInvalidStorage("unknown", "PERSISTENCE_SCHEMA_TARGET_MISMATCH");

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "停用保留时真实结构漂移仍阻断正式Host")]
    public Task RetainedDeployment_RealDriftStillBlocks() => VerifyInvalidStorage("drift", "PERSISTENCE_SCHEMA_TARGET_MISMATCH");

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "缺少可信部署资产仍阻断正式Host")]
    public Task RetainedDeployment_MissingTrustedAssetStillBlocks() => VerifyInvalidStorage("missing", "PERSISTENCE_DEPLOYMENT_ASSET_MISSING");

    private static async Task VerifyInvalidStorage(string scenario, string diagnostic)
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        await fixture.StopAsync();
        SetEnabled(fixture, "InforCenter", false);
        var before = await Scalar(fixture, "SELECT COUNT(*) FROM dbo.__NewPMigrationHistory;");
        if (scenario == "unknown") await Execute(fixture, "CREATE TABLE dbo.Unknown262 (ID int NOT NULL);");
        else if (scenario == "drift") await Execute(fixture, "ALTER TABLE dbo.Part ADD Unexpected262 int NULL;");
        else
        {
            var statePath = Assert.Single(Directory.GetFiles(Path.Combine(fixture.PluginRoot, "persistence"), "state.json", SearchOption.AllDirectories));
            var state = JsonNode.Parse(File.ReadAllText(statePath))!;
            File.Delete(Path.Combine(Path.GetDirectoryName(statePath)!, state["successful"]!.GetValue<string>() + ".json"));
        }
        await fixture.StartAsync(expectFailure: true);
        Assert.Contains(diagnostic, fixture.LastError);
        Assert.Equal(before, await Scalar(fixture, "SELECT COUNT(*) FROM dbo.__NewPMigrationHistory;"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式Host首次迁移、业务引用、组合停用及同版本恢复")]
    public async Task InforCenterDisableAndRestore_PreservesDataAndReferencesAndRemovesCapabilities()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        var root = await Post(fixture, "workspace.spaces.access", "/api/workspace/spaces/public/initialize", null);
        var position = new { spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = root.GetProperty("rootFolderId").GetString(), linkIds = Array.Empty<string>() };
        var targets = new List<(string Model, string Id)>();
        foreach (var (model, route, behavior) in new[] { ("Part", "/api/part/parts", "part.part.create"), ("Document", "/api/document/documents", "document.document.create") })
        {
            var created = await Post(fixture, behavior, route, new { ECODE = "RETAINED", ENAME = model + "保留" });
            var id = created.GetProperty("fields").GetProperty("EID").GetString()!;
            targets.Add((model, id));
            await Post(fixture, "workspace.folder.add", "/api/workspace/content/references", new { position, target = new { modelKey = model, id } });
        }
        await fixture.StopAsync();
        SetEnabled(fixture, "InforCenter", false);
        await fixture.StartAsync();
        Assert.DoesNotContain(fixture.Catalog, point => point.ModuleId is "Workspace" or "Part" or "Document");
        var runtime = await fixture.Administrator.GetFromJsonAsync<JsonElement>("/plugins/active-module-runtime.json");
        Assert.DoesNotContain(runtime.GetProperty("modules").EnumerateArray(), module => module.GetProperty("moduleId").GetString() is "Workspace" or "Part" or "Document");
        var swagger = await fixture.Administrator.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        Assert.DoesNotContain(swagger.GetProperty("paths").EnumerateObject(), path => path.Name.StartsWith("/api/part/") || path.Name.StartsWith("/api/document/") || path.Name.StartsWith("/api/workspace/"));
        foreach (var path in new[] { "/api/part/parts", "/api/document/documents", "/api/workspace/spaces/public" })
        {
            using var response = await fixture.Administrator.GetAsync(path);
            // 正式 Host 对没有终态访问规则的路由应用 deny-all fallback，故不存在的业务路由返回 403。
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        await fixture.StopAsync();
        SetEnabled(fixture, "InforCenter", true);
        await fixture.StartAsync();
        foreach (var target in targets)
        {
            var read = await FeatureHttpTestRequests.ReadAsync(fixture.Administrator,
                Point(fixture, target.Model.ToLowerInvariant() + "." + target.Model.ToLowerInvariant() + ".read"),
                "/api/" + target.Model.ToLowerInvariant() + "/" + target.Model.ToLowerInvariant() + "s/" + target.Id);
            Assert.Equal(target.Model + "保留", read.GetProperty("fields").GetProperty("ENAME").GetString());
        }
        var scope = JsonSerializer.Serialize(new { position.spaceId, position.rootFolderId, linkIds = "[]" });
        var contents = await FeatureHttpTestRequests.ReadAsync(fixture.Administrator, Point(fixture, "workspace.spaces.access"),
            "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId=" + position.rootFolderId + "&queryScope=" + Uri.EscapeDataString(scope));
        Assert.Equal(targets.OrderBy(target => target.Model).Select(target => (target.Model, target.Id)),
            contents.GetProperty("items").EnumerateArray().Select(item => (item.GetProperty("associatedObject").GetProperty("modelKey").GetString()!, item.GetProperty("associatedObject").GetProperty("id").GetString()!)).OrderBy(target => target.Item1));
    }

    internal static void SetEnabled(FeatureCatalogHostFixture fixture, string moduleId, bool enabled)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.Combine(fixture.HostRoot, "modules", moduleId), "plugin.json")));
        var version = document.RootElement.GetProperty("version").GetString()!;
        var inventory = new[] { new InstalledPluginInventoryItem(moduleId, [version], version) };
        var connection = new SqlConnectionStringBuilder(fixture.ConnectionString);
        using var deploymentStore = new PersistenceDeploymentStore(Path.Combine(fixture.PluginRoot, "persistence"), PersistenceProvider.SqlServer,
            connection.DataSource.ToUpperInvariant() + "|" + connection.InitialCatalog.ToUpperInvariant());
        var deployment = HostPluginActivationDeployment.WithDeploymentStore(
            Path.Combine(fixture.HostRoot, "server/platform-modeling/metadata/storage-types.json"), PersistenceProvider.SqlServer, deploymentStore);
        var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(
            Path.Combine(fixture.PluginRoot, ".feature-gates", "external-plugins-disabled"), deployment));
        // 离线选版先选定已安装版本，再保留最终停用选择；RequestDisable 本身刻意保留旧 desired 版本。
        var selected = manager.RequestEnable(inventory, moduleId, version);
        Assert.True(selected.IsSuccess, selected.Message);
        var result = enabled ? selected : manager.RequestDisable(inventory, moduleId);
        Assert.True(result.IsSuccess, result.Message);
    }

    // 真正声明了新版的不同迁移路径；若该停用/屏蔽 owner 被错误编译或迁移，正式 compiler 必须失败。
    private static void SelectUnmigratableNewPartVersion(FeatureCatalogHostFixture fixture, bool enabled)
    {
        var path = Path.Combine(Path.Combine(fixture.HostRoot, "modules", "Part"), "plugin.json");
        var capsule = JsonNode.Parse(File.ReadAllText(path))!;
        capsule["version"] = "1.1.0";
        capsule["persistence"]!["migrations"]!["sqlServer"]!["assembly"] = "migrations/Part.NewVersion.SqlServerMigrations.dll";
        File.WriteAllText(path, capsule.ToJsonString());
        SetEnabled(fixture, "Part", enabled);
    }

    private static async Task<int> Scalar(FeatureCatalogHostFixture fixture, string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
    private static async Task Execute(FeatureCatalogHostFixture fixture, string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static FeaturePoint Point(FeatureCatalogHostFixture fixture, string behavior) => Assert.Single(fixture.Catalog, point => point.BehaviorKey == behavior);
    private static async Task<JsonElement> Post(FeatureCatalogHostFixture fixture, string behavior, string path, object? body)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, Point(fixture, behavior), HttpMethod.Post, path, body);
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
