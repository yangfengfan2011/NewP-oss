using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.PluginPlatform.ComponentBundles;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class NativeComponentSelectionProcessTests
{
    [EnvironmentGatedFact("NEWP_RUN_NATIVE_SELECTION_PROCESS", "固定未预装发行物、真实D/Part版本包与正式重启/API消费")]
    public async Task FixedHost_ConsumesIndependentMemberSelectionAndExplicitBundleDecisionsAcrossSwitches()
    {
        var target = Required("NEWP_NATIVE_SELECTION_HOST");
        var initial = Required("NEWP_NATIVE_SELECTION_BUNDLE_V10");
        var independent = Required("NEWP_NATIVE_SELECTION_BUNDLE_V11");
        var higher = Required("NEWP_NATIVE_SELECTION_BUNDLE_V12");
        var omitted = Required("NEWP_NATIVE_SELECTION_BUNDLE_OMITTED");
        var inspector = new ComponentBundleInspector();
        var first = inspector.Inspect(initial).Manifest;
        var upgraded = inspector.Inspect(independent).Manifest;
        var next = inspector.Inspect(higher).Manifest;
        Assert.Equal("Part", upgraded.RootModuleId);
        Assert.Single(upgraded.Components);
        Assert.Equal("1.1.0", upgraded.Components[0].Version);
        var parent = first.Components.Single(item => item.ModuleId == "InforCenter");
        Assert.Equal(parent.ContentDigest, next.Components.Single(item => item.ModuleId == "InforCenter").ContentDigest);
        Assert.NotEqual(first.ContentDigest, next.ContentDigest);
        Assert.Equal(new[] { "authoring/newp.plugin.json", "plugin.json" }, parent.Files.Keys.Order().ToArray());
        Assert.False(File.Exists(Path.Combine(target, "PartModule.dll")));
        Assert.False(Directory.Exists(Path.Combine(target, "modules", "Part")));
        Assert.False(Directory.Exists(Path.Combine(target, "modules", "InforCenter")));

        IReadOnlyDictionary<string, string>? fixedFiles = null;
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([], async fixture =>
        {
            // 仅替换本夹具新建的发行副本；不构建或修改调用方提供的固定目标。
            Assert.True(PathContainmentCheck(fixture.EvidenceRoot, fixture.HostRoot));
            Directory.Delete(fixture.HostRoot, recursive: true);
            CopyFiles(target, fixture.HostRoot);
            fixedFiles = HashFiles(fixture.HostRoot);
            Assert.Equal(0, (await Offline(fixture, "disable", "--module-id", "Workspace")).ExitCode);
            var desired = File.ReadAllBytes(DesiredPath(fixture));
            Assert.Equal(0, (await Offline(fixture, "install-native", "--package", initial)).ExitCode);
            Assert.Equal(desired, File.ReadAllBytes(DesiredPath(fixture)));
            Assert.Equal(0, (await Offline(fixture, "enable-bundle", "--package", initial)).ExitCode);
        });
        await AssertRuntime(fixture, "1.0.0", "1.0.0");
        var create = Assert.Single(fixture.Catalog, point => point.BehaviorKey == "part.part.create");
        using var response = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, create, HttpMethod.Post, "/api/part/parts",
            new { ECODE = "NATIVE273", ENAME = "独立版本保留" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fields").GetProperty("EID").GetString()!;
        await fixture.StopAsync();
        var parentFiles = HashFiles(Path.Combine(fixture.PluginRoot, "packages", "InforCenter", "1.0.0"));
        var beforeInstall = File.ReadAllBytes(DesiredPath(fixture));
        Assert.Equal(0, (await Offline(fixture, "install-native", "--package", independent)).ExitCode);
        Assert.Equal(beforeInstall, File.ReadAllBytes(DesiredPath(fixture)));
        Assert.Equal(0, (await Offline(fixture, "select", "--module-id", "Part", "--version", "1.1.0")).ExitCode);
        Assert.Equal(parentFiles, HashFiles(Path.Combine(fixture.PluginRoot, "packages", "InforCenter", "1.0.0")));
        await fixture.StartAsync();
        await AssertRuntime(fixture, "1.0.0", "1.1.0");
        await AssertPart(fixture, id);
        await fixture.StopAsync();

        // 安装和重打只追加库存；1.1同时遇到较低1.0与较高1.2都先裁决。
        foreach (var package in new[] { initial, higher })
        {
            var bytes = File.ReadAllBytes(DesiredPath(fixture));
            Assert.Equal(0, (await Offline(fixture, "install-native", "--package", package)).ExitCode);
            Assert.Equal(bytes, File.ReadAllBytes(DesiredPath(fixture)));
            var conflict = await Offline(fixture, "enable-bundle", "--package", package);
            Assert.NotEqual(0, conflict.ExitCode);
            Assert.Contains("PLUGIN_BUNDLE_VERSION_CHOICE_REQUIRED", conflict.Output);
            Assert.Contains("\"selectedVersion\":\"1.1.0\"", conflict.Output);
            Assert.Contains("\"moduleId\":\"Part\"", conflict.Output);
            Assert.Equal(bytes, File.ReadAllBytes(DesiredPath(fixture)));
            var decisions = await Decisions(fixture, new[] { new { moduleId = "Part", version = "1.1.0" } });
            Assert.Equal(0, (await Offline(fixture, "enable-bundle", "--package", package, "--decisions", decisions)).ExitCode);
            Assert.Equal("1.1.0", Desired(fixture, "Part").Version);
            Assert.False(Desired(fixture, "Workspace").Enabled);
        }
        Assert.Equal(0, (await Offline(fixture, "disable", "--module-id", "Part")).ExitCode);
        var preserve = await Decisions(fixture, new[] { new { moduleId = "Part", version = "1.1.0" } });
        Assert.Equal(0, (await Offline(fixture, "enable-bundle", "--package", initial, "--decisions", preserve)).ExitCode);
        Assert.False(Desired(fixture, "Part").Enabled);
        await fixture.StartAsync();
        Assert.DoesNotContain(fixture.Catalog, point => point.ModuleId == "Part");
        await fixture.StopAsync();
        Assert.Equal(0, (await Offline(fixture, "enable", "--module-id", "Part", "--version", "1.1.0")).ExitCode);

        // 新父声明不再携带Part，并不卸载或更换该成员自己的选择。
        Assert.Equal(0, (await Offline(fixture, "install-native", "--package", omitted)).ExitCode);
        var chooseParent = await Decisions(fixture, new[] { new { moduleId = "InforCenter", version = "1.1.0" } });
        Assert.Equal(0, (await Offline(fixture, "select-bundle", "--package", omitted, "--decisions", chooseParent)).ExitCode);
        Assert.True(Directory.Exists(Path.Combine(fixture.PluginRoot, "packages", "Part", "1.1.0")));
        Assert.Equal(("1.1.0", true), Desired(fixture, "Part"));
        foreach (var enabled in new[] { true, false })
        {
            fixture.ExternalPluginsEnabled = enabled;
            var choices = ReadDesired(fixture);
            await fixture.StartAsync();
            await AssertRuntime(fixture, "1.1.0", "1.1.0");
            await AssertPart(fixture, id);
            await fixture.StopAsync();
            Assert.Equal(choices, ReadDesired(fixture));
        }
        Assert.NotEqual(0, (await Offline(fixture, "install", "--package", initial)).ExitCode);

        var legacy = Path.Combine(fixture.PluginRoot, ".feature-gates", "external-plugins-disabled", "lifecycle", "v2", "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        var old = JsonNode.Parse(File.ReadAllBytes(DesiredPath(fixture)))!;
        var oldPart = old["desiredModules"]!.AsArray().Single(item => item!["moduleId"]!.GetValue<string>() == "Part")!;
        oldPart["version"] = "1.0.0";
        oldPart["enabled"] = false;
        File.WriteAllText(legacy, old.ToJsonString());
        var canonicalBytes = File.ReadAllBytes(DesiredPath(fixture));
        Assert.NotEqual(0, (await Offline(fixture, "select", "--module-id", "Part", "--version", "1.1.0")).ExitCode);
        Assert.Equal(canonicalBytes, File.ReadAllBytes(DesiredPath(fixture)));
        var reconcile = await Decisions(fixture, new[] { new { moduleId = "Part", version = "1.1.0", enabled = true } });
        Assert.Equal(0, (await Offline(fixture, "reconcile-desired", "--file", reconcile)).ExitCode);
        Assert.False(File.Exists(legacy));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(legacy)!, "state.reconciled-*.json"));

        Assert.Equal(0, (await Offline(fixture, "disable", "--module-id", "Authorization")).ExitCode);
        var invalidDesired = File.ReadAllBytes(DesiredPath(fixture));
        await fixture.StartAsync(expectFailure: true);
        Assert.Contains("MODULE_DEPENDENCY_MISSING", fixture.LastError);
        Assert.Contains("missing module 'Authorization'", fixture.LastError);
        Assert.Equal(invalidDesired, File.ReadAllBytes(DesiredPath(fixture)));
        Assert.False(Desired(fixture, "Authorization").Enabled);
        Assert.Equal(0, (await Offline(fixture, "enable", "--module-id", "Authorization", "--version", "1.0.0")).ExitCode);
        await fixture.StartAsync();
        await AssertRuntime(fixture, "1.1.0", "1.1.0");
        await AssertPart(fixture, id);
        await fixture.StopAsync();
        Assert.Equal(fixedFiles, HashFiles(fixture.HostRoot));
        await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "fixed-host-files.json"), JsonSerializer.Serialize(fixedFiles));
        Console.WriteLine("NATIVE_SELECTION_PROCESS_EVIDENCE: " + fixture.EvidenceRoot);
    }

    private static async Task AssertRuntime(FeatureCatalogHostFixture fixture, string parentVersion, string partVersion)
    {
        var runtime = await fixture.Administrator.GetFromJsonAsync<JsonElement>("/plugins/active-module-runtime.json");
        var modules = runtime.GetProperty("modules").EnumerateArray().ToArray();
        var parent = modules.Single(item => item.GetProperty("moduleId").GetString() == "InforCenter");
        Assert.Equal(parentVersion, parent.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Null, parent.GetProperty("backend").ValueKind);
        Assert.Equal(JsonValueKind.Null, parent.GetProperty("frontend").ValueKind);
        Assert.Empty(parent.GetProperty("modelKeys").EnumerateArray());
        var part = modules.Single(item => item.GetProperty("moduleId").GetString() == "Part");
        Assert.Equal(partVersion, part.GetProperty("version").GetString());
        Assert.Contains("/Part/" + partVersion + "/", part.GetProperty("frontend").GetProperty("url").GetString());
        Assert.Equal(parentVersion == "1.0.0" ? "InforCenter" : null, part.GetProperty("parentModuleId").GetString());
    }

    private static async Task AssertPart(FeatureCatalogHostFixture fixture, string id)
    {
        var read = Assert.Single(fixture.Catalog, point => point.BehaviorKey == "part.part.read");
        var result = await FeatureHttpTestRequests.ReadAsync(fixture.Administrator, read, "/api/part/parts/" + id);
        Assert.Equal("独立版本保留", result.GetProperty("fields").GetProperty("ENAME").GetString());
    }

    private static async Task<(int ExitCode, string Output)> Offline(FeatureCatalogHostFixture fixture, string action, params string[] options)
    {
        var arguments = new[] { Path.Combine(fixture.HostRoot, "HostApi.dll"), "plugin-deploy", action }.Concat(options).ToArray();
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = fixture.HostRoot, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ConnectionStrings__SqlServer"] = fixture.ConnectionString;
        start.Environment["Platform__PluginInstallRoot"] = fixture.PluginRoot;
        start.Environment["Platform__ExternalPluginsEnabled"] = fixture.ExternalPluginsEnabled.ToString();
        var watch = Stopwatch.StartNew();
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        var output = (await stdout + await stderr).Replace(fixture.ConnectionString, "[connection]");
        watch.Stop();
        var log = "native-selection-" + action + "-" + Guid.NewGuid().ToString("N") + ".log";
        await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, log), output);
        await File.AppendAllTextAsync(Path.Combine(fixture.EvidenceRoot, "native-selection.commands.jsonl"), JsonSerializer.Serialize(new
        {
            command = "dotnet " + string.Join(" ", arguments.Select(argument => "'" + argument.Replace("'", "''") + "'")),
            cwd = fixture.HostRoot, externalPluginsEnabled = fixture.ExternalPluginsEnabled, sqlCatalog = ReusableSqlServerDatabase.Catalog,
            installRoot = fixture.PluginRoot, process.ExitCode, elapsedSeconds = watch.Elapsed.TotalSeconds, log
        }) + Environment.NewLine);
        return (process.ExitCode, output);
    }

    private static async Task<string> Decisions<T>(FeatureCatalogHostFixture fixture, T value)
    {
        var path = Path.Combine(fixture.EvidenceRoot, "native-decisions-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value));
        return path;
    }

    private static string DesiredPath(FeatureCatalogHostFixture fixture) => Path.Combine(fixture.PluginRoot, "lifecycle", "v2", "state.json");
    private static string ReadDesired(FeatureCatalogHostFixture fixture) => JsonNode.Parse(File.ReadAllText(DesiredPath(fixture)))!["desiredModules"]!.ToJsonString();
    private static (string Version, bool Enabled) Desired(FeatureCatalogHostFixture fixture, string id)
    {
        var selected = JsonNode.Parse(File.ReadAllText(DesiredPath(fixture)))!["desiredModules"]!.AsArray().Single(item => item!["moduleId"]!.GetValue<string>() == id)!;
        return (selected["version"]!.GetValue<string>(), selected["enabled"]!.GetValue<bool>());
    }

    private static string Required(string key) => Path.GetFullPath(Environment.GetEnvironmentVariable(key) ?? throw new InvalidOperationException("Missing " + key));
    private static IReadOnlyDictionary<string, string> HashFiles(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
    private static bool PathContainmentCheck(string root, string target) => Path.GetFullPath(target).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void CopyFiles(string source, string target)
    {
        foreach (var path in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, path));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(path, destination);
        }
    }
}
