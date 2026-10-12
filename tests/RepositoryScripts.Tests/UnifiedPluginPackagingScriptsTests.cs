using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.RepositoryScripts.Tests;

public class UnifiedPluginPackagingScriptsTests : IDisposable
{
    private readonly List<string> _tempRoots = new();

    [Fact]
    public void PackUnifiedPlugin_DisablesPersistentBuildServers()
    {
        var repoRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repoRoot, "scripts", "pack-unified-plugin.ps1"));

        Assert.Contains("--disable-build-servers", script, StringComparison.Ordinal);
    }

    [Fact]
    public void NewModule_GeneratesCanonicalBuiltInCapsule()
    {
        var repoRoot = FindRepositoryRoot();
        var fixtureRoot = Path.Combine(Path.GetTempPath(), $"newp-new-module-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureRoot);
        _tempRoots.Add(fixtureRoot);

        CopyFile(
            Path.Combine(repoRoot, "scripts", "new-module.ps1"),
            Path.Combine(fixtureRoot, "scripts", "new-module.ps1"));
        CopyTemplateTree(
            Path.Combine(repoRoot, "server", "templates", "backend-module-template"),
            Path.Combine(fixtureRoot, "server", "templates", "backend-module-template"));
        CopyTemplateTree(
            Path.Combine(repoRoot, "frontend", "templates", "frontend-module-template"),
            Path.Combine(fixtureRoot, "frontend", "templates", "frontend-module-template"));

        var hostProjectPath = Path.Combine(fixtureRoot, "apps", "host-api", "HostApi.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(hostProjectPath)!);
        File.WriteAllText(
            hostProjectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <Content Include="..\..\frontend\modules\existing\newp.plugin.json">
                  <Link>modules\Existing\plugin.json</Link>
                  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
                </Content>
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="..\..\server\sdk\Sdk.csproj" />
              </ItemGroup>
            </Project>
            """);

        var hostProjectBefore = File.ReadAllText(hostProjectPath);
        var scriptPath = Path.Combine(fixtureRoot, "scripts", "new-module.ps1");
        var result = RunPowerShellScript(
            fixtureRoot,
            scriptPath,
            [
                "-ModuleName", "CanonicalSample",
                "-DisplayName", "规范样例"
            ]);

        Assert.True(
            result.ExitCode == 0,
            $"Expected exit code 0 but got {result.ExitCode}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");

        var capsuleRoot = Path.Combine(fixtureRoot, "frontend", "modules", "canonical-sample");
        var manifestPath = Path.Combine(capsuleRoot, "newp.plugin.json");
        var navigationPath = Path.Combine(
            capsuleRoot,
            "metadata",
            "navigation",
            "CanonicalSample.nav.json");
        Assert.True(File.Exists(manifestPath));
        Assert.True(File.Exists(navigationPath));
        Assert.False(Directory.Exists(Path.Combine(fixtureRoot, "apps", "host-api", "modules", "CanonicalSample")));

        using (var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath)))
        {
            var manifest = manifestDocument.RootElement;
            Assert.Equal("CanonicalSample", manifest.GetProperty("moduleId").GetString());
            Assert.Equal("../../CanonicalSampleModule.dll", manifest.GetProperty("entryAssembly").GetString());
            Assert.Equal("canonical-sample.backend", manifest.GetProperty("backend").GetProperty("behaviorKey").GetString());
            Assert.Equal("canonical-sample.frontend", manifest.GetProperty("frontend").GetProperty("behaviorKey").GetString());
            Assert.Equal("behavior-bindings", manifest.GetProperty("frontend").GetProperty("bindingMode").GetString());
            Assert.Equal(
                "canonical-sample.page",
                Assert.Single(manifest.GetProperty("frontend").GetProperty("bindings").EnumerateArray()).GetString());
            Assert.False(manifest.TryGetProperty("security", out _));
            var feature = Assert.Single(manifest.GetProperty("features").EnumerateArray());
            Assert.Equal("canonical-sample", feature.GetProperty("key").GetString());
            Assert.Equal("canonical-sample.read", feature.GetProperty("behaviorKey").GetString());
        }

        using (var navigationDocument = JsonDocument.Parse(File.ReadAllText(navigationPath)))
        {
            var item = Assert.Single(
                navigationDocument.RootElement
                    .GetProperty("navigation")
                    .GetProperty("items")
                    .EnumerateArray());
            Assert.Equal("canonical-sample.page", item.GetProperty("targetKey").GetString());
            Assert.Equal("canonical-sample", item.GetProperty("featureKey").GetString());
            Assert.False(item.TryGetProperty("permissions", out _));
        }

        var runtimeModulesRoot = Path.Combine(fixtureRoot, "runtime", "modules");
        var runtimeModuleRoot = Path.Combine(runtimeModulesRoot, "CanonicalSample");
        CopyFile(manifestPath, Path.Combine(runtimeModuleRoot, "plugin.json"));
        CopyTemplateTree(
            Path.Combine(capsuleRoot, "metadata"),
            Path.Combine(runtimeModuleRoot, "metadata"));
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(runtimeModulesRoot);
        var compilation = new CanonicalModulePlanCompiler().Compile("scaffold-contract", candidates);
        Assert.True(
            compilation.IsSuccess,
            string.Join(Environment.NewLine, compilation.Diagnostics.Select(static item => item.Message)));

        var point = Assert.Single(compilation.Plan!.FeatureCatalog);
        Assert.Equal("CanonicalSample", point.ModuleId);
        Assert.Equal("canonical-sample.read", point.BehaviorKey);

        var backendProject = File.ReadAllText(
            Path.Combine(fixtureRoot, "server", "modules", "CanonicalSample", "CanonicalSample.csproj"));
        Assert.DoesNotContain("EmbeddedResource", backendProject, StringComparison.Ordinal);
        Assert.DoesNotContain("newp.plugin.json", backendProject, StringComparison.Ordinal);
        Assert.Contains(@"..\..\platform-authorization\PlatformAuthorization.csproj", backendProject, StringComparison.Ordinal);

        var backendModule = File.ReadAllText(
            Path.Combine(fixtureRoot, "server", "modules", "CanonicalSample", "CanonicalSampleModule.cs"));
        Assert.Contains("class CanonicalSampleModule : ModuleCapsuleBindingBase", backendModule, StringComparison.Ordinal);
        Assert.Contains("BehaviorKey => \"canonical-sample.backend\"", backendModule, StringComparison.Ordinal);
        Assert.DoesNotContain("ModuleId =>", backendModule, StringComparison.Ordinal);

        var controller = File.ReadAllText(
            Path.Combine(fixtureRoot, "server", "modules", "CanonicalSample", "Controllers", "CanonicalSampleController.cs"));
        Assert.Contains("[RequireFeature(\"canonical-sample.read\")]", controller, StringComparison.Ordinal);
        Assert.Contains("[EndpointModule(\"CanonicalSample\")]", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("ModuleId =", controller, StringComparison.Ordinal);

        var frontendBinding = File.ReadAllText(
            Path.Combine(fixtureRoot, "frontend", "modules", "canonical-sample", "src", "index.ts"));
        Assert.Contains("FrontendBehaviorBindings", frontendBinding, StringComparison.Ordinal);
        Assert.Contains("behaviorKey: 'canonical-sample.frontend'", frontendBinding, StringComparison.Ordinal);
        Assert.Contains("routeCapabilities:", frontendBinding, StringComparison.Ordinal);
        Assert.Contains("targetKey: 'canonical-sample.page'", frontendBinding, StringComparison.Ordinal);
        Assert.Contains("createRecoverableCapabilityLoader", frontendBinding, StringComparison.Ordinal);
        Assert.Contains("import('./CanonicalSamplePage.primary')", frontendBinding, StringComparison.Ordinal);
        Assert.Contains("import('./CanonicalSamplePage.recovery')", frontendBinding, StringComparison.Ordinal);
        Assert.DoesNotContain("import CanonicalSamplePage", frontendBinding, StringComparison.Ordinal);
        Assert.DoesNotContain("customPages:", frontendBinding, StringComparison.Ordinal);
        Assert.DoesNotContain("metadata:", frontendBinding, StringComparison.Ordinal);
        Assert.DoesNotContain("routes:", frontendBinding, StringComparison.Ordinal);
        Assert.DoesNotContain("menus:", frontendBinding, StringComparison.Ordinal);
        Assert.DoesNotContain("permissions:", frontendBinding, StringComparison.Ordinal);

        var frontendPrimaryEntry = File.ReadAllText(
            Path.Combine(fixtureRoot, "frontend", "modules", "canonical-sample", "src", "CanonicalSamplePage.primary.ts"));
        var frontendRecoveryEntry = File.ReadAllText(
            Path.Combine(fixtureRoot, "frontend", "modules", "canonical-sample", "src", "CanonicalSamplePage.recovery.ts"));
        Assert.Contains("export function primary()", frontendPrimaryEntry, StringComparison.Ordinal);
        Assert.Contains("createElement(CanonicalSamplePage)", frontendPrimaryEntry, StringComparison.Ordinal);
        Assert.Contains("export function recovery()", frontendRecoveryEntry, StringComparison.Ordinal);
        Assert.Contains("createElement(CanonicalSamplePage)", frontendRecoveryEntry, StringComparison.Ordinal);

        var hostProject = File.ReadAllText(hostProjectPath);
        Assert.Equal(hostProjectBefore, hostProject);
    }

    [Fact]
    public void PackUnifiedPlugin_WritesFrontendPublicBasePathMatchingHostStaticAssetsRoute()
    {
        var repoRoot = FindRepositoryRoot();
        var outputRoot = Path.Combine(repoRoot, "artifacts", "plugin-packages", $"script-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputRoot);
        _tempRoots.Add(outputRoot);

        var result = RunPowerShellScript(
            repoRoot,
            Path.Combine(repoRoot, "scripts", "pack-unified-plugin.ps1"),
            [
                "-ModuleName", "Identity",
                "-Configuration", "Debug",
                "-OutputRoot", outputRoot,
                "-Force"
            ]);

        var packageRoot = Path.Combine(outputRoot, "Identity");
        var frontendManifestPath = Path.Combine(packageRoot, "frontend", "plugin.frontend.json");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(frontendManifestPath), $"Expected frontend manifest at {frontendManifestPath}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");

        using var document = JsonDocument.Parse(File.ReadAllText(frontendManifestPath));
        Assert.Equal(
            "/plugin-assets/Identity/1.0.0/",
            document.RootElement.GetProperty("publicBasePath").GetString());
    }

    [Fact]
    public void PackUnifiedPlugin_IncludesSelectedModuleMetadata_AndPlatformResources()
    {
        var repoRoot = FindRepositoryRoot();
        var outputRoot = Path.Combine(repoRoot, "artifacts", "plugin-packages", $"script-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputRoot);
        _tempRoots.Add(outputRoot);

        var result = RunPowerShellScript(
            repoRoot,
            Path.Combine(repoRoot, "scripts", "pack-unified-plugin.ps1"),
            [
                "-ModuleName", "Organization",
                "-Configuration", "Debug",
                "-OutputRoot", outputRoot,
                "-Force"
            ]);

        var packageRoot = Path.Combine(outputRoot, "Organization");
        var modelMetadataPath = Path.Combine(Path.Combine(packageRoot, "modules", "Organization"), "metadata", "models", "Person.model.json");
        var navigationMetadataPath = Path.Combine(Path.Combine(packageRoot, "modules", "Organization"), "metadata", "navigation", "Organization.nav.json");
        var personListDictionaryPath = Path.Combine(Path.Combine(packageRoot, "modules", "Organization"), "metadata", "dictionaries", "PersonListView.dic.json");

        Assert.True(result.ExitCode == 0, $"Expected exit code 0 but got {result.ExitCode}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        Assert.True(File.Exists(modelMetadataPath), $"Expected model metadata at {modelMetadataPath}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        Assert.True(File.Exists(navigationMetadataPath), $"Expected navigation metadata at {navigationMetadataPath}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
        Assert.True(File.Exists(personListDictionaryPath), $"Expected personnel dictionary at {personListDictionaryPath}.\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");

        using var dictionaryDocument = JsonDocument.Parse(File.ReadAllText(personListDictionaryPath));
        var personList = dictionaryDocument.RootElement.GetProperty("PersonListView");
        Assert.Equal("人员中心", personList.GetProperty("Title").GetProperty("zhs").GetString());
        Assert.Equal("Person Center", personList.GetProperty("Title").GetProperty("en").GetString());
        Assert.Equal("查看完整详情", personList.GetProperty("Action_ViewFullDetail").GetProperty("zhs").GetString());
        Assert.Equal("View Full Details", personList.GetProperty("Action_ViewFullDetail").GetProperty("en").GetString());
    }

    public void Dispose()
    {
        foreach (var tempRoot in _tempRoots)
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "scripts", "pack-unified-plugin.ps1")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static void CopyFile(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath);
    }

    private static void CopyTemplateTree(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (var filePath in Directory.GetFiles(sourceRoot))
        {
            File.Copy(filePath, Path.Combine(destinationRoot, Path.GetFileName(filePath)));
        }

        foreach (var directoryPath in Directory.GetDirectories(sourceRoot))
        {
            var directoryName = Path.GetFileName(directoryPath);
            if (directoryName is "bin" or "obj" or "dist" or "node_modules")
            {
                continue;
            }

            CopyTemplateTree(directoryPath, Path.Combine(destinationRoot, directoryName));
        }
    }

    private static ScriptResult RunPowerShellScript(string workingDirectory, string scriptPath, IReadOnlyList<string> arguments)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdout.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderr.AppendLine(args.Data);
            }
        };

        Assert.True(process.Start(), $"Failed to start script {scriptPath}.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return new ScriptResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private sealed record ScriptResult(int ExitCode, string StdOut, string StdErr);
}
