using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class OfflineComponentDeploymentSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "完整历史快照覆盖SQL分块后的末行事实，不能只比较首块")]
    public async Task HistorySnapshot_CoversEveryRowAndDetectsFactChangesAfterFirstChunk()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        await fixture.StopAsync();
        var before = await History(fixture);
        using var snapshot = JsonDocument.Parse(before);
        Assert.Equal(await Scalar(fixture, "SELECT COUNT(*) FROM dbo.__NewPMigrationHistory;"), snapshot.RootElement.GetArrayLength());
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP (1) Position, MigrationId, ContentDigest FROM dbo.__NewPMigrationHistory ORDER BY Position DESC;";
        int position;
        string migrationId;
        string originalDigest;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            position = reader.GetInt32(0);
            migrationId = reader.GetString(1);
            originalDigest = reader.GetString(2);
        }
        var last = snapshot.RootElement.EnumerateArray().Last();
        Assert.Equal(position, last.GetProperty("Position").GetInt32());
        Assert.Equal(migrationId, last.GetProperty("MigrationId").GetString());
        // 仅读取首个传输块用于校准旧消费者盲区，不将它作为任何账本快照。
        command.CommandText = "SELECT * FROM dbo.__NewPMigrationHistory ORDER BY Position FOR JSON PATH;";
        var firstChunk = (string)(await command.ExecuteScalarAsync())!;
        Assert.True(before.Length > firstChunk.Length, "The real history did not exercise SQL JSON chunking.");
        Assert.True(before.LastIndexOf("\"MigrationId\":\"" + migrationId + "\"", StringComparison.Ordinal) >= firstChunk.Length);
        Assert.Equal(firstChunk, before[..firstChunk.Length]);
        var changedDigest = originalDigest == new string('0', 64) ? new string('1', 64) : new string('0', 64);
        command.Parameters.AddWithValue("@position", position);
        command.Parameters.AddWithValue("@digest", changedDigest);
        try
        {
            // Host 已停止且catalog由夹具独占；只校准末行，finally恢复原事实，不把篡改作为启动候选。
            command.CommandText = "UPDATE dbo.__NewPMigrationHistory SET ContentDigest=@digest WHERE Position=@position;";
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
            var after = await History(fixture);
            using var changed = JsonDocument.Parse(after);
            Assert.Equal(snapshot.RootElement.GetArrayLength(), changed.RootElement.GetArrayLength());
            Assert.Equal(changedDigest, changed.RootElement.EnumerateArray().Last().GetProperty("ContentDigest").GetString());
            Assert.Equal(firstChunk, after[..firstChunk.Length]);
            Assert.NotEqual(before, after);
        }
        finally
        {
            command.CommandText = "UPDATE dbo.__NewPMigrationHistory SET ContentDigest=@digest WHERE Position=@position;";
            command.Parameters["@digest"].Value = originalDigest;
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        Assert.Equal(before, await History(fixture));
        await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "history-snapshot-calibration.json"), JsonSerializer.Serialize(new
        {
            rows = snapshot.RootElement.GetArrayLength(), lastPosition = position, lastMigrationId = migrationId,
            scalarFirstChunkLength = firstChunk.Length, completeJsonLength = before.Length,
            lastFactOffset = before.LastIndexOf("\"MigrationId\":\"" + migrationId + "\"", StringComparison.Ordinal)
        }));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实成员移组和解除归组保留自身选择与数据库身份")]
    public async Task MoveAndUngroup_RecomputeOwnChoiceAndPreserveRealData()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        var partId = await CreatePart(fixture);
        var history = await History(fixture);
        await fixture.StopAsync();
        Assert.Equal(0, (await Desired(fixture, ("InforCenter", false), ("Workspace", false))).ExitCode);
        await fixture.StartAsync();
        Assert.DoesNotContain(fixture.Catalog, point => point.ModuleId == "Part");
        await fixture.StopAsync();
        Composition(fixture, "InforCenter", ["Workspace"]);
        Composition(fixture, "Document", ["Part"]);
        // Document 已解除归组且自身原选择为 true；Part 移入启用祖先，无第二次 enable。
        await fixture.StartAsync();
        Assert.Contains(fixture.Catalog, point => point.ModuleId == "Part");
        await AssertPart(fixture, partId);
        var runtime = await fixture.Administrator.GetFromJsonAsync<JsonElement>("/plugins/active-module-runtime.json");
        Assert.Equal("Document", Assert.Single(runtime.GetProperty("compositionGraph").EnumerateArray(), item => item.GetProperty("moduleId").GetString() == "Part").GetProperty("parentModuleId").GetString());
        await fixture.StopAsync();
        Composition(fixture, "InforCenter", ["Workspace", "Part"]);
        Composition(fixture, "Document", []);
        await fixture.StartAsync();
        Assert.DoesNotContain(fixture.Catalog, point => point.ModuleId == "Part");
        await fixture.StopAsync();
        Composition(fixture, "InforCenter", ["Workspace"]);
        await fixture.StartAsync();
        Assert.Contains(fixture.Catalog, point => point.ModuleId == "Part");
        await AssertPart(fixture, partId);
        await fixture.StopAsync();
        var partVersion = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.HostRoot, "modules", "Part", "plugin.json")))!["version"]!.GetValue<string>();
        Assert.Equal(0, (await Offline(fixture, "select", "-ModuleId", "Part", "-Version", partVersion, "-Enabled", "false")).ExitCode);
        Assert.Equal(0, (await Offline(fixture, "select", "-ModuleId", "Part", "-Version", partVersion)).ExitCode);
        await fixture.StartAsync();
        Assert.DoesNotContain(fixture.Catalog, point => point.ModuleId == "Part");
        Assert.Equal(history, await History(fixture));
        Assert.Equal(1, await Scalar(fixture, "SELECT COUNT(*) FROM dbo.Part WHERE EID='" + partId + "';"));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "归档不能替代已卸载成员；解除引用与活动依赖后保留数据正常重启")]
    public async Task Uninstall_RejectsRetainedCompositionAndActiveDependenciesThenPreservesStorage()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        var partId = await CreatePart(fixture);
        await fixture.StopAsync();
        var history = await History(fixture);
        var statePath = Assert.Single(Directory.GetFiles(Path.Combine(fixture.PluginRoot, "persistence"), "state.json", SearchOption.AllDirectories));
        var state = JsonNode.Parse(File.ReadAllText(statePath))!;
        var assetPath = Path.Combine(Path.GetDirectoryName(statePath)!, state["successful"]!.GetValue<string>() + ".json");
        var asset = File.ReadAllBytes(assetPath);
        Assert.Equal(0, (await Desired(fixture, ("InforCenter", false), ("Workspace", false))).ExitCode);
        var referenced = await Offline(fixture, "uninstall", "-ModuleId", "Part");
        Assert.NotEqual(0, referenced.ExitCode);
        Assert.Contains("MODULE_COMPOSITION_MEMBER_MISSING", referenced.Output);
        Assert.True(Directory.Exists(Path.Combine(fixture.HostRoot, "modules", "Part")));
        Assert.Equal(history, await History(fixture));
        Assert.Equal(asset, File.ReadAllBytes(assetPath));
        Composition(fixture, "InforCenter", ["Workspace", "Document"]);
        Assert.Equal(0, (await Desired(fixture, ("InforCenter", true), ("Workspace", true))).ExitCode);
        var dependedOn = await Offline(fixture, "uninstall", "-ModuleId", "Part");
        Assert.NotEqual(0, dependedOn.ExitCode);
        Assert.Contains("MODULE_DEPENDENCY_MISSING", dependedOn.Output);
        Assert.True(Directory.Exists(Path.Combine(fixture.HostRoot, "modules", "Part")));
        Assert.Equal(history, await History(fixture));
        Assert.Equal(0, (await Desired(fixture, ("Workspace", false))).ExitCode);
        Assert.Equal(0, (await Offline(fixture, "uninstall", "-ModuleId", "Part")).ExitCode);
        Assert.False(Directory.Exists(Path.Combine(fixture.HostRoot, "modules", "Part")));
        await fixture.StartAsync();
        Assert.Contains(fixture.Catalog, point => point.ModuleId == "Document");
        Assert.DoesNotContain(fixture.Catalog, point => point.ModuleId is "Part" or "Workspace");
        Assert.Equal(1, await Scalar(fixture, "SELECT COUNT(*) FROM dbo.Part WHERE EID='" + partId + "';"));
        Assert.Equal(history, await History(fixture));
        Assert.Equal(asset, File.ReadAllBytes(assetPath));
        await fixture.StopAsync();
        // 代码目录和组合声明都已删除，仍不能接管保留 owner 的全局模型名。
        RenameDocumentToPart(fixture);
        await fixture.StartAsync(expectFailure: true);
        Assert.Contains("PERSISTENCE_DEPLOYED_IDENTITY_CONFLICT", fixture.LastError);
        Assert.Equal(history, await History(fixture));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式离线请求不得绕过平台必需性")]
    public async Task RequiredPlatformMember_UninstallRejectedAndDisabledCandidateCannotStart()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        await fixture.StopAsync();
        var history = await History(fixture);
        var uninstall = await Offline(fixture, "uninstall", "-ModuleId", "Audit");
        Assert.NotEqual(0, uninstall.ExitCode);
        Assert.Contains("PLATFORM_REQUIRED_MODULE_MISSING", uninstall.Output);
        Assert.True(Directory.Exists(Path.Combine(fixture.HostRoot, "modules", "Audit")));
        Assert.Equal(0, (await Desired(fixture, ("Platform", false))).ExitCode);
        await fixture.StartAsync(expectFailure: true);
        // Platform 屏蔽全部必需能力，正式 Canonical 先报告真实活动业务依赖；必需级别本身在 Kernel 验证。
        Assert.Contains("MODULE_DEPENDENCY_MISSING", fixture.LastError);
        Assert.Contains("Module 'Document' depends on missing module 'Authentication'", fixture.LastError);
        Assert.Equal(history, await History(fixture));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "正式离线选择只更新desired，重启后按完整候选生效")]
    public async Task OfflineDesired_DisablesRealMembersAndRejectsChangesWhileHostRuns()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        var online = await Offline(fixture, "disable", "-ModuleId", "Part");
        Assert.NotEqual(0, online.ExitCode);
        Assert.Contains("PLUGIN_HOST_MUST_BE_STOPPED", online.Output);
        await fixture.StopAsync();
        var before = await History(fixture);
        var desired = await Desired(fixture, ("Workspace", false), ("Part", false));
        Assert.Equal(0, desired.ExitCode);
        Assert.Contains("PendingRestart", desired.Output);
        await fixture.StartAsync();
        Assert.DoesNotContain(fixture.Catalog, point => point.ModuleId is "Workspace" or "Part");
        Assert.Contains(fixture.Catalog, point => point.ModuleId == "Document");
        Assert.Equal(before, await History(fixture));
        Assert.Equal(1, await Scalar(fixture, "SELECT COUNT(*) FROM sys.tables WHERE name=N'Part';"));
        using var response = await fixture.Administrator.PostAsJsonAsync("/api/plugin-ops/modules/Part/enable", new { version = "1.0.0" });
        Assert.Equal(501, (int)response.StatusCode);
        Assert.Contains("PLUGIN_RUNTIME_OPERATIONS_UNSUPPORTED", await response.Content.ReadAsStringAsync());
    }

    private static async Task<(int ExitCode, string Output)> Desired(FeatureCatalogHostFixture fixture, params (string Id, bool Enabled)[] selections)
    {
        var file = Path.Combine(fixture.EvidenceRoot, "desired-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(selections.Select(selection => new
        {
            moduleId = selection.Id, version = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.HostRoot, "modules", selection.Id, "plugin.json")))!["version"]!.GetValue<string>(), enabled = selection.Enabled
        })));
        return await Offline(fixture, "desired", "-CandidatePath", file);
    }

    internal static async Task<(int ExitCode, string Output)> Offline(FeatureCatalogHostFixture fixture, string action, params string[] options)
    {
        var start = new ProcessStartInfo("pwsh.exe") { WorkingDirectory = fixture.HostRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(fixture.RepositoryRoot, "scripts", "manage-unified-plugin.ps1"), "-Action", action, "-HostPath", Path.Combine(fixture.HostRoot, "HostApi.dll"), "-InstallRoot", fixture.PluginRoot }.Concat(options)) start.ArgumentList.Add(argument);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        // 离线确认与正常 Host 使用同一 catalog，不能读取发行物默认数据库的部署状态。
        start.Environment["ConnectionStrings__SqlServer"] = fixture.ConnectionString;
        start.Environment["Platform__ExternalPluginsEnabled"] = "false";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        var output = (await stdout + await stderr).Replace(fixture.ConnectionString, "[connection]", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "offline-" + action + "-" + Guid.NewGuid().ToString("N") + ".log"), output);
        return (process.ExitCode, output);
    }

    private static async Task<string> History(FeatureCatalogHostFixture fixture)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT * FROM dbo.__NewPMigrationHistory ORDER BY Position FOR JSON PATH;", connection);
        // FOR JSON PATH 把长JSON分为多行；完整账本比较必须消费全部传输块。
        await using var reader = await command.ExecuteReaderAsync();
        var json = new StringBuilder();
        while (await reader.ReadAsync()) json.Append(reader.GetString(0));
        return json.ToString();
    }

    private static async Task<int> Scalar(FeatureCatalogHostFixture fixture, string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static void Composition(FeatureCatalogHostFixture fixture, string id, string[] members)
    {
        var path = Path.Combine(fixture.HostRoot, "modules", id, "plugin.json");
        var capsule = JsonNode.Parse(File.ReadAllText(path))!;
        capsule["composition"] = new JsonObject { ["members"] = new JsonArray(members.Select(member => JsonValue.Create(member)).ToArray()) };
        File.WriteAllText(path, capsule.ToJsonString());
    }

    private static async Task<string> CreatePart(FeatureCatalogHostFixture fixture)
    {
        await new FeatureHttpTestRequests(fixture).InitializeOrganizationAsync(fixture.Administrator);
        var point = Assert.Single(fixture.Catalog, point => point.BehaviorKey == "part.part.create");
        using var response = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, point, HttpMethod.Post, "/api/part/parts", new { ECODE = "OFFLINE264", ENAME = "离线保留264" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fields").GetProperty("EID").GetString()!;
    }

    private static async Task AssertPart(FeatureCatalogHostFixture fixture, string id)
    {
        var point = Assert.Single(fixture.Catalog, point => point.BehaviorKey == "part.part.read");
        var result = await FeatureHttpTestRequests.ReadAsync(fixture.Administrator, point, "/api/part/parts/" + id);
        Assert.Equal(id, result.GetProperty("fields").GetProperty("EID").GetString());
        Assert.Equal("离线保留264", result.GetProperty("fields").GetProperty("ENAME").GetString());
    }

    private static void RenameDocumentToPart(FeatureCatalogHostFixture fixture)
    {
        var root = Path.Combine(fixture.HostRoot, "modules", "Document");
        var path = Path.Combine(root, "metadata", "models", "Document.model.json");
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var model = document["Document"]!;
        document.Remove("Document");
        document["Part"] = model;
        File.WriteAllText(Path.Combine(root, "metadata", "models", "Part.model.json"), document.ToJsonString());
        File.Delete(path);
        var capsulePath = Path.Combine(root, "plugin.json");
        var capsule = JsonNode.Parse(File.ReadAllText(capsulePath))!;
        capsule["persistence"]!["models"] = new JsonArray("metadata/models/Part.model.json");
        File.WriteAllText(capsulePath, capsule.ToJsonString());
    }
}
