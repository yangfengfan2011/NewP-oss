using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.PluginPlatform.ComponentBundles;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.PluginPlatform.StandardData;
using ICP.Platform.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.PluginPlatform.Tests;

public sealed class ComponentBundleScaleTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_COMPONENT_SCALE", "正式组件 CLI 构建/打包/检查与 100 组件、5 层运行时消费")]
    public async Task HundredComponents_FiveLevels_UseFormalDistributionAndRuntimeConsumer()
    {
        var repository = FindRepository();
        var tool = Path.GetFullPath(Environment.GetEnvironmentVariable("NEWP_COMPONENT_SCALE_TOOL")
            ?? throw new InvalidOperationException("NEWP_COMPONENT_SCALE_TOOL must identify the built formal CLI."));
        Assert.True(File.Exists(tool));
        var root = Path.Combine(Path.GetTempPath(), "NewP-274-scale-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.GetFullPath(Environment.GetEnvironmentVariable("NEWP_COMPONENT_SCALE_EVIDENCE")
            ?? Path.Combine(repository, ".codex-temp", "issue-267", "274", "scale", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(evidence);
        var metrics = new List<Phase>();
        var source = Path.Combine(root, "repository");
        var stage = Path.Combine(root, "bundle");
        var zip = Path.Combine(root, "bundle.zip");
        try
        {
            WriteDeclarations(source, repository);
            var declarations = ComponentSourceCatalog.Discover(source).GetGroup("Scale000");
            var declaredParents = declarations.ToDictionary(component => component.ModuleId, _ => (string?)null);
            foreach (var component in declarations)
                foreach (var member in component.Capsule.GetProperty("composition").GetProperty("members").EnumerateArray())
                    declaredParents[member.GetString()!] = component.ModuleId;
            var declaredLevels = declaredParents.Keys.Max(id => Depth(id, declaredParents));
            var layerCounts = declaredParents.Keys.GroupBy(id => Depth(id, declaredParents))
                .OrderBy(group => group.Key).Select(group => group.Count()).ToArray();
            Assert.Equal(100, declaredParents.Count);
            Assert.Equal(5, declaredLevels);
            Assert.Equal([1, 4, 16, 48, 31], layerCounts);
            await File.WriteAllTextAsync(Path.Combine(evidence, "authoring-graph.json"), JsonSerializer.Serialize(new
            { parents = declaredParents, levels = declaredLevels, layerCounts }, new JsonSerializerOptions { WriteIndented = true }));
            await RunCliAsync(tool, ["build", source, "Scale000", stage], "build", repository, evidence, metrics);
            await RunCliAsync(tool, ["pack", stage, zip], "pack", repository, evidence, metrics);
            await RunCliAsync(tool, ["inspect", zip], "inspect", repository, evidence, metrics);
            var inspection = new ComponentBundleInspector().Inspect(stage);
            Assert.Equal(100, inspection.Manifest.Components.Count);
            File.Copy(Path.Combine(stage, "component-bundle.json"), Path.Combine(evidence, "component-bundle.json"));
            File.Copy(Path.Combine(stage, "platform-runtime-release.json"), Path.Combine(evidence, "platform-runtime-release.json"));

            var timer = Stopwatch.StartNew();
            var installRoot = Path.Combine(root, "installed");
            var store = new InstalledPluginStateStore(installRoot);
            new NativeComponentPackageInstaller(installRoot, store).Install(zip);
            var platformRoot = Path.Combine(root, "platform");
            Directory.CreateDirectory(Path.Combine(platformRoot, "modules"));
            var trust = TrustedBuiltInModuleSource.CreateForHost(platformRoot, Path.Combine(platformRoot, "modules"));
            var inspector = new StandardDataPackageInspector(new PlatformApiHostCatalog("1.0.0", [], []));
            var inventory = InstalledPluginInventoryAdapter.Create(store);
            var desired = new ResolvedPluginLifecycleState(inspection.Manifest.Components
                .Select(component => new PluginDesiredModuleState(component.ModuleId, component.Version, true)).ToArray(), null, null, false);
            new StandardDataPackagePreflight(inspector, "1.1.0", trust, installRoot, false).Validate(inventory, desired);
            var plan = new StandardDataCandidatePlanCompiler(trust, inspector, installRoot: installRoot,
                externalPluginsEnabled: false).Compile(inventory, desired);
            Assert.Equal(100, plan.Modules.Count);
            Assert.All(plan.Modules, module =>
            {
                Assert.True(module.Provenance.AuthenticatesBuiltIn(module));
                Assert.Empty(module.ModelKeys);
                Assert.Null(module.BackendEntry);
                Assert.Null(module.FrontendEntry);
                Assert.Null(module.Persistence);
            });
            var materializer = new RuntimeViewMaterializer(store, new FrontendRuntimeManifestBuilder("/plugins/runtime/assets"), trust);
            using (var projection = materializer.Stage(installRoot, plan))
            using (var commit = projection.BeginCommit()) commit.Complete();
            var catalogPath = Path.Combine(installRoot, "runtime", "active-module-runtime.json");
            using var catalog = JsonDocument.Parse(File.ReadAllBytes(catalogPath));
            var parents = catalog.RootElement.GetProperty("modules").EnumerateArray().ToDictionary(
                module => module.GetProperty("moduleId").GetString()!, module => module.GetProperty("parentModuleId").GetString());
            Assert.Equal(100, parents.Count);
            var runtimeLevels = parents.Keys.Max(id => Depth(id, parents));
            Assert.Equal(declaredLevels, runtimeLevels);
            File.Copy(catalogPath, Path.Combine(evidence, "active-module-runtime.json"));
            metrics.Add(new("install-validate-compile-materialize", timer.Elapsed.TotalSeconds));
            await File.WriteAllTextAsync(Path.Combine(evidence, "metrics.json"), JsonSerializer.Serialize(new
            { components = parents.Count, declaredLevels, runtimeLevels, layerCounts, declarationOnlyScaleInput = true, phases = metrics }, new JsonSerializerOptions { WriteIndented = true }));
            output.WriteLine($"100 components / 5 levels; evidence: {evidence}");
            foreach (var phase in metrics) output.WriteLine($"{phase.Name}: {phase.Seconds:F6}s");
        }
        finally
        {
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(root).StartsWith(temp, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe scale cleanup root.");
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            await File.WriteAllTextAsync(Path.Combine(evidence, "cleanup.json"), JsonSerializer.Serialize(new { ownedRoot = root, resourcesRemoved = !Directory.Exists(root) }));
        }
    }

    private sealed record Phase(string Name, double Seconds);
    private sealed record Declaration(string Id, string Root, List<string> Members);

    // 规模输入仅声明组合图；真实业务纵向验证另消费主仓 Workspace/Part/Document，不伪造代码或数据模型。
    private static void WriteDeclarations(string source, string repository)
    {
        var declarations = new List<Declaration> { new("Scale000", Path.Combine(source, "frontend", "modules", "scale000"), []) };
        var previous = declarations.ToArray();
        foreach (var count in new[] { 4, 16, 48, 31 })
        {
            var level = new List<Declaration>();
            for (var index = 0; index < count; index++)
            {
                var parent = previous[index % previous.Length];
                var id = $"Scale{declarations.Count:D3}";
                var declaration = new Declaration(id, Path.Combine(parent.Root, id.ToLowerInvariant()), []);
                parent.Members.Add(id);
                declarations.Add(declaration);
                level.Add(declaration);
            }
            previous = level.ToArray();
        }
        foreach (var declaration in declarations)
        {
            Directory.CreateDirectory(declaration.Root);
            File.WriteAllText(Path.Combine(declaration.Root, "newp.plugin.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = "1.0", moduleId = declaration.Id, displayName = declaration.Id, version = "1.0.0",
                dependencies = Array.Empty<string>(), hostVersion = new { min = "1.1.0", max = "2.0.0" },
                composition = new { members = declaration.Members }, frontend = new { enabled = false }
            }));
        }
        var storage = Path.Combine(source, "server", "platform-modeling", "metadata", "storage-types.json");
        Directory.CreateDirectory(Path.GetDirectoryName(storage)!);
        File.Copy(Path.Combine(repository, "server", "platform-modeling", "metadata", "storage-types.json"), storage);
    }

    private static int Depth(string id, IReadOnlyDictionary<string, string?> parents) =>
        parents[id] is { } parent ? 1 + Depth(parent, parents) : 1;

    private static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ICP-Platform.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static async Task RunCliAsync(string tool, string[] arguments, string phase, string repository, string evidence, List<Phase> metrics)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = repository, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add(tool);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Formal component CLI did not start.");
        var timer = Stopwatch.StartNew();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await process.WaitForExitAsync(timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(evidence, phase + ".stdout.log"), await stdout);
            await File.WriteAllTextAsync(Path.Combine(evidence, phase + ".stderr.log"), await stderr);
            await File.WriteAllTextAsync(Path.Combine(evidence, phase + "-command.json"), JsonSerializer.Serialize(new
            { file = "dotnet", arguments = new[] { tool }.Concat(arguments), cwd = repository, exitCode = process.ExitCode }));
            Assert.True(process.ExitCode == 0, $"Formal {phase} failed; read {evidence}/{phase}.stderr.log.");
            metrics.Add(new(phase, timer.Elapsed.TotalSeconds));
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }
}
