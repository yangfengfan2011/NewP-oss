using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Modeling.Projection;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        var projector = new ModelRuntimeMetadataProjector();
        var materializer = new RuntimeViewMaterializer(
            new InstalledPluginStateStore(_installRoot),
            new FrontendRuntimeManifestBuilder(),
            builtInModuleSource: null,
            projector.Project);

        var candidate = CreateStandardDataPlan(packageRoot);
        using (var stage = materializer.Stage(_installRoot, candidate))
        using (var commit = stage.BeginCommit())
        {
            commit.Complete();
        }

        var moduleRuntimeRoot = Path.Combine(_installRoot, "runtime", "modules", "Partner.AssetCatalog");
        Assert.True(File.Exists(Path.Combine(moduleRuntimeRoot, "authoring", "newp.plugin.json")));
        Assert.True(File.Exists(Path.Combine(moduleRuntimeRoot, "projections", "standard-data.json")));
        Assert.True(File.Exists(Path.Combine(moduleRuntimeRoot, "metadata", "navigation", "standard-data.nav.json")));
        Assert.True(File.Exists(Path.Combine(moduleRuntimeRoot, "metadata", "views", "AssetList.view.json")));
        var runtimeFeatures = FeatureCatalogCompiler.Compile(StandardFeatureMetadataCompiler.Read("Partner.AssetCatalog",
            StandardDataMetadataProjection.ReadDocuments(Path.Combine(moduleRuntimeRoot, "metadata")).Values
                .Select(document => JsonSerializer.SerializeToElement(document))));
        Assert.Equal(candidate.FeatureCatalog.Select(point => (point.Position, point.BehaviorKey)),
            runtimeFeatures.Select(point => (point.Position, point.BehaviorKey)));
        Assert.Equal(["", "Partner.AssetCatalog.Asset.create", "Partner.AssetCatalog.Asset.disable", "Partner.AssetCatalog.Asset.enable", "Partner.AssetCatalog.Asset.read", "Partner.AssetCatalog.Asset.update"],
            runtimeFeatures.Select(point => point.BehaviorKey).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(runtimeFeatures, point => point.Key == "refresh");
        var modelPath = Path.Combine(moduleRuntimeRoot, "metadata", "models", "Asset.model.json");
        Assert.True(File.Exists(modelPath));
        var detailViewPath = Path.Combine(moduleRuntimeRoot, "metadata", "views", "AssetDetail.view.json");
        Assert.True(File.Exists(detailViewPath));
        Assert.False(Directory.Exists(Path.Combine(_installRoot, "public", "Partner.AssetCatalog")));

        using var listView = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(moduleRuntimeRoot, "metadata", "views", "AssetList.view.json")));
        var rowActions = listView.RootElement
            .GetProperty("view")
            .GetProperty("rowActions")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(
            ["view", "edit", "disable", "enable"],
            rowActions.Select(static action => action.GetProperty("key").GetString()));
        Assert.DoesNotContain(rowActions, static action => action.GetProperty("key").GetString() == "delete");
        AssertStateAction(rowActions, "disable", expectedEnabledValue: true);
        AssertStateAction(rowActions, "enable", expectedEnabledValue: false);
        Assert.Equal(
            "partner.asset.detail",
            listView.RootElement.GetProperty("view").GetProperty("detailViewKey").GetString());

        using var detailView = JsonDocument.Parse(File.ReadAllText(detailViewPath));
        var detail = detailView.RootElement.GetProperty("view");
        Assert.Equal("Author-owned detail", detail.GetProperty("titleKey").GetString());
        Assert.Equal(
            ["Name", "IsEnabled"],
            detail.GetProperty("sections")[0].GetProperty("fields").EnumerateArray()
                .Select(static field => field.GetString()));

        using var modelDocument = JsonDocument.Parse(File.ReadAllText(modelPath));
        var model = modelDocument.RootElement.GetProperty("Asset");
        var contractPath = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "Kernel.Tests",
            "Fixtures",
            "StandardDataRuntimeModel.contract.json");
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(File.ReadAllBytes(contractPath)),
            JsonNode.Parse(File.ReadAllBytes(modelPath))));
        var nameField = model.GetProperty("fields").EnumerateArray()
            .Single(static field => field.GetProperty("name").GetString() == "Name");
        Assert.False(nameField.GetProperty("storage").GetProperty("nullable").GetBoolean());
        Assert.True(nameField.GetProperty("form").GetProperty("create").GetProperty("required").GetBoolean());
        Assert.True(nameField.GetProperty("form").GetProperty("edit").GetProperty("required").GetBoolean());
        Assert.False(nameField.GetProperty("form").GetProperty("create").TryGetProperty("defaultValue", out _));
        var fields = model.GetProperty("fields").EnumerateArray().ToArray();
        Assert.Equal(["Name", "IsEnabled", "EID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"],
            fields.Select(static field => field.GetProperty("name").GetString()));
        foreach (var fieldName in new[] { "EID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" })
        {
            var fixedField = fields.Single(field => field.GetProperty("name").GetString() == fieldName);
            Assert.Equal("hidden", fixedField.GetProperty("form").GetProperty("create").GetProperty("mode").GetString());
            Assert.Equal("hidden", fixedField.GetProperty("form").GetProperty("edit").GetProperty("mode").GetString());
        }

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

    [Fact]
    public void Stage_StandardDataModelProjectionFails_PreservesPreviousProjectionAndCleansStaging()
    {
        SeedPreviousProjection();
        var packageRoot = Path.Combine(_installRoot, "packages", "Partner.AssetCatalog", "1.0.0");
        WriteStandardDataPackage(packageRoot);
        var projectionCalls = 0;
        var materializer = new RuntimeViewMaterializer(
            new InstalledPluginStateStore(_installRoot),
            new FrontendRuntimeManifestBuilder(),
            builtInModuleSource: null,
            (_, sourcePath) =>
            {
                projectionCalls++;
                Assert.EndsWith(
                    Path.Combine("metadata", "models", "Asset.model.json"),
                    sourcePath,
                    StringComparison.OrdinalIgnoreCase);
                throw new InvalidOperationException("standard-data model projection failed");
            });

        var error = Assert.Throws<InvalidOperationException>(
            () => materializer.Stage(_installRoot, CreateStandardDataPlan(packageRoot)));

        Assert.Equal("standard-data model projection failed", error.Message);
        Assert.Equal(1, projectionCalls);
        AssertPreviousProjection();
        Assert.Empty(Directory.GetDirectories(_installRoot, ".materialize-staging-*"));
        Assert.Empty(Directory.GetDirectories(_installRoot, ".materialize-commit-*"));
    }

    [Fact]
    public void Stage_StandardDataModuleWithoutDetailDeclaration_DoesNotPublishDetailCapability()
    {
        var packageRoot = Path.Combine(_installRoot, "packages", "Partner.AssetCatalog", "1.0.0");
        WriteStandardDataPackage(packageRoot, "undeclared");
        var materializer = new RuntimeViewMaterializer(new InstalledPluginStateStore(_installRoot));

        using (var stage = materializer.Stage(_installRoot, CreateStandardDataPlan(packageRoot)))
        using (var commit = stage.BeginCommit())
        {
            commit.Complete();
        }

        var viewRoot = Path.Combine(
            _installRoot,
            "runtime",
            "modules",
            "Partner.AssetCatalog",
            "metadata",
            "views");
        Assert.False(File.Exists(Path.Combine(viewRoot, "AssetDetail.view.json")));
        using var listView = JsonDocument.Parse(File.ReadAllText(Path.Combine(viewRoot, "AssetList.view.json")));
        var list = listView.RootElement.GetProperty("view");
        Assert.False(list.TryGetProperty("detailViewKey", out _));
        Assert.DoesNotContain(
            list.GetProperty("rowActions").EnumerateArray(),
            static action => action.GetProperty("key").GetString() == "view");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("wrong-mode")]
    [InlineData("wrong-model")]
    public void Stage_DeclaredDetailViewReferenceIsInvalid_RejectsWholeCandidate(string scenario)
    {
        var packageRoot = Path.Combine(_installRoot, "packages", "Partner.AssetCatalog", "1.0.0");
        WriteStandardDataPackage(packageRoot, scenario);
        var materializer = new RuntimeViewMaterializer(new InstalledPluginStateStore(_installRoot));

        Assert.Throws<JsonException>(() => materializer.Stage(_installRoot, CreateStandardDataPlan(packageRoot)));

        Assert.False(Directory.Exists(Path.Combine(_installRoot, "runtime")));
        Assert.Empty(Directory.GetDirectories(_installRoot, ".materialize-staging-*"));
    }

    private static void AssertStateAction(
        IReadOnlyList<JsonElement> rowActions,
        string key,
        bool expectedEnabledValue)
    {
        var action = Assert.Single(rowActions, item => item.GetProperty("key").GetString() == key);
        Assert.Equal(key, action.GetProperty("standardAction").GetString());
        Assert.False(action.TryGetProperty("permissions", out _));
        var visibility = action.GetProperty("visibleWhen");
        Assert.Equal("IsEnabled", visibility.GetProperty("field").GetString());
        Assert.Equal("equals", visibility.GetProperty("operator").GetString());
        Assert.Equal(expectedEnabledValue, visibility.GetProperty("value").GetBoolean());
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

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ICP-Platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

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
                    [],
                    [],
                    ["Asset"],
                    new ModulePlanProvenance(
                        "installed-package",
                        Path.Combine(packageRoot, "plugin.json"),
                        "/")) { Features = ReadStandardDataFeatures(packageRoot) }
            ]);

    private static IReadOnlyList<FeatureDeclaration> ReadStandardDataFeatures(string packageRoot)
    {
        var documents = StandardDataMetadataProjection.Project("Partner.AssetCatalog",
            JsonNode.Parse(File.ReadAllText(Path.Combine(packageRoot, "authoring/newp.plugin.json")))!.AsObject(),
            JsonNode.Parse(File.ReadAllText(Path.Combine(packageRoot, "projections/standard-data.json")))!.AsObject(),
            StandardDataMetadataProjection.ReadDocuments(Path.Combine(packageRoot, "metadata")));
        return StandardFeatureMetadataCompiler.Read("Partner.AssetCatalog", documents.Values.Select(document => JsonSerializer.SerializeToElement(document)));
    }

    private static void WriteStandardDataPackage(string packageRoot, string detailScenario = "valid")
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
        var blueprint =
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
            """;
        if (!string.Equals(detailScenario, "undeclared", StringComparison.Ordinal))
        {
            blueprint = blueprint.Replace(
                "\"displayField\": \"Name\",",
                "\"displayField\": \"Name\",\n                \"detailViewKey\": \"partner.asset.detail\",",
                StringComparison.Ordinal);
        }

        File.WriteAllText(
            Path.Combine(packageRoot, "authoring", "newp.plugin.json"),
            blueprint);
        var projection =
            """
            {
              "moduleSlug": "partner-asset-catalog",
              "resources": [
                {
                  "resourceKey": "Asset",
                  "apiRoute": "/api/standard/partner-asset-catalog/assets",
                  "pageRoute": "/assets",
                  "permissionCodes": []
                }
              ]
            }
            """;
        if (!string.Equals(detailScenario, "undeclared", StringComparison.Ordinal))
        {
            projection = projection.Replace(
                "\"pageRoute\": \"/assets\",",
                "\"pageRoute\": \"/assets\",\n          \"detailViewKey\": \"partner.asset.detail\",",
                StringComparison.Ordinal);
        }

        File.WriteAllText(
            Path.Combine(packageRoot, "projections", "standard-data.json"),
            projection);

        if (!string.Equals(detailScenario, "missing", StringComparison.Ordinal)
            && !string.Equals(detailScenario, "undeclared", StringComparison.Ordinal))
        {
            var viewsRoot = Path.Combine(packageRoot, "metadata", "views");
            Directory.CreateDirectory(viewsRoot);
            var mode = string.Equals(detailScenario, "wrong-mode", StringComparison.Ordinal) ? "list" : "detail";
            var model = string.Equals(detailScenario, "wrong-model", StringComparison.Ordinal) ? "Other" : "Asset";
            var authoredDetail =
            """
            {
              "schemaVersion": "1.0",
              "type": "view",
              "view": {
                "key": "partner.asset.detail",
                "titleKey": "Author-owned detail",
                "mode": "__MODE__",
                "model": "__MODEL__",
                "sections": [
                  { "key": "basic", "titleKey": "Basic", "fields": ["Name", "IsEnabled"] }
                ]
              }
            }
            """
                .Replace("__MODE__", mode, StringComparison.Ordinal)
                .Replace("__MODEL__", model, StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(viewsRoot, "AssetDetail.view.json"), authoredDetail);
            if (string.Equals(detailScenario, "duplicate", StringComparison.Ordinal))
            {
                File.WriteAllText(Path.Combine(viewsRoot, "AssetDetailDuplicate.view.json"), authoredDetail);
            }
        }
    }
}
