using System.Diagnostics;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class CanonicalRuntimeViewMaterializerTests : IDisposable
{
    private readonly List<string> _links = [];
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "icp-platform-tests",
        $"canonical-runtime-{Guid.NewGuid():N}");

    [Fact]
    public void CreateForHost_WhenModulesRootIsOutsideInstallationRoot_RejectsTrust()
    {
        var installationRoot = Path.Combine(_root, "installation");
        var externalModulesRoot = Path.Combine(_root, "external", "modules");
        Directory.CreateDirectory(installationRoot);
        Directory.CreateDirectory(externalModulesRoot);

        var error = Assert.Throws<InvalidOperationException>(() =>
            TrustedBuiltInModuleSource.CreateForHost(installationRoot, externalModulesRoot));

        Assert.Contains("installation root", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateForHost_WhenIntermediateDirectoryIsReparsePoint_RejectsTrust()
    {
        var installationRoot = Path.Combine(_root, "installation");
        var externalBundleRoot = Path.Combine(_root, "external-bundle");
        var modulesRoot = Path.Combine(externalBundleRoot, "modules");
        Directory.CreateDirectory(installationRoot);
        Directory.CreateDirectory(modulesRoot);
        var linkedBundleRoot = Path.Combine(installationRoot, "linked-bundle");
        CreateDirectoryLink(linkedBundleRoot, externalBundleRoot);

        var error = Assert.Throws<InvalidOperationException>(() =>
            TrustedBuiltInModuleSource.CreateForHost(
                installationRoot,
                Path.Combine(linkedBundleRoot, "modules")));

        Assert.Contains("reparse point", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadTrustedBuiltIn_WhenBackendEntryTraversesReparsePoint_RejectsTrust()
    {
        var installationRoot = Path.Combine(_root, "installation");
        var modulesRoot = Path.Combine(installationRoot, "modules");
        var moduleRoot = Path.Combine(modulesRoot, "Organization");
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(
            Path.Combine(moduleRoot, "plugin.json"),
            """
            {
              "schemaVersion": "1.0",
              "moduleId": "Organization",
              "displayName": "组织结构",
              "version": "1.0.0",
              "entryAssembly": "../../entries/OrganizationModule.dll",
              "dependencies": []
            }
            """);
        var source = TrustedBuiltInModuleSource.CreateForHost(installationRoot, modulesRoot);
        var externalEntries = Path.Combine(_root, "external-entries");
        Directory.CreateDirectory(externalEntries);
        var externalEntry = Path.Combine(externalEntries, "OrganizationModule.dll");
        File.WriteAllText(externalEntry, "fixture");
        var linkedEntries = Path.Combine(installationRoot, "entries");
        CreateDirectoryLink(linkedEntries, externalEntries);

        var error = Assert.Throws<InvalidOperationException>(() =>
            new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(source));

        Assert.Contains("reparse point", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stage_AuthenticatedBackendlessBuiltInReplacedWithJunctionAfterPlanIssued_RejectsBeforeConsumption()
    {
        var modulesRoot = Path.Combine(_root, "modules");
        var moduleRoot = Path.Combine(modulesRoot, "PluginOps");
        var metadataRoot = Path.Combine(moduleRoot, "metadata", "models");
        Directory.CreateDirectory(metadataRoot);
        File.WriteAllText(
            Path.Combine(moduleRoot, "plugin.json"),
            """
            {
              "schemaVersion": "1.0",
              "moduleId": "PluginOps",
              "displayName": "插件运维",
              "version": "1.0.0",
              "dependencies": []
            }
            """);
        File.WriteAllText(
            Path.Combine(metadataRoot, "PluginRegistration.model.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "model",
              "PluginRegistration": { "kind": "Entity" }
            }
            """);
        var source = TrustedBuiltInModuleSource.CreateForHost(_root, modulesRoot);
        var candidate = Assert.Single(new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(source));
        var compilation = new CanonicalModulePlanCompiler().Compile("issued-before-swap", [candidate]);
        var plan = Assert.IsType<CanonicalModulePlan>(compilation.Plan);

        var externalModuleRoot = Path.Combine(_root, "external-PluginOps");
        Directory.Move(moduleRoot, externalModuleRoot);
        var sentinelPath = Path.Combine(externalModuleRoot, "external-sentinel.txt");
        File.WriteAllText(sentinelPath, "must remain untouched");
        CreateDirectoryLink(moduleRoot, externalModuleRoot);

        var installRoot = Path.Combine(_root, "install");
        var materializer = new RuntimeViewMaterializer(
            new InstalledPluginStateStore(installRoot),
            new FrontendRuntimeManifestBuilder(),
            source);

        var error = Assert.Throws<InvalidOperationException>(() => materializer.Stage(installRoot, plan));

        Assert.Contains("reparse point", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(sentinelPath));
    }

    [Fact]
    public void Stage_PublicBuiltInLabel_DoesNotSelectPrivilegedMetadataLayout()
    {
        var packageRoot = Path.Combine(_root, "package");
        var privilegedMetadataRoot = Path.Combine(packageRoot, "metadata", "navigation");
        var installedMetadataRoot = Path.Combine(packageRoot, "modules", "Partner", "metadata", "navigation");
        Directory.CreateDirectory(privilegedMetadataRoot);
        Directory.CreateDirectory(installedMetadataRoot);
        File.WriteAllText(Path.Combine(privilegedMetadataRoot, "privileged.json"), "{}");
        File.WriteAllText(Path.Combine(installedMetadataRoot, "installed.json"), "{}");
        var module = CreatePublicBuiltInModule("Partner", packageRoot);
        var plan = new CanonicalModulePlan(
            "candidate",
            "1.0",
            new string('a', 64),
            new string('b', 64),
            [],
            [module]);
        var installRoot = Path.Combine(_root, "install");

        using var stage = new RuntimeViewMaterializer(new InstalledPluginStateStore(installRoot))
            .Stage(installRoot, plan);

        var projectedMetadata = Path.Combine(stage.StagedRuntimeRoot, "modules", "Partner", "metadata", "navigation");
        Assert.True(File.Exists(Path.Combine(projectedMetadata, "installed.json")));
        Assert.False(File.Exists(Path.Combine(projectedMetadata, "privileged.json")));
    }

    [Fact]
    public void PathContainment_WhenRootIsDriveRoot_HandlesRootCaseAndDirectoryBoundary()
    {
        var driveRoot = Path.GetPathRoot(_root)!;
        var caseVariant = driveRoot.ToLowerInvariant();

        Assert.True(PathContainment.Contains(caseVariant, _root));
        Assert.True(PathContainment.Contains(driveRoot, driveRoot));
        Assert.False(PathContainment.Contains(Path.Combine(_root, "module"), Path.Combine(_root, "module-other")));
    }

    [Fact]
    public void RuntimeCatalogs_PublicBuiltInLabel_UseExternalFrontendProjection()
    {
        var module = CreatePublicBuiltInModule("Partner", Path.Combine(_root, "package")) with
        {
            FrontendEntry = "entry.js"
        };
        var plan = new CanonicalModulePlan(
            "candidate",
            "1.0",
            new string('a', 64),
            new string('b', 64),
            [],
            [module]);

        using var manifest = JsonDocument.Parse(new FrontendRuntimeManifestBuilder().Build(plan));
        var manifestEntry = Assert.Single(manifest.RootElement.GetProperty("plugins").EnumerateArray());
        Assert.Equal("/plugin-assets/Partner/1.0.0/entry.js", manifestEntry.GetProperty("url").GetString());
        Assert.False(manifestEntry.TryGetProperty("moduleId", out _));

        using var catalog = JsonDocument.Parse(new ActiveModuleRuntimeCatalogBuilder().Build(plan));
        var frontend = Assert.Single(catalog.RootElement.GetProperty("modules").EnumerateArray())
            .GetProperty("frontend");
        Assert.Equal("/plugin-assets/Partner/1.0.0/entry.js", frontend.GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Null, frontend.GetProperty("moduleId").ValueKind);
    }

    [Fact]
    public void Materialize_BuiltInCapsule_WritesOneActiveCatalogAndHashBoundCompatibilityProjections()
    {
        var modulesRoot = Path.Combine(_root, "modules");
        var moduleRoot = Path.Combine(modulesRoot, "Organization");
        var metadataRoot = Path.Combine(moduleRoot, "metadata");
        Directory.CreateDirectory(Path.Combine(metadataRoot, "navigation"));
        Directory.CreateDirectory(Path.Combine(metadataRoot, "models"));
        File.WriteAllText(Path.Combine(_root, "OrganizationModule.dll"), "fixture assembly");
        File.WriteAllText(
            Path.Combine(moduleRoot, "plugin.json"),
            """
            {
              "schemaVersion": "1.0",
              "moduleId": "Organization",
              "displayName": "组织结构",
              "version": "1.0.0",
              "entryAssembly": "../../OrganizationModule.dll",
              "dependencies": [],
              "backend": { "behaviorKey": "organization.backend" },
              "frontend": {
                "behaviorKey": "organization.frontend",
                "entry": "organization",
                "bindingMode": "behavior-bindings",
                "bindings": ["organization.person.list"]
              },
              "security": {
                "permissions": [
                  { "code": "Organization.Person.View", "displayName": "查看人员" }
                ]
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(metadataRoot, "navigation", "Organization.nav.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "nav",
              "navigation": {
                "items": [
                  {
                    "key": "organization.personCenter",
                    "path": "/organization/persons",
                    "entryType": "standard-list",
                    "targetKey": "organization.person.list",
                    "permissions": ["Organization.Person.View"]
                  }
                ]
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(metadataRoot, "models", "Person.model.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "model",
              "Person": { "kind": "Entity" }
            }
            """);

        var installRoot = Path.Combine(_root, "install");
        var stateStore = new InstalledPluginStateStore(installRoot);
        var builtInModuleSource = TrustedBuiltInModuleSource.CreateForHost(_root, modulesRoot);
        var materializer = new RuntimeViewMaterializer(
            stateStore,
            new FrontendRuntimeManifestBuilder(),
            builtInModuleSource);

        materializer.Materialize(installRoot);

        var runtimeRoot = Path.Combine(installRoot, "runtime");
        var activeCatalogPath = Path.Combine(runtimeRoot, "active-module-runtime.json");
        var planPath = Path.Combine(runtimeRoot, "canonical-module-plan.json");
        Assert.True(File.Exists(activeCatalogPath));
        Assert.True(File.Exists(planPath));
        Assert.True(File.Exists(Path.Combine(runtimeRoot, "backend-load-view", "Organization", "plugin.json")));
        Assert.True(File.Exists(Path.Combine(runtimeRoot, "modules", "Organization", "metadata", "navigation", "Organization.nav.json")));

        using var active = JsonDocument.Parse(File.ReadAllText(activeCatalogPath));
        var root = active.RootElement;
        var snapshotId = root.GetProperty("snapshotId").GetString();
        var planHash = root.GetProperty("planHash").GetString();
        Assert.False(string.IsNullOrWhiteSpace(snapshotId));
        Assert.Equal(planHash, snapshotId);
        var module = Assert.Single(root.GetProperty("modules").EnumerateArray());
        Assert.Equal("Organization", module.GetProperty("moduleId").GetString());
        Assert.False(module.TryGetProperty("provenance", out _));
        Assert.Equal("organization.frontend", module.GetProperty("frontend").GetProperty("behaviorKey").GetString());
        Assert.Equal("behavior-bindings", module.GetProperty("frontend").GetProperty("bindingMode").GetString());

        foreach (var fileName in new[]
                 {
                     "frontend-plugin-manifest.json",
                     "metadata-runtime-manifest.json",
                     "localization-manifest.json"
                 })
        {
            using var projection = JsonDocument.Parse(File.ReadAllText(Path.Combine(runtimeRoot, fileName)));
            Assert.Equal(snapshotId, projection.RootElement.GetProperty("snapshotId").GetString());
            Assert.Equal(planHash, projection.RootElement.GetProperty("projectionHash").GetString());
        }
    }

    public void Dispose()
    {
        foreach (var link in _links.Where(path => File.Exists(path) || Directory.Exists(path)))
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }
            else
            {
                File.Delete(link);
            }
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void CreateDirectoryLink(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            _links.Add(linkPath);
            return;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ArgumentList = { "/c", "mklink", "/J", linkPath, targetPath }
        }) ?? throw new InvalidOperationException("无法启动 junction 创建进程。");
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"junction 创建失败：{process.StandardOutput.ReadToEnd()} {process.StandardError.ReadToEnd()}");
        _links.Add(linkPath);
    }

    private static CanonicalModulePlanModule CreatePublicBuiltInModule(string moduleId, string packageRoot) =>
        new(
            moduleId,
            "1.0.0",
            moduleId,
            null,
            packageRoot,
            null,
            null,
            null,
            null,
            "legacy-module",
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            new ModulePlanProvenance("built-in", Path.Combine(packageRoot, "plugin.json"), "/"));
}
