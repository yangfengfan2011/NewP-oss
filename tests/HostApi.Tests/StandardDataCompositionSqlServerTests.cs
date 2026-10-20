using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.PluginPlatform.StandardData;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Modeling;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class StandardDataCompositionSqlServerTests
{
    private const string ModuleId = "Partner.AssetCatalog";
    private const string ApiRoute = "/api/plugins/partner-asset-catalog/assets";
    [EnvironmentGatedFact("NEWP_RUN_EXTERNAL_PLUGIN_SQLSERVER_INTEGRATION", "真实包经规范编译器形成固定字段和同一EF provider目标")]
    public void PublicPackage_CompilesCanonicalProviderTarget()
    {
        var package = Environment.GetEnvironmentVariable("NEWP_EXTERNAL_PLUGIN_PACKAGE");
        Assert.True(File.Exists(package));
        var root = Directory.CreateTempSubdirectory("external266_target_").FullName;
        try
        {
            var packageRoot = Path.Combine(root, "packages", "Partner.AssetCatalog", "1.0.0");
            ZipFile.ExtractToDirectory(package!, packageRoot);
            var catalog = new PlatformApiHostCatalog("1.0.0", ["standard-data.v1"], [new("1.0.0", PlatformApiReleaseIdentity.ReleaseDigest)]);
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !Directory.Exists(Path.Combine(repository.FullName, ".git"))) repository = repository.Parent;
            var releaseRoot = Path.Combine(repository!.FullName, "apps", "host-api", "bin", "Release", "net10.0");
            var builtIns = TrustedBuiltInModuleSource.CreateForHostDistribution(releaseRoot, Path.Combine(releaseRoot, "modules"));
            // 与正式Host同源编译，保留模板投影的功能、资源和读取合同，不手造丢字段的第二候选。
            var modules = new StandardDataCandidatePlanCompiler(builtIns, new StandardDataPackageInspector(catalog)).Compile(
                [new("Partner.AssetCatalog", ["1.0.0"], "1.0.0", new Dictionary<string, string> { ["1.0.0"] = packageRoot })],
                new([new("Partner.AssetCatalog", "1.0.0", true)], null, null, false));
            var external = Assert.Single(modules.Modules, module => module.ModuleId == ModuleId);
            Assert.NotEmpty(external.DataExecutions);
            Assert.Contains(modules.ActionDataAccessCatalog, requirement => requirement.ModuleId == ModuleId && requirement.Provider == "standardCrud.update");
            var result = new PersistencePlanCompiler(Path.Combine(repository!.FullName, "server", "platform-modeling", "metadata", "storage-types.json")).Compile(modules, PersistenceProvider.SqlServer);
            Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
            var model = Assert.Single(result.Plan!.Models, model => model.OwnerModuleId == "Partner.AssetCatalog");
            Assert.Equal("Asset", model.LogicalKey);
            Assert.Equal("np_partner_assetcatalog", model.SchemaName);
            Assert.Equal(FieldValueType.User, Assert.Single(model.Fields, field => field.Name == "CreatedBy").ValueType);
            using var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>().UseSqlServer("Server=unused;Database=unused").Options, result.Plan);
            Assert.Equal(128, db.Model.FindEntityType("Asset")!.FindProperty("CreatedBy")!.GetMaxLength());
            var target = new ICP.Platform.Persistence.EfCore.ProviderTargets.EfProviderTargetSource(db, result.Plan).Read();
            var table = Assert.Single(target.Tables, table => table.OwnerModuleId == "Partner.AssetCatalog");
            Assert.Equal(["EID", "Code", "CreatedAt", "CreatedBy", "Name", "PurchasedAt", "UpdatedAt", "UpdatedBy"], table.Columns.Select(column => column.Name));
            Assert.Equal(128, Assert.Single(table.Columns, column => column.Name == "CreatedBy").MaxLength);
            using var services = new ServiceCollection().AddPlatformPersistence(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Persistence:Provider"] = "SqlServer", ["ConnectionStrings:SqlServer"] = "Server=unused;Database=unused" }).Build(), result.Plan).BuildServiceProvider();
            using var scope = services.CreateScope();
            var actual = scope.ServiceProvider.GetRequiredService<ICP.Platform.Persistence.ProviderTargets.IProviderTargetSource>().Read();
            var actualTable = Assert.Single(actual.Tables, table => table.OwnerModuleId == "Partner.AssetCatalog");
            Assert.Equal(128, Assert.Single(actualTable.Columns, column => column.Name == "CreatedBy").MaxLength);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [EnvironmentGatedFact("NEWP_RUN_EXTERNAL_PLUGIN_SQLSERVER_INTEGRATION", "实际公开包经正式离线安装与Host迁移后可读写Asset")]
    public async Task PublicPackage_InstallsAndWritesThroughFormalHost()
    {
        var package = Environment.GetEnvironmentVariable("NEWP_EXTERNAL_PLUGIN_PACKAGE");
        Assert.True(File.Exists(package), "Set NEWP_EXTERNAL_PLUGIN_PACKAGE to the public release smoke .npp output.");
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var repository = output;
        while (repository is not null && !Directory.Exists(Path.Combine(repository.FullName, ".git"))) repository = repository.Parent;
        var sourceHost = Path.Combine(repository!.FullName, "apps", "host-api", "bin", output.Parent!.Name, output.Name);
        // 此消费者不改Example功能反例，直接保留本轮正式Host发行物的完整装配输入。
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([], sourceHostRoot: sourceHost);
        await fixture.StopAsync();
        fixture.ExternalPluginsEnabled = true;
        var packageRoot = Path.Combine(fixture.EvidenceRoot, "external-package");
        ZipFile.ExtractToDirectory(package!, packageRoot);
        Assert.Equal(0, (await Offline(fixture, "install", "-PackagePath", packageRoot)).ExitCode);
        Assert.Equal(0, (await Offline(fixture, "select", "-ModuleId", "Partner.AssetCatalog", "-Version", "1.0.0", "-Enabled", "true")).ExitCode);
        try { await fixture.StartAsync(); }
        catch
        {
            foreach (var file in Directory.GetFiles(Path.Combine(fixture.PluginRoot, "persistence"), "*.json", SearchOption.AllDirectories))
            {
                var asset = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file));
                var model = asset?["plan"]?["models"]?.AsArray().SingleOrDefault(model => model?["ownerModuleId"]?.GetValue<string>() == "Partner.AssetCatalog");
                if (model is not null) File.WriteAllText(Path.Combine(fixture.EvidenceRoot, "external266-pending-model.json"), model.ToJsonString());
            }
            throw;
        }
        const string resource = "Partner.AssetCatalog.Asset";
        using var manifest = await fixture.Administrator.GetFromJsonAsync<JsonDocument>("/plugins/metadata-runtime-manifest.json");
        var owner = Assert.Single(manifest!.RootElement.GetProperty("modules").EnumerateArray(), module => module.GetProperty("moduleId").GetString() == ModuleId);
        var declarations = owner.GetProperty("dataAccess").Deserialize<ActionDataAccessRequirement[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        ActionDataAccessRequirement Declaration(string execution, FeaturePoint point) => Assert.Single(declarations,
            item => item.Execution == resource + "." + execution && item.FeaturePositions.Contains(point.Position, StringComparer.Ordinal));
        var point = Assert.Single(fixture.Catalog, point => point.BehaviorKey == resource + ".create");
        using (var missing = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, point, HttpMethod.Post, ApiRoute,
            new { Code = "MISSING321", Name = "缺少来源" })) Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var response = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, point, HttpMethod.Post, ApiRoute,
            new { Code = "EXTERNAL266", Name = "真实外仓266" }, Declaration("create", point));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("fields").GetProperty("EID").GetString()!;
        // 编辑准备沿真实行动作来源消费update.prepare，正式详情仍保留独立read。
        var edit = Assert.Single(fixture.Catalog, item => item.BehaviorKey == resource + ".update" && item.ActionScope == "list.rowActions");
        var update = Declaration("update", edit);
        using (var missing = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, edit, HttpMethod.Get, ApiRoute + "/" + id))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var detailResponse = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, edit, HttpMethod.Get, ApiRoute + "/" + id, dataAccess: update);
        Assert.True(detailResponse.StatusCode == HttpStatusCode.OK, await detailResponse.Content.ReadAsStringAsync());
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("真实外仓266", detail.GetProperty("fields").GetProperty("Name").GetString());
        using (var changed = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, edit, HttpMethod.Put, ApiRoute + "/" + id,
            new { Name = "正式写入321" }, update)) Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());
        var authorize = Assert.Single(fixture.Catalog, item => item.BehaviorKey == resource + ".authorize");
        var manage = Declaration("authorization.manage", authorize);
        var permissionRoute = "/api/authorization/data-scope/objects/" + resource + "/" + id;
        using (var missing = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, authorize, HttpMethod.Get, permissionRoute))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var permissions = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, authorize, HttpMethod.Get, permissionRoute, dataAccess: manage))
            Assert.True(permissions.IsSuccessStatusCode, await permissions.Content.ReadAsStringAsync());
        using (var missing = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, authorize, HttpMethod.Put, permissionRoute,
            new ICP.Platform.Modules.Authorization.DTOs.DataScopeObjectDto(resource, id, []))) Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var saved = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, authorize, HttpMethod.Put, permissionRoute,
            new ICP.Platform.Modules.Authorization.DTOs.DataScopeObjectDto(resource, id, []), manage))
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(*) FROM np_partner_assetcatalog.Asset WHERE EID=@id;", connection);
        command.Parameters.AddWithValue("@id", id);
        Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
        await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "external266-first-slice.json"),
            JsonSerializer.Serialize(new { packageSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(package!))), id, model = "Asset", owner = "Partner.AssetCatalog", fixture.EvidenceRoot }));
    }

    [EnvironmentGatedFact("NEWP_RUN_EXTERNAL_PLUGIN_SQLSERVER_INTEGRATION", "公开版本归组、屏蔽恢复、解除引用卸载保留真实数据与可信资产")]
    public async Task PublicVersions_ComposeRestoreAndUninstallWithoutChangingDataOrHistory()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        var initialize = fixture.Catalog.First(point => point.BehaviorKey == "organization.company.initialize");
        using var initialized = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, initialize, HttpMethod.Post, "/api/organization/initialization");
        Assert.True(initialized.IsSuccessStatusCode, await initialized.Content.ReadAsStringAsync());
        string CurrentVersion(string module) => JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.HostRoot, "modules", module, "plugin.json")))!["version"]!.GetValue<string>();
        var partVersion = CurrentVersion("Part");
        var workspaceVersion = CurrentVersion("Workspace");
        var partPoint = Assert.Single(fixture.Catalog, point => point.BehaviorKey == "part.part.create");
        using var partResponse = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, partPoint, HttpMethod.Post, "/api/part/parts", new { ECODE = "EXTERNAL266PART", ENAME = "外仓组合真实Part266" });
        Assert.True(partResponse.IsSuccessStatusCode, await partResponse.Content.ReadAsStringAsync());
        var partId = (await partResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fields").GetProperty("EID").GetString()!;
        await fixture.StopAsync();
        fixture.ExternalPluginsEnabled = true;
        await Install(fixture, "1.0.0");
        await Select(fixture, ModuleId, "1.0.0", true);
        await fixture.StartAsync();
        var create = Assert.Single(fixture.Catalog, point => point.BehaviorKey == ModuleId + ".Asset.create");
        using var response = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, create, HttpMethod.Post, ApiRoute, new { Code = "LIFECYCLE266", Name = "独立作者保留266" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var assetId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fields").GetProperty("EID").GetString()!;
        var history = await History(fixture);
        await fixture.StopAsync();
        await Install(fixture, "1.1.0");
        await fixture.StartAsync();
        var part = await Node(fixture, "Part");
        Assert.Equal("InforCenter", part.GetProperty("parentModuleId").GetString());
        Assert.Equal("1.0.0", (await Node(fixture, ModuleId)).GetProperty("version").GetString());
        await fixture.StopAsync();
        Composition(fixture, "InforCenter", ["Workspace", "Document", ModuleId]);
        await Select(fixture, ModuleId, "1.1.0");
        await fixture.StartAsync();
        Assert.Equal(ModuleId, (await Node(fixture, "Part")).GetProperty("parentModuleId").GetString());
        await AssertAsset(fixture, assetId);
        await fixture.StopAsync();
        // Workspace的真实业务依赖要求与Part一同暂停；父级屏蔽不覆盖Part自身选择。
        await Desired(fixture, (ModuleId, "1.1.0", false), ("Workspace", workspaceVersion, false));
        await fixture.StartAsync();
        Assert.DoesNotContain(fixture.Catalog, point => point.ModuleId is ModuleId or "Part");
        part = await Node(fixture, "Part");
        Assert.True(part.GetProperty("enabled").GetBoolean());
        using var unavailable = await fixture.Administrator.GetAsync(ApiRoute + "/" + assetId);
        Assert.True(unavailable.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        Assert.Equal(history, await History(fixture));
        await fixture.StopAsync();
        await Select(fixture, "Part", partVersion, false);
        await Select(fixture, ModuleId, "1.1.0", true);
        await fixture.StartAsync();
        Assert.Contains(fixture.Catalog, point => point.ModuleId == ModuleId);
        Assert.DoesNotContain(fixture.Catalog, point => point.ModuleId == "Part");
        await AssertAsset(fixture, assetId);
        await fixture.StopAsync();
        await Select(fixture, "Part", partVersion, true);
        await Select(fixture, ModuleId, "1.1.0", false);
        await Install(fixture, "1.2.0");
        await fixture.StartAsync();
        Assert.Equal(ModuleId, (await Node(fixture, "Part")).GetProperty("parentModuleId").GetString());
        Assert.Equal("1.1.0", (await Node(fixture, ModuleId)).GetProperty("version").GetString());
        await fixture.StopAsync();
        await Select(fixture, ModuleId, "1.2.0");
        await fixture.StartAsync();
        // 选中解除引用版本后Part按原true选择自动恢复，Asset自身仍为false。
        Assert.Equal(JsonValueKind.Null, (await Node(fixture, "Part")).GetProperty("parentModuleId").ValueKind);
        Assert.Contains(fixture.Catalog, point => point.ModuleId == "Part");
        Assert.DoesNotContain(fixture.Catalog, point => point.ModuleId == ModuleId);
        var statePath = Assert.Single(Directory.GetFiles(Path.Combine(fixture.PluginRoot, "persistence"), "state.json", SearchOption.AllDirectories));
        var successful = JsonNode.Parse(File.ReadAllText(statePath))!["successful"]!.GetValue<string>();
        var assetPath = Path.Combine(Path.GetDirectoryName(statePath)!, successful + ".json");
        var archive = File.ReadAllBytes(assetPath);
        var deployed = JsonNode.Parse(archive)!;
        Assert.Equal("1.1.0", Assert.Single(deployed["modules"]!.AsArray(), module => module!["moduleId"]!.GetValue<string>() == ModuleId)!["version"]!.GetValue<string>());
        Assert.Contains(deployed["plan"]!["models"]!.AsArray(), model => model!["name"]!.GetValue<string>() == "Asset" && model["ownerModuleId"]!.GetValue<string>() == ModuleId);
        await fixture.StopAsync();
        await Select(fixture, "InforCenter", "1.0.0", false);
        var referenced = await Offline(fixture, "uninstall", true, "-ModuleId", ModuleId);
        Assert.Contains("MODULE_COMPOSITION_MEMBER_MISSING", referenced.Output);
        Assert.True(Directory.Exists(Path.Combine(fixture.PluginRoot, "packages", ModuleId)));
        Assert.Equal(history, await History(fixture));
        Assert.Equal(archive, File.ReadAllBytes(assetPath));
        Composition(fixture, "InforCenter", ["Workspace", "Document", "Part"]);
        await Select(fixture, "InforCenter", "1.0.0", true);
        await Offline(fixture, "uninstall", "-ModuleId", ModuleId);
        await fixture.StartAsync();
        Assert.DoesNotContain(fixture.Catalog, point => point.ModuleId == ModuleId);
        Assert.Contains(fixture.Catalog, point => point.ModuleId == "Part");
        Assert.Equal(1, await Count(fixture, "SELECT COUNT(*) FROM np_partner_assetcatalog.Asset WHERE EID=@id;", assetId));
        Assert.Equal(1, await Count(fixture, "SELECT COUNT(*) FROM dbo.Part WHERE EID=@id;", partId));
        Assert.Equal(history, await History(fixture));
        Assert.Equal(archive, File.ReadAllBytes(assetPath));
        Assert.False(Directory.Exists(Path.Combine(fixture.PluginRoot, "packages", ModuleId)));
        await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "external266-lifecycle.json"), JsonSerializer.Serialize(new
            { assetId, partId, inactiveEndpointStatus = (int)unavailable.StatusCode, archivedSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(archive)), historySha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(history))) }));
    }

    // 所有版本都来自同一次公开Tool发布烟测，解包只消费.npp字节，不拼装installed契约。
    private static async Task Install(FeatureCatalogHostFixture fixture, string version)
    {
        var package = Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("NEWP_EXTERNAL_PLUGIN_PACKAGE"))!, ModuleId + "@" + version + ".npp");
        Assert.True(File.Exists(package), "Run public release smoke and select its complete version set.");
        var root = Path.Combine(fixture.EvidenceRoot, "package-" + version);
        ZipFile.ExtractToDirectory(package, root);
        await Offline(fixture, "install", "-PackagePath", root);
    }

    private static Task<(int ExitCode, string Output)> Select(FeatureCatalogHostFixture fixture, string id, string version, bool? enabled = null) =>
        Offline(fixture, "select", ["-ModuleId", id, "-Version", version, .. enabled is null ? Array.Empty<string>() : new[] { "-Enabled", enabled.Value ? "true" : "false" }]);

    private static async Task Desired(FeatureCatalogHostFixture fixture, params (string Id, string Version, bool Enabled)[] selections)
    {
        var path = Path.Combine(fixture.EvidenceRoot, "external266-desired-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(selections.Select(selection => new { moduleId = selection.Id, version = selection.Version, enabled = selection.Enabled })));
        await Offline(fixture, "desired", "-CandidatePath", path);
    }

    private static void Composition(FeatureCatalogHostFixture fixture, string id, string[] members)
    {
        var path = Path.Combine(fixture.HostRoot, "modules", id, "plugin.json");
        var capsule = JsonNode.Parse(File.ReadAllText(path))!;
        capsule["composition"] = new JsonObject { ["members"] = new JsonArray(members.Select(member => JsonValue.Create(member)).ToArray()) };
        File.WriteAllText(path, capsule.ToJsonString());
    }

    private static async Task<JsonElement> Node(FeatureCatalogHostFixture fixture, string id)
    {
        var runtime = await fixture.Administrator.GetFromJsonAsync<JsonElement>("/plugins/active-module-runtime.json");
        return Assert.Single(runtime.GetProperty("compositionGraph").EnumerateArray(), node => node.GetProperty("moduleId").GetString() == id);
    }

    private static async Task AssertAsset(FeatureCatalogHostFixture fixture, string id)
    {
        var point = Assert.Single(fixture.Catalog, point => point.BehaviorKey == ModuleId + ".Asset.read");
        var detail = await FeatureHttpTestRequests.ReadAsync(fixture.Administrator, point, ApiRoute + "/" + id);
        Assert.Equal("独立作者保留266", detail.GetProperty("fields").GetProperty("Name").GetString());
    }

    // 两份正式账本完整拼接全部FOR JSON传输块，捕捉末行事实；不用scalar截断快照。
    private static async Task<string> History(FeatureCatalogHostFixture fixture)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var result = new StringBuilder();
        foreach (var sql in new[] { "SELECT * FROM dbo.__NewPMigrationHistory ORDER BY Position FOR JSON PATH;", "SELECT * FROM dbo.__EFMigrationsHistory ORDER BY MigrationId FOR JSON PATH;" })
        {
            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) result.Append(reader.GetString(0));
            result.Append('\n');
        }
        return result.ToString();
    }

    private static async Task<int> Count(FeatureCatalogHostFixture fixture, string sql, string id)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    // 与正常Host相同发行物和安装根，仅在已停止期间写desired/installed；配置开关一致。
    private static Task<(int ExitCode, string Output)> Offline(FeatureCatalogHostFixture fixture, string action, params string[] options) =>
        Offline(fixture, action, false, options);

    private static async Task<(int ExitCode, string Output)> Offline(FeatureCatalogHostFixture fixture, string action,
        bool expectFailure, params string[] options)
    {
        var start = new ProcessStartInfo("pwsh.exe") { WorkingDirectory = fixture.HostRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(fixture.RepositoryRoot, "scripts", "manage-unified-plugin.ps1"), "-Action", action, "-HostPath", Path.Combine(fixture.HostRoot, "HostApi.dll"), "-InstallRoot", fixture.PluginRoot }.Concat(options)) start.ArgumentList.Add(argument);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["Platform__ExternalPluginsEnabled"] = "true";
        start.Environment["ConnectionStrings__SqlServer"] = fixture.ConnectionString;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        var output = (await stdout + await stderr).Replace(fixture.ConnectionString, "[connection]", StringComparison.Ordinal)
            .Replace(fixture.AdministratorPassword, "[credential]", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "external266-offline-" + action + "-" + Guid.NewGuid().ToString("N") + ".log"), output);
        Assert.True(expectFailure ? process.ExitCode != 0 : process.ExitCode == 0, output);
        return (process.ExitCode, output);
    }
}
