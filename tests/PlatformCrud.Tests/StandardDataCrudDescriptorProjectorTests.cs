using System.Text.Json;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Planning;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Plugin.Contracts;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardDataCrudDescriptorProjectorTests : IDisposable
{
    private readonly string _packageRoot = Path.Combine(
        Path.GetTempPath(),
        "newp-standard-data-crud-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Project_StandardDataModule_DerivesCrudDescriptorFromBlueprintAndProjection()
    {
        WritePackageContracts();
        var plan = new CanonicalModulePlan(
            "candidate",
            "1.0",
            "plan-hash",
            "snapshot",
            [],
            [CreateModule()]);

        var descriptor = Assert.Single(new StandardDataCrudDescriptorProjector().Project(plan));
        Assert.Equal("Partner.AssetCatalog.Asset.list", descriptor.DataExecutions["list"]);
        Assert.Equal("Partner.AssetCatalog.Asset.read", descriptor.DataExecutions["read"]);
        Assert.Equal(new[] { "create", "update" }, descriptor.LegacyDataExecutions);

        Assert.Equal("Partner.AssetCatalog.Asset", descriptor.ResourceKey);
        Assert.Equal("Partner.AssetCatalog", descriptor.ModuleId);
        Assert.Equal("Asset", descriptor.ModelName);
        Assert.Equal("api/standard/partner-asset-catalog/assets", descriptor.Route);
        Assert.Equal("EID", descriptor.IdField);
        Assert.Equal("Name", descriptor.DisplayField);
        Assert.Equal(["EID", "Name", "SerialNo"], descriptor.FieldSet.ListFields);
        Assert.Equal(["Name", "SerialNo"], descriptor.FieldSet.CreateFields);
        Assert.Equal(
            "Partner.AssetCatalog.Asset.read",
            descriptor.Authorization!.GetFeatureBehavior(ICP.Platform.Crud.Descriptors.StandardCrudOperation.List));
        var requiredPolicy = Assert.Single(descriptor.FieldPolicies);
        Assert.Equal("Name", requiredPolicy.Field);
        Assert.True(requiredPolicy.StorageRequired);
        Assert.True(requiredPolicy.CreateInputRequired);
        Assert.True(requiredPolicy.UpdateNonEmpty);
        Assert.Equal(StandardCrudFieldValueOwner.Caller, requiredPolicy.ValueOwner);
        Assert.Null(requiredPolicy.CreateDefaultValue);
        var requiredDisplay = Assert.Single(descriptor.RequiredFields);
        Assert.Equal("Name", requiredDisplay.Field);
        Assert.Equal(StandardCrudRequiredFieldScope.CreateAndUpdate, requiredDisplay.Scope);
        Assert.Equal("Partner.AssetCatalog.Asset.NameRequired", requiredDisplay.ErrorCode);
        Assert.Equal("Asset的Name不能为空。", requiredDisplay.Message);
        Assert.Equal("Partner.AssetCatalog.Asset.Name.required", requiredDisplay.ReasonKey);
        Assert.Equal("SerialNo", Assert.Single(descriptor.UniqueConstraints).Fields.Single());
    }

    [Fact]
    public void Project_NonStandardModule_IsIgnored()
    {
        var module = CreateModule() with { FrontendBindingMode = "behavior-bindings" };
        var plan = new CanonicalModulePlan("candidate", "1.0", "hash", "snapshot", [], [module]);

        Assert.Empty(new StandardDataCrudDescriptorProjector().Project(plan));
    }

    public void Dispose()
    {
        if (Directory.Exists(_packageRoot))
        {
            Directory.Delete(_packageRoot, recursive: true);
        }
    }

    private CanonicalModulePlanModule CreateModule() => new CanonicalModulePlanModule(
        "Partner.AssetCatalog",
        "1.0.0",
        "Asset Catalog",
        null,
        _packageRoot,
        null,
        null,
        "projections/standard-data.json",
        "standard-data.v1",
        "standard-data",
        [],
        ["/api/standard/partner-asset-catalog/assets", "/assets"],
        ["Partner.AssetCatalog.Asset"],
        ["Partner.AssetCatalog.Asset"],
        ["Asset"],
        new ModulePlanProvenance("installed-package", Path.Combine(_packageRoot, "plugin.json"), "/"));

    private void WritePackageContracts()
    {
        Directory.CreateDirectory(Path.Combine(_packageRoot, "authoring"));
        Directory.CreateDirectory(Path.Combine(_packageRoot, "projections"));
        var blueprint = new StandardPluginBlueprint(
            "1.0",
            "standard-data.v1",
            "Partner.AssetCatalog",
            "Asset Catalog",
            "1.0.0",
            null,
            new StandardPluginCompatibility(
                new PlatformApiVersionRange("1.0.0", "2.0.0"),
                new StandardPersistenceCompatibility("ef-migrations.v1", ["sqlServer"])),
            new StandardDataBlueprint(
            [
                new StandardDataResourceBlueprint(
                    "Asset",
                    "Assets",
                    "Asset",
                    "Name",
                    [
                        new StandardDataFieldBlueprint("Name", "string", Required: true),
                        new StandardDataFieldBlueprint("SerialNo", "string", Required: false, Unique: true)
                    ])
            ]),
            new StandardNavigationBlueprint("business", "Assets", "Assets", 200),
            new StandardPersistenceBlueprint([], new Dictionary<string, StandardMigrationBundleBlueprint>()));
        var projection = new StandardPluginProjection(
            "partner-asset-catalog",
            [
                new StandardDataResourceProjection(
                    "Asset",
                    "/api/standard/partner-asset-catalog/assets",
                    "/assets",
                    "np_partner_asset_catalog",
                    "assets",
                    ["UX_assets_SerialNo"])
            ]);
        var manifest = new StandardPluginPackageManifest(
            1,
            "standard-data.v1",
            "Partner.AssetCatalog",
            "Asset Catalog",
            "1.0.0",
            null,
            new PlatformApiAttestation(
                "1.0.0",
                "1.0.0",
                "2.0.0",
                "sha256:" + new string('0', 64),
                "standard-data.v1",
                "ef-migrations.v1",
                ["sqlServer"]),
            new StandardPluginPackageArtifacts(
                "authoring/newp.plugin.json",
                "projections/standard-data.json",
                "provenance/explain.json"),
            blueprint.Persistence,
            new StandardPluginPackageChecksums("checksums/sha256sums.json"));

        File.WriteAllText(
            Path.Combine(_packageRoot, "plugin.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        File.WriteAllText(
            Path.Combine(_packageRoot, "authoring", "newp.plugin.json"),
            JsonSerializer.Serialize(blueprint, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        File.WriteAllText(
            Path.Combine(_packageRoot, "projections", "standard-data.json"),
            JsonSerializer.Serialize(projection, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
