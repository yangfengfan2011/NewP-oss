using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class RuntimeViewStagingTests : IDisposable
{
    private readonly string _installRoot = Path.Combine(
        Path.GetTempPath(),
        "newp-runtime-staging-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void BeginCommit_WhenNotCompleted_RestoresPreviousRuntimeAndPublicProjection()
    {
        SeedPreviousProjection();
        var materializer = new RuntimeViewMaterializer(new InstalledPluginStateStore(_installRoot));

        using var stage = materializer.Stage(_installRoot, CreatePlan("candidate-rollback"));

        AssertPreviousProjection();
        using (stage.BeginCommit())
        {
            Assert.False(File.Exists(Path.Combine(_installRoot, "runtime", "old-runtime.txt")));
            Assert.False(File.Exists(Path.Combine(_installRoot, "public", "old-public.txt")));
            Assert.True(File.Exists(Path.Combine(_installRoot, "runtime", "canonical-module-plan.json")));
        }

        AssertPreviousProjection();
    }

    [Fact]
    public void BeginCommit_WhenCompleted_PublishesCandidateAndCleansOwnedWorkspaces()
    {
        SeedPreviousProjection();
        var materializer = new RuntimeViewMaterializer(new InstalledPluginStateStore(_installRoot));

        using (var stage = materializer.Stage(_installRoot, CreatePlan("candidate-complete")))
        using (var commit = stage.BeginCommit())
        {
            commit.Complete();
        }

        Assert.False(File.Exists(Path.Combine(_installRoot, "runtime", "old-runtime.txt")));
        Assert.False(File.Exists(Path.Combine(_installRoot, "public", "old-public.txt")));
        Assert.True(File.Exists(Path.Combine(_installRoot, "runtime", "canonical-module-plan.json")));
        Assert.Empty(Directory.GetDirectories(_installRoot, ".materialize-staging-*"));
        Assert.Empty(Directory.GetDirectories(_installRoot, ".materialize-commit-*"));
    }

    [Fact]
    public void Stage_StandardDataModule_PublishesContractsAndGeneratedMetadataWithoutRuntimeCode()
    {
        var packageRoot = Path.Combine(_installRoot, "packages", "Partner.AssetCatalog", "1.0.0");
        WriteStandardDataPackage(packageRoot);
        var materializer = new RuntimeViewMaterializer(new InstalledPluginStateStore(_installRoot));

        using (var stage = materializer.Stage(_installRoot, CreateStandardDataPlan(packageRoot)))
        using (var commit = stage.BeginCommit())
        {
            commit.Complete();
        }

        var moduleRuntimeRoot = Path.Combine(_installRoot, "runtime", "modules", "Partner.AssetCatalog");
        Assert.True(File.Exists(Path.Combine(moduleRuntimeRoot, "authoring", "newp.plugin.json")));
        Assert.True(File.Exists(Path.Combine(moduleRuntimeRoot, "projections", "standard-data.json")));
        Assert.True(File.Exists(Path.Combine(moduleRuntimeRoot, "metadata", "navigation", "standard-data.nav.json")));
        Assert.True(File.Exists(Path.Combine(moduleRuntimeRoot, "metadata", "views", "AssetList.view.json")));
        Assert.True(File.Exists(Path.Combine(moduleRuntimeRoot, "metadata", "views", "AssetDetail.view.json")));
        Assert.False(Directory.Exists(Path.Combine(_installRoot, "public", "Partner.AssetCatalog")));

        using var catalog = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(_installRoot, "runtime", "active-module-runtime.json")));
        var frontend = catalog.RootElement.GetProperty("modules")[0].GetProperty("frontend");
        Assert.Equal("standard-data", frontend.GetProperty("bindingMode").GetString());
        Assert.Equal(
            "/plugins/runtime/modules/Partner.AssetCatalog/projections/standard-data.json",
            frontend.GetProperty("projectionUrl").GetString());
        Assert.Equal(
            "/plugins/runtime/modules/Partner.AssetCatalog/authoring/newp.plugin.json",
            frontend.GetProperty("blueprintUrl").GetString());
        Assert.False(frontend.TryGetProperty("url", out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(_installRoot))
        {
            Directory.Delete(_installRoot, recursive: true);
        }
    }

    private void SeedPreviousProjection()
    {
        Directory.CreateDirectory(Path.Combine(_installRoot, "runtime"));
        Directory.CreateDirectory(Path.Combine(_installRoot, "public"));
        File.WriteAllText(Path.Combine(_installRoot, "runtime", "old-runtime.txt"), "old runtime");
        File.WriteAllText(Path.Combine(_installRoot, "public", "old-public.txt"), "old public");
    }

    private void AssertPreviousProjection()
    {
        Assert.Equal(
            "old runtime",
            File.ReadAllText(Path.Combine(_installRoot, "runtime", "old-runtime.txt")));
        Assert.Equal(
            "old public",
            File.ReadAllText(Path.Combine(_installRoot, "public", "old-public.txt")));
    }

    private static CanonicalModulePlan CreatePlan(string snapshotId) =>
        new(
            snapshotId,
            "1.0",
            new string('a', 64),
            snapshotId,
            [1, 2, 3],
            []);

    private static CanonicalModulePlan CreateStandardDataPlan(string packageRoot) =>
        new(
            "candidate",
            "1.0",
            new string('b', 64),
            "standard-snapshot",
            [4, 5, 6],
            [
                new CanonicalModulePlanModule(
                    "Partner.AssetCatalog",
                    "1.0.0",
                    "Asset Catalog",
                    null,
                    packageRoot,
                    null,
                    null,
                    "projections/standard-data.json",
                    "standard-data.v1",
                    "standard-data",
                    [],
                    ["/api/standard/partner-asset-catalog/assets", "/assets"],
                    ["Partner.AssetCatalog.Asset"],
                    ["Partner.AssetCatalog.Asset"],
                    [
                        "Partner.AssetCatalog.Asset.View",
                        "Partner.AssetCatalog.Asset.Create",
                        "Partner.AssetCatalog.Asset.Update",
                        "Partner.AssetCatalog.Asset.Delete"
                    ],
                    [],
                    ["Asset"],
                    new ModulePlanProvenance(
                        "installed-package",
                        Path.Combine(packageRoot, "plugin.json"),
                        "/"))
            ]);

    private static void WriteStandardDataPackage(string packageRoot)
    {
        Directory.CreateDirectory(Path.Combine(packageRoot, "authoring"));
        Directory.CreateDirectory(Path.Combine(packageRoot, "projections"));
        File.WriteAllText(
            Path.Combine(packageRoot, "plugin.json"),
            """
            {
              "artifacts": {
                "blueprint": "authoring/newp.plugin.json",
                "standardProjection": "projections/standard-data.json"
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(packageRoot, "authoring", "newp.plugin.json"),
            """
            {
              "schemaVersion": "1.0",
              "profile": "standard-data.v1",
              "moduleId": "Partner.AssetCatalog",
              "displayName": "Asset Catalog",
              "version": "1.0.0",
              "standardData": {
                "resources": [
                  {
                    "key": "Asset",
                    "pluralName": "Assets",
                    "displayName": "Asset",
                    "displayField": "Name",
                    "fields": [
                      { "name": "Name", "type": "string", "required": true },
                      { "name": "IsEnabled", "type": "boolean", "required": false }
                    ]
                  }
                ]
              },
              "navigation": { "slot": "business", "group": "Assets", "menu": "Assets", "order": 200 }
            }
            """);
        File.WriteAllText(
            Path.Combine(packageRoot, "projections", "standard-data.json"),
            """
            {
              "moduleSlug": "partner-asset-catalog",
              "resources": [
                {
                  "resourceKey": "Asset",
                  "apiRoute": "/api/standard/partner-asset-catalog/assets",
                  "pageRoute": "/assets",
                  "permissionCodes": [
                    "Partner.AssetCatalog.Asset.View",
                    "Partner.AssetCatalog.Asset.Create",
                    "Partner.AssetCatalog.Asset.Update",
                    "Partner.AssetCatalog.Asset.Delete"
                  ]
                }
              ]
            }
            """);
    }
}
