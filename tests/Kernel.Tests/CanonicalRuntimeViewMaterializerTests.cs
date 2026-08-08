using System.Text.Json;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class CanonicalRuntimeViewMaterializerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "icp-platform-tests",
        $"canonical-runtime-{Guid.NewGuid():N}");

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
        var materializer = new RuntimeViewMaterializer(
            stateStore,
            new FrontendRuntimeManifestBuilder(),
            modulesRoot);

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
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
