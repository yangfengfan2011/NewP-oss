using ICP.Platform.Kernel.PluginDiscovery;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Modeling.Projection;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public class RuntimeViewMaterializerTests : IDisposable
{
    [Fact]
    public void Project_RealPosition_ProducesButtonHostRuntimeContract()
    {
        var sourcePath = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(FindRepositoryRoot(), "Organization"),
            "metadata", "models", "Position.model.json");
        var authoring = File.ReadAllBytes(sourcePath);
        var runtime = new ModelRuntimeMetadataProjector().Project(authoring, sourcePath);
        var contract = Path.Combine(FindRepositoryRoot(), "tests", "Kernel.Tests", "Fixtures", "PositionRuntime.contract.json");
        if (Environment.GetEnvironmentVariable("NEWP_POSITION_MODEL_FIXTURE_EXPORT") == "1")
            File.WriteAllText(contract, Encoding.UTF8.GetString(runtime).Replace("\r\n", "\n").Replace("\n", "\r\n"));
        else Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllBytes(contract)), JsonNode.Parse(runtime)));
        Assert.Equal(authoring, File.ReadAllBytes(sourcePath));
    }

    private readonly UnifiedPluginPackageTestData _testData;
    private readonly List<string> _installRoots = new();

    public RuntimeViewMaterializerTests()
    {
        _testData = new UnifiedPluginPackageTestData();
    }

    [Fact]
    public void ValidateAndMaterialize_ProjectEveryRuntimeModelWithoutChangingAuthoringOrPlan()
    {
        var packageRoot = _testData.CreateValidPackageWithMetadataAndResources("Organization");
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(packageRoot, installRoot, "Organization");
        var metadataRoot = Path.Combine(
            installRoot,
            "packages",
            "Organization",
            "1.0.0",
            "modules",
            "Organization",
            "metadata");
        var modelsRoot = Path.Combine(metadataRoot, "models");
        Directory.CreateDirectory(modelsRoot);
        var companyPath = WriteModel(modelsRoot, "Company");
        var departmentPath = WriteModel(modelsRoot, "Department");
        var authoringCompany = File.ReadAllBytes(companyPath);
        var authoringDepartment = File.ReadAllBytes(departmentPath);
        var settingsPath = Path.Combine(metadataRoot, "settings", "Organization.settings.json");
        var authoringSettings = File.ReadAllBytes(settingsPath);
        var stateStore = new InstalledPluginStateStore(installRoot);
        var baselinePlan = new RuntimeViewMaterializer(stateStore).Materialize(installRoot);
        var projectedPaths = new List<string>();
        var materializer = new RuntimeViewMaterializer(
            stateStore,
            new FrontendRuntimeManifestBuilder(),
            builtInModuleSource: null,
            (bytes, sourcePath) =>
            {
                projectedPaths.Add(Path.GetFileName(sourcePath));
                var json = Encoding.UTF8.GetString(bytes.Span);
                return Encoding.UTF8.GetBytes(json.Replace(
                    "\"kind\": \"Entity\"",
                    "\"kind\": \"Entity\", \"runtimeProjected\": true",
                    StringComparison.Ordinal));
            });

        projectedPaths.Clear();
        materializer.ValidateModelMetadata(baselinePlan);
        Assert.Equal(["Company.model.json", "Department.model.json"], projectedPaths);

        projectedPaths.Clear();
        var projectedPlan = materializer.Materialize(installRoot);

        Assert.Equal(["Company.model.json", "Department.model.json"], projectedPaths);
        Assert.Equal(baselinePlan.PlanHash, projectedPlan.PlanHash);
        Assert.Equal(baselinePlan.CanonicalBytes, projectedPlan.CanonicalBytes);
        Assert.Equal(authoringCompany, File.ReadAllBytes(companyPath));
        Assert.Equal(authoringDepartment, File.ReadAllBytes(departmentPath));
        Assert.Equal(authoringSettings, File.ReadAllBytes(settingsPath));
        Assert.Contains(
            "\"runtimeProjected\": true",
            File.ReadAllText(Path.Combine(installRoot, "runtime", "modules", "Organization", "metadata", "models", "Company.model.json")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Materialize_WhenModelProjectionFails_PreservesActiveRuntimeAndRemovesStaging()
    {
        var packageRoot = _testData.CreateValidPackageWithMetadataAndResources("Organization");
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(packageRoot, installRoot, "Organization");
        var modelsRoot = Path.Combine(
            installRoot,
            "packages",
            "Organization",
            "1.0.0",
            "modules",
            "Organization",
            "metadata",
            "models");
        Directory.CreateDirectory(modelsRoot);
        WriteModel(modelsRoot, "Company");
        var stateStore = new InstalledPluginStateStore(installRoot);
        new RuntimeViewMaterializer(stateStore).Materialize(installRoot);
        var runtimeModelPath = Path.Combine(
            installRoot,
            "runtime",
            "modules",
            "Organization",
            "metadata",
            "models",
            "Company.model.json");
        var previousRuntime = File.ReadAllBytes(runtimeModelPath);
        var materializer = new RuntimeViewMaterializer(
            stateStore,
            new FrontendRuntimeManifestBuilder(),
            builtInModuleSource: null,
            (_, _) => throw new InvalidOperationException("model projection failed"));

        var error = Assert.Throws<InvalidOperationException>(() => materializer.Materialize(installRoot));

        Assert.Contains("model projection failed", error.Message, StringComparison.Ordinal);
        Assert.Equal(previousRuntime, File.ReadAllBytes(runtimeModelPath));
        Assert.Empty(Directory.EnumerateDirectories(installRoot, ".materialize-staging-*"));
    }

    [Fact]
    public void Materialize_RealOrganizationModels_ProduceSharedCanonicalRuntimeContracts()
    {
        var packageRoot = _testData.CreateValidPackageWithMetadataAndResources("Organization");
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(packageRoot, installRoot, "Organization");
        var installedModelsRoot = Path.Combine(
            installRoot,
            "packages",
            "Organization",
            "1.0.0",
            "modules",
            "Organization",
            "metadata",
            "models");
        Directory.CreateDirectory(installedModelsRoot);
        var repositoryModelsRoot = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(FindRepositoryRoot(), "Organization"), "metadata", "models");
        var companyAuthoringPath = WriteModelWithoutField(repositoryModelsRoot, installedModelsRoot, "Company", "UpdatedAt");
        var departmentAuthoringPath = WriteModelWithoutField(repositoryModelsRoot, installedModelsRoot, "Department", "UpdatedAt");
        var personAuthoringPath = CopyModel(repositoryModelsRoot, installedModelsRoot, "Person");
        var companyAuthoring = File.ReadAllBytes(companyAuthoringPath);
        var departmentAuthoring = File.ReadAllBytes(departmentAuthoringPath);
        var personAuthoring = File.ReadAllBytes(personAuthoringPath);
        var stateStore = new InstalledPluginStateStore(installRoot);

        var unprojectedPlan = new RuntimeViewMaterializer(stateStore).Materialize(installRoot);
        var unprojectedCompanyPath = RuntimeModelPath(installRoot, "Company");
        var unprojectedDepartmentPath = RuntimeModelPath(installRoot, "Department");
        var unprojectedPersonPath = RuntimeModelPath(installRoot, "Person");
        Assert.False(HasField(unprojectedCompanyPath, "Company", "UpdatedAt"));
        Assert.False(HasField(unprojectedDepartmentPath, "Department", "UpdatedAt"));

        var projector = new ModelRuntimeMetadataProjector();
        var projectedPlan = new RuntimeViewMaterializer(
            stateStore,
            new FrontendRuntimeManifestBuilder(),
            builtInModuleSource: null,
            projector.Project).Materialize(installRoot);

        var expectedContractPath = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "Kernel.Tests",
            "Fixtures",
            "OrganizationRuntimeModels.contract.json");
        var expectedContracts = JsonNode.Parse(File.ReadAllBytes(expectedContractPath))!.AsObject();
        Assert.True(JsonNode.DeepEquals(expectedContracts["company"], JsonNode.Parse(File.ReadAllBytes(unprojectedCompanyPath))));
        Assert.True(JsonNode.DeepEquals(expectedContracts["department"], JsonNode.Parse(File.ReadAllBytes(unprojectedDepartmentPath))));
        Assert.True(JsonNode.DeepEquals(expectedContracts["person"], JsonNode.Parse(File.ReadAllBytes(unprojectedPersonPath))));
        Assert.Equal("hidden", ReadMode(unprojectedCompanyPath, "Company", "UpdatedAt", "create"));
        Assert.Equal("hidden", ReadMode(unprojectedCompanyPath, "Company", "UpdatedAt", "edit"));
        Assert.Equal("hidden", ReadMode(unprojectedDepartmentPath, "Department", "UpdatedAt", "create"));
        Assert.Equal("hidden", ReadMode(unprojectedDepartmentPath, "Department", "UpdatedAt", "edit"));
        Assert.Equal("hidden", ReadMode(unprojectedCompanyPath, "Company", "IsEnabled", "create"));
        Assert.Equal("editable", ReadMode(unprojectedCompanyPath, "Company", "IsEnabled", "edit"));
        Assert.Equal("hidden", ReadMode(unprojectedDepartmentPath, "Department", "IsEnabled", "create"));
        Assert.Equal("editable", ReadMode(unprojectedDepartmentPath, "Department", "IsEnabled", "edit"));
        Assert.Equal(companyAuthoring, File.ReadAllBytes(companyAuthoringPath));
        Assert.Equal(departmentAuthoring, File.ReadAllBytes(departmentAuthoringPath));
        Assert.Equal(personAuthoring, File.ReadAllBytes(personAuthoringPath));
        Assert.Equal(unprojectedPlan.PlanHash, projectedPlan.PlanHash);
        Assert.Equal(unprojectedPlan.CanonicalBytes, projectedPlan.CanonicalBytes);
    }

    [Fact]
    public void Project_RealAuthorizationRole_ProducesSharedCanonicalRuntimeContract()
    {
        var sourcePath = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(FindRepositoryRoot(), "Authorization"), "metadata", "models", "Role.model.json");
        var authoring = File.ReadAllBytes(sourcePath);
        var runtimeBytes = new ModelRuntimeMetadataProjector().Project(authoring, sourcePath);
        var contractPath = Path.Combine(FindRepositoryRoot(), "tests", "Kernel.Tests", "Fixtures", "AuthorizationRoleRuntime.contract.json");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllBytes(contractPath)), JsonNode.Parse(runtimeBytes)));
        Assert.Equal(File.ReadAllBytes(sourcePath), authoring);
    }

    [Fact]
    public void Project_RealAuditBusinessChange_ProducesSharedCanonicalRuntimeContract()
    {
        var sourcePath = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(FindRepositoryRoot(), "Audit"), "metadata", "models", "BusinessChange.model.json");
        var authoring = File.ReadAllBytes(sourcePath);
        var runtimeBytes = new ModelRuntimeMetadataProjector().Project(authoring, sourcePath);
        var contractPath = Path.Combine(FindRepositoryRoot(), "tests", "Kernel.Tests", "Fixtures", "AuditBusinessChangeRuntime.contract.json");
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllBytes(contractPath)), JsonNode.Parse(runtimeBytes)));
        Assert.Equal(File.ReadAllBytes(sourcePath), authoring);
    }

    [Fact]
    public void Project_RealIdentityAccount_ProducesSharedCanonicalRuntimeContract()
    {
        var sourcePath = Path.Combine(
            ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(FindRepositoryRoot(), "Identity"),
            "metadata",
            "models",
            "IdentityAccount.model.json");
        var authoring = File.ReadAllBytes(sourcePath);

        var runtimeBytes = new ModelRuntimeMetadataProjector().Project(authoring, sourcePath);

        var contractPath = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "Kernel.Tests",
            "Fixtures",
            "IdentityRuntimeModels.contract.json");
        var expected = JsonNode.Parse(File.ReadAllBytes(contractPath))!.AsObject();
        Assert.True(JsonNode.DeepEquals(expected["identityAccount"], JsonNode.Parse(runtimeBytes)));
        Assert.Equal(File.ReadAllBytes(sourcePath), authoring);
    }

    [Fact]
    public void MaterializeEnabledPlugin_CreatesHostLoadableBackendView_FrontendManifest_AndPublicAssets()
    {
        var packageRoot = _testData.CreateValidPackage();
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var installResult = installer.Install(packageRoot, installRoot);
        Assert.Equal("Installed", installResult.Status);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        record!.CurrentEnabledVersion = record.Version;
        Assert.True(stateStore.TrySave(record, out var saveError));
        Assert.Null(saveError);

        var materializer = new RuntimeViewMaterializer(stateStore);

        materializer.Materialize(installRoot);

        var backendViewRoot = Path.Combine(installRoot, "runtime", "backend-load-view", "Audit");
        var backendViewPath = Path.Combine(backendViewRoot, "plugin.json");
        var backendEntryAssemblyPath = Path.Combine(backendViewRoot, "AuditModule.dll");
        var frontendManifestPath = Path.Combine(installRoot, "runtime", "frontend-plugin-manifest.json");
        var stagedEntryPath = Path.Combine(installRoot, "public", "Audit", "1.0.0", "assets", "entry.js");
        var stagedFrontendManifestPath = Path.Combine(installRoot, "public", "Audit", "1.0.0", "manifest.json");

        Assert.True(File.Exists(backendViewPath));
        Assert.Equal(
            File.ReadAllText(Path.Combine(installRoot, "packages", "Audit", "1.0.0", "backend", "plugin.backend.json")),
            File.ReadAllText(backendViewPath));
        Assert.True(File.Exists(backendEntryAssemblyPath));

        var pluginScanner = new PluginScanner();
        var scanResult = pluginScanner.ScanPlugin(backendViewRoot);
        Assert.True(scanResult.IsSuccess);
        Assert.Equal("Audit", scanResult.Manifest!.ModuleId);
        Assert.Equal(backendEntryAssemblyPath, scanResult.ResolvedEntryAssemblyPath);

        Assert.True(File.Exists(frontendManifestPath));
        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(frontendManifestPath));
        var plugins = manifestDocument.RootElement.GetProperty("plugins");
        Assert.Equal(JsonValueKind.Array, plugins.ValueKind);
        Assert.Single(plugins.EnumerateArray());
        Assert.Equal("/plugin-assets/Audit/1.0.0/assets/entry.js", plugins[0].GetProperty("url").GetString());

        Assert.True(File.Exists(stagedEntryPath));
        Assert.True(File.Exists(stagedFrontendManifestPath));
    }

    [Fact]
    public void Materialize_UsesCamelCaseFrontendManifestKeys_FromInstalledPackage()
    {
        var packageRoot = _testData.CreateValidPackage();
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(packageRoot, installRoot, "Audit");

        var installedFrontendManifestPath = Path.Combine(installRoot, "packages", "Audit", "1.0.0", "frontend", "plugin.frontend.json");
        File.WriteAllText(
            installedFrontendManifestPath,
            JsonSerializer.Serialize(
                new
                {
                    moduleId = "Audit",
                    entryManifest = "manifest.json",
                    assetRoot = "assets",
                    publicBasePath = "/plugins/Audit/1.0.0"
                },
                new JsonSerializerOptions { WriteIndented = true }));

        var stateStore = new InstalledPluginStateStore(installRoot);
        var materializer = new RuntimeViewMaterializer(stateStore);

        materializer.Materialize(installRoot);

        var frontendManifestPath = Path.Combine(installRoot, "runtime", "frontend-plugin-manifest.json");
        var stagedEntryPath = Path.Combine(installRoot, "public", "Audit", "1.0.0", "assets", "entry.js");
        var stagedFrontendManifestPath = Path.Combine(installRoot, "public", "Audit", "1.0.0", "manifest.json");

        Assert.True(File.Exists(frontendManifestPath));
        Assert.True(File.Exists(stagedEntryPath));
        Assert.True(File.Exists(stagedFrontendManifestPath));

        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(frontendManifestPath));
        Assert.Equal(
            "/plugin-assets/Audit/1.0.0/assets/entry.js",
            manifestDocument.RootElement.GetProperty("plugins")[0].GetProperty("url").GetString());
    }

    [Fact]
    public void MaterializeEnabledPlugin_StagesModuleMetadata_AndPlatformResourcesIntoRuntime()
    {
        var packageRoot = _testData.CreateValidPackageWithMetadataAndResources();
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(packageRoot, installRoot, "Audit");

        var stateStore = new InstalledPluginStateStore(installRoot);
        var materializer = new RuntimeViewMaterializer(stateStore);

        materializer.Materialize(installRoot);

        var runtimeModuleManifestPath = Path.Combine(installRoot, "runtime", "modules", "Audit", "metadata", "metadata.manifest.json");
        var runtimeModuleSettingsPath = Path.Combine(installRoot, "runtime", "modules", "Audit", "metadata", "settings", "Audit.settings.json");
        var runtimeLanguagesPath = Path.Combine(installRoot, "runtime", "resources", "localization", "languages.json");

        Assert.True(File.Exists(runtimeModuleManifestPath));
        Assert.True(File.Exists(runtimeModuleSettingsPath));
        Assert.True(File.Exists(runtimeLanguagesPath));
        Assert.Contains("\"moduleId\": \"Audit\"", File.ReadAllText(runtimeModuleManifestPath));
        Assert.Contains("\"plugin-default\"", File.ReadAllText(runtimeModuleSettingsPath));
        Assert.Contains("\"defaultLanguage\": \"zhs\"", File.ReadAllText(runtimeLanguagesPath));
    }

    [Fact]
    public void MaterializeEnabledPlugin_WritesLocalizationManifest_FromDictionaryMetadata()
    {
        var packageRoot = CreatePackageWithLocalizationMetadata();
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(packageRoot, installRoot, "Audit");
        EnableInstalledPackage(CreatePlatformPackageWithLanguages(), installRoot, "Platform");

        var stateStore = new InstalledPluginStateStore(installRoot);
        var materializer = new RuntimeViewMaterializer(stateStore);

        materializer.Materialize(installRoot);

        var localizationManifestPath = Path.Combine(installRoot, "runtime", "localization-manifest.json");

        Assert.True(File.Exists(localizationManifestPath));
        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(localizationManifestPath));
        Assert.Equal("zhs", manifestDocument.RootElement.GetProperty("defaultLanguage").GetString());

        var supportedLanguages = manifestDocument.RootElement.GetProperty("supportedLanguages");
        Assert.Equal(2, supportedLanguages.GetArrayLength());

        var catalogs = manifestDocument.RootElement.GetProperty("catalogs");
        Assert.True(catalogs.GetProperty("zhs").TryGetProperty("ValueLists.CodeAlreadyExists", out var zhsValueListCodeAlreadyExists));
        Assert.True(catalogs.GetProperty("en").TryGetProperty("ValueLists.CodeAlreadyExists", out var enValueListCodeAlreadyExists));
        Assert.Equal("添加", catalogs.GetProperty("zhs").GetProperty("Common.Add").GetString());
        Assert.Equal("Add", catalogs.GetProperty("en").GetProperty("Common.Add").GetString());
        Assert.Equal("页面标题", catalogs.GetProperty("zhs").GetProperty("Pages.ListTitle").GetString());
        Assert.Equal("List title", catalogs.GetProperty("en").GetProperty("Pages.ListTitle").GetString());
        Assert.Equal("值列表编码 {code} 已存在。", zhsValueListCodeAlreadyExists.GetString());
        Assert.Equal("Value list code {code} already exists.", enValueListCodeAlreadyExists.GetString());
    }

    [Fact]
    public void MaterializeEnabledPlugin_WritesMetadataRuntimeManifest()
    {
        var packageRoot = _testData.CreateValidPackageWithMetadataAndResources("Organization", "1.0.0", "zhs");
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(packageRoot, installRoot, "Organization");

        var packageMetadataRoot = Path.Combine(installRoot, "packages", "Organization", "1.0.0", "modules", "Organization", "metadata");
        Directory.CreateDirectory(Path.Combine(packageMetadataRoot, "navigation"));
        Directory.CreateDirectory(Path.Combine(packageMetadataRoot, "views"));
        Directory.CreateDirectory(Path.Combine(packageMetadataRoot, "models"));
        File.WriteAllText(
            Path.Combine(packageMetadataRoot, "navigation", "Organization.nav.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "nav",
              "navigation": {
                "items": []
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(packageMetadataRoot, "views", "PersonList.view.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "view",
              "view": {
                "key": "organization.person.list",
                "mode": "list",
                "model": "Person"
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(packageMetadataRoot, "models", "Person.model.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "model",
              "Person": {
                "kind": "Entity"
              }
            }
            """);

        var stateStore = new InstalledPluginStateStore(installRoot);
        var materializer = new RuntimeViewMaterializer(stateStore);

        materializer.Materialize(installRoot);

        var manifestPath = Path.Combine(installRoot, "runtime", "metadata-runtime-manifest.json");
        Assert.True(File.Exists(manifestPath));

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var modules = document.RootElement.GetProperty("modules");
        Assert.Single(modules.EnumerateArray());
        Assert.Equal("Organization", modules[0].GetProperty("moduleId").GetString());
        Assert.Equal("navigation/Organization.nav.json", modules[0].GetProperty("navFiles")[0].GetString());
        Assert.Equal("views/PersonList.view.json", modules[0].GetProperty("viewFiles")[0].GetString());
        Assert.Equal("models/Person.model.json", modules[0].GetProperty("modelFiles")[0].GetString());
    }

    [Fact]
    public void MaterializeEnabledPlugin_WhenLanguagesJsonIsMissing_WritesMinimalLocalizationManifest()
    {
        var packageRoot = CreatePackageWithLocalizationMetadata();
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(packageRoot, installRoot, "Audit");

        var stateStore = new InstalledPluginStateStore(installRoot);
        var materializer = new RuntimeViewMaterializer(stateStore);

        materializer.Materialize(installRoot);

        var localizationManifestPath = Path.Combine(installRoot, "runtime", "localization-manifest.json");

        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(localizationManifestPath));
        Assert.Equal("zhs", manifestDocument.RootElement.GetProperty("defaultLanguage").GetString());
        Assert.Empty(manifestDocument.RootElement.GetProperty("supportedLanguages").EnumerateArray());
        Assert.Empty(manifestDocument.RootElement.GetProperty("catalogs").EnumerateObject());
    }

    [Fact]
    public void MaterializeEnabledPlugin_WhenDictionaryMissingSupportedLanguage_Throws()
    {
        var packageRoot = CreatePackageWithLocalizationMetadata(includeEnglishTranslation: false);
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(packageRoot, installRoot, "Audit");
        EnableInstalledPackage(CreatePlatformPackageWithLanguages(), installRoot, "Platform");

        var stateStore = new InstalledPluginStateStore(installRoot);
        var materializer = new RuntimeViewMaterializer(stateStore);

        var exception = Assert.ThrowsAny<Exception>(() => materializer.Materialize(installRoot));
        Assert.Contains("en", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Materialize_WhenPublicSwapFailsAfterRuntimeSwap_RestoresLocalizationManifest()
    {
        var packageRoot = CreatePackageWithLocalizationMetadata();
        var platformPackageRoot = CreatePlatformPackageWithLanguages();
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(packageRoot, installRoot, "Audit");
        EnableInstalledPackage(platformPackageRoot, installRoot, "Platform");

        var stateStore = new InstalledPluginStateStore(installRoot);
        var baselineMaterializer = new RuntimeViewMaterializer(stateStore);
        baselineMaterializer.Materialize(installRoot);

        var localizationManifestPath = Path.Combine(installRoot, "runtime", "localization-manifest.json");
        var previousLocalizationManifest = File.ReadAllText(localizationManifestPath);

        File.WriteAllText(
            Path.Combine(installRoot, "packages", "Audit", "1.0.0", "modules", "Audit", "metadata", "dictionaries", "Common.dic.json"),
            JsonSerializer.Serialize(
                new Dictionary<string, object?>
                {
                    ["schemaVersion"] = "1.0",
                    ["type"] = "dic",
                    ["Common"] = new Dictionary<string, object?>
                    {
                        ["Add"] = new Dictionary<string, string>
                        {
                            ["zhs"] = "新增",
                            ["en"] = "Add"
                        }
                    }
                },
                new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(
            Path.Combine(installRoot, "packages", "Audit", "1.0.0", "frontend", "assets", "entry.js"),
            "updated-entry");

        var materializer = CreateMaterializerWithSwapFailure(stateStore, 4, "Simulated public swap failure");

        var exception = Assert.Throws<IOException>(() => materializer.Materialize(installRoot));
        Assert.Contains("public swap failure", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(previousLocalizationManifest, File.ReadAllText(localizationManifestPath));
    }

    [Fact]
    public void Materialize_WhenEnabledPluginsShareIdenticalPlatformResources_AllowsProjection()
    {
        var auditPackageRoot = _testData.CreateValidPackageWithMetadataAndResources("Audit", "1.0.0", "zhs");
        var filesPackageRoot = _testData.CreateValidPackageWithMetadataAndResources("Files", "1.0.0", "zhs");
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(auditPackageRoot, installRoot, "Audit");
        EnableInstalledPackage(filesPackageRoot, installRoot, "Files");

        var stateStore = new InstalledPluginStateStore(installRoot);
        var materializer = new RuntimeViewMaterializer(stateStore);

        materializer.Materialize(installRoot);

        var runtimeLanguagesPath = Path.Combine(installRoot, "runtime", "resources", "localization", "languages.json");
        Assert.True(File.Exists(runtimeLanguagesPath));
        Assert.Contains("\"defaultLanguage\": \"zhs\"", File.ReadAllText(runtimeLanguagesPath));
    }

    [Fact]
    public void Materialize_WhenEnabledPluginsHaveConflictingPlatformResources_ThrowsAndPreservesPreviousProjection()
    {
        var auditPackageRoot = _testData.CreateValidPackageWithMetadataAndResources("Audit", "1.0.0", "zhs");
        var filesPackageRoot = _testData.CreateValidPackageWithMetadataAndResources("Files", "1.0.0", "en");
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(auditPackageRoot, installRoot, "Audit");

        var stateStore = new InstalledPluginStateStore(installRoot);
        var materializer = new RuntimeViewMaterializer(stateStore);
        materializer.Materialize(installRoot);

        var runtimeLanguagesPath = Path.Combine(installRoot, "runtime", "resources", "localization", "languages.json");
        var previousRuntimeLanguages = File.ReadAllText(runtimeLanguagesPath);

        EnableInstalledPackage(filesPackageRoot, installRoot, "Files");

        var exception = Assert.Throws<InvalidOperationException>(() => materializer.Materialize(installRoot));
        Assert.Contains("conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(previousRuntimeLanguages, File.ReadAllText(runtimeLanguagesPath));
    }

    [Fact]
    public void Materialize_WhenFrontendManifestPathEscapesPackageRoot_ThrowsAndPreservesPreviousProjection()
    {
        var packageRoot = _testData.CreateValidPackage();
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(packageRoot, installRoot, "Audit");

        var stateStore = new InstalledPluginStateStore(installRoot);
        var materializer = new RuntimeViewMaterializer(stateStore);
        materializer.Materialize(installRoot);

        var runtimeManifestPath = Path.Combine(installRoot, "runtime", "frontend-plugin-manifest.json");
        var publicEntryPath = Path.Combine(installRoot, "public", "Audit", "1.0.0", "assets", "entry.js");
        var previousRuntimeManifest = File.ReadAllText(runtimeManifestPath);
        var previousPublicEntry = File.ReadAllText(publicEntryPath);

        var installedFrontendManifestPath = Path.Combine(installRoot, "packages", "Audit", "1.0.0", "frontend", "plugin.frontend.json");
        File.WriteAllText(
            installedFrontendManifestPath,
            JsonSerializer.Serialize(
                new
                {
                    moduleId = "Audit",
                    entryManifest = "../manifest.json",
                    assetRoot = "assets",
                    publicBasePath = "/plugins/Audit/1.0.0"
                },
                new JsonSerializerOptions { WriteIndented = true }));

        var exception = Assert.ThrowsAny<Exception>(() => materializer.Materialize(installRoot));
        Assert.Contains("unsafe", exception.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(previousRuntimeManifest, File.ReadAllText(runtimeManifestPath));
        Assert.Equal(previousPublicEntry, File.ReadAllText(publicEntryPath));
        Assert.True(File.Exists(publicEntryPath));
    }

    [Fact]
    public void Materialize_WhenInstalledFrontendEntryIsMissing_ThrowsAndPreservesPreviousProjection()
    {
        var packageRoot = _testData.CreateValidPackage();
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(packageRoot, installRoot, "Audit");

        var stateStore = new InstalledPluginStateStore(installRoot);
        var materializer = new RuntimeViewMaterializer(stateStore);
        materializer.Materialize(installRoot);

        var runtimeManifestPath = Path.Combine(installRoot, "runtime", "frontend-plugin-manifest.json");
        var publicEntryPath = Path.Combine(installRoot, "public", "Audit", "1.0.0", "assets", "entry.js");
        var previousRuntimeManifest = File.ReadAllText(runtimeManifestPath);
        var previousPublicEntry = File.ReadAllText(publicEntryPath);

        File.Delete(Path.Combine(installRoot, "packages", "Audit", "1.0.0", "frontend", "assets", "entry.js"));

        var exception = Assert.ThrowsAny<Exception>(() => materializer.Materialize(installRoot));
        Assert.Contains("entry", exception.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(previousRuntimeManifest, File.ReadAllText(runtimeManifestPath));
        Assert.Equal(previousPublicEntry, File.ReadAllText(publicEntryPath));
        Assert.True(File.Exists(publicEntryPath));
    }

    [Fact]
    public void Materialize_WhenInstalledStatePackageRootPointsToAnotherInRootPackage_RejectsCorruptState()
    {
        var packageRoot = _testData.CreateValidPackage();
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(packageRoot, installRoot, "Audit");

        var otherPackageRoot = Path.Combine(installRoot, "packages", "OtherModule", "9.9.9");
        Directory.CreateDirectory(otherPackageRoot);

        var statePath = Path.Combine(installRoot, "state", "Audit.json");
        var record = JsonSerializer.Deserialize<InstalledPluginRecord>(File.ReadAllText(statePath))!;
        record.PackageRoot = otherPackageRoot;

        File.WriteAllText(
            statePath,
            JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));

        var stateStore = new InstalledPluginStateStore(installRoot);
        var materializer = new RuntimeViewMaterializer(stateStore);

        var exception = Assert.ThrowsAny<Exception>(() => materializer.Materialize(installRoot));
        Assert.Contains("corrupt", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "runtime")));
        Assert.False(Directory.Exists(Path.Combine(installRoot, "public")));
    }

    [Theory]
    [InlineData("../1.0.0", null)]
    [InlineData("1.0.0", "..\\escaped-package-root")]
    public void Materialize_WhenInstalledStateContainsUnsafeVersionOrPackageRoot_RejectsCorruptState(
        string version,
        string? packageRootOverride)
    {
        var packageRoot = _testData.CreateValidPackage();
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(packageRoot, installRoot, "Audit");

        var statePath = Path.Combine(installRoot, "state", "Audit.json");
        var record = JsonSerializer.Deserialize<InstalledPluginRecord>(File.ReadAllText(statePath))!;
        record.Version = version;
        if (!string.IsNullOrWhiteSpace(packageRootOverride))
        {
            record.PackageRoot = packageRootOverride;
        }

        File.WriteAllText(
            statePath,
            JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));

        var stateStore = new InstalledPluginStateStore(installRoot);
        var materializer = new RuntimeViewMaterializer(stateStore);

        var exception = Assert.ThrowsAny<Exception>(() => materializer.Materialize(installRoot));
        Assert.Contains("corrupt", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "runtime")));
        Assert.False(Directory.Exists(Path.Combine(installRoot, "public")));
    }

    [Fact]
    public void Materialize_WhenPublicSwapFailsAfterRuntimeSwap_RestoresPreviousRuntimeAndPublicProjection()
    {
        var packageRoot = _testData.CreateValidPackage();
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(packageRoot, installRoot, "Audit");

        var stateStore = new InstalledPluginStateStore(installRoot);
        var baselineMaterializer = new RuntimeViewMaterializer(stateStore);
        baselineMaterializer.Materialize(installRoot);

        var runtimeManifestPath = Path.Combine(installRoot, "runtime", "frontend-plugin-manifest.json");
        var publicEntryPath = Path.Combine(installRoot, "public", "Audit", "1.0.0", "assets", "entry.js");
        var previousRuntimeManifest = File.ReadAllText(runtimeManifestPath);
        var previousPublicEntry = File.ReadAllText(publicEntryPath);

        var installedFrontendManifestPath = Path.Combine(installRoot, "packages", "Audit", "1.0.0", "frontend", "plugin.frontend.json");
        File.WriteAllText(
            installedFrontendManifestPath,
            JsonSerializer.Serialize(
                new
                {
                    moduleId = "Audit",
                    entryManifest = "manifest.json",
                    assetRoot = "assets",
                    publicBasePath = "/plugins/Audit/1.0.0"
                },
                new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(installRoot, "packages", "Audit", "1.0.0", "frontend", "assets", "entry.js"), "updated-entry");

        var materializer = CreateMaterializerWithSwapFailure(stateStore, 4, "Simulated public swap failure");

        var exception = Assert.Throws<IOException>(() => materializer.Materialize(installRoot));
        Assert.Contains("public swap failure", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(previousRuntimeManifest, File.ReadAllText(runtimeManifestPath));
        Assert.Equal(previousPublicEntry, File.ReadAllText(publicEntryPath));
    }

    [Fact]
    public void Materialize_HonorsCurrentEnabledVersion_WhenDifferentFromLatestInstalledVersion()
    {
        var versionOnePackageRoot = _testData.CreateValidPackage("Audit", "1.0.0");
        var versionTwoPackageRoot = _testData.CreateValidPackage("Audit", "2.0.0");
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        Assert.Equal("Installed", installer.Install(versionOnePackageRoot, installRoot).Status);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        record!.CurrentEnabledVersion = "1.0.0";
        Assert.True(stateStore.TrySave(record, out var saveError));
        Assert.Null(saveError);

        Assert.Equal("Installed", installer.Install(versionTwoPackageRoot, installRoot).Status);
        File.WriteAllText(Path.Combine(installRoot, "packages", "Audit", "2.0.0", "frontend", "assets", "entry.js"), "version-two-entry");
        File.WriteAllText(Path.Combine(installRoot, "packages", "Audit", "2.0.0", "backend", "plugin.backend.json"), "{\n  \"moduleId\": \"Audit\",\n  \"version\": \"2.0.0\"\n}");

        var materializer = new RuntimeViewMaterializer(stateStore);

        materializer.Materialize(installRoot);

        var backendViewPath = Path.Combine(installRoot, "runtime", "backend-load-view", "Audit", "plugin.json");
        var frontendManifestPath = Path.Combine(installRoot, "runtime", "frontend-plugin-manifest.json");
        var stagedEntryPath = Path.Combine(installRoot, "public", "Audit", "1.0.0", "assets", "entry.js");
        var versionTwoEntryPath = Path.Combine(installRoot, "public", "Audit", "2.0.0", "assets", "entry.js");

        Assert.True(File.Exists(backendViewPath));
        Assert.Equal(
            File.ReadAllText(Path.Combine(installRoot, "packages", "Audit", "1.0.0", "backend", "plugin.backend.json")),
            File.ReadAllText(backendViewPath));

        Assert.True(File.Exists(frontendManifestPath));
        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(frontendManifestPath));
        Assert.Equal("/plugin-assets/Audit/1.0.0/assets/entry.js", manifestDocument.RootElement.GetProperty("plugins")[0].GetProperty("url").GetString());

        Assert.True(File.Exists(stagedEntryPath));
        Assert.False(File.Exists(versionTwoEntryPath));
        Assert.Equal("export default 'audit';\n", File.ReadAllText(stagedEntryPath));
    }

    [Fact]
    public void Materialize_HonorsNonDefaultFrontendPluginBaseUrl()
    {
        var packageRoot = _testData.CreateValidPackage();
        var installRoot = CreateInstallRoot();
        EnableInstalledPackage(packageRoot, installRoot, "Audit");

        var stateStore = new InstalledPluginStateStore(installRoot);
        var customBaseUrl = "/custom-plugin-path";
        var manifestBuilder = new FrontendRuntimeManifestBuilder(customBaseUrl);
        var materializer = new RuntimeViewMaterializer(stateStore, manifestBuilder);

        materializer.Materialize(installRoot);

        var frontendManifestPath = Path.Combine(installRoot, "runtime", "frontend-plugin-manifest.json");
        Assert.True(File.Exists(frontendManifestPath));

        using var manifestDocument = JsonDocument.Parse(File.ReadAllText(frontendManifestPath));
        var plugins = manifestDocument.RootElement.GetProperty("plugins");
        Assert.Single(plugins.EnumerateArray());
        Assert.Equal("/custom-plugin-path/Audit/1.0.0/assets/entry.js", plugins[0].GetProperty("url").GetString());
    }

    private static RuntimeViewMaterializer CreateMaterializerWithSwapFailure(InstalledPluginStateStore stateStore, int failOnCallNumber, string message)
    {
        var constructor = typeof(RuntimeViewMaterializer).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(InstalledPluginStateStore),
                typeof(FrontendRuntimeManifestBuilder),
                typeof(LocalizationRuntimeManifestBuilder),
                typeof(MetadataRuntimeManifestBuilder),
                typeof(Action<string, string>)
            ],
            modifiers: null);

        Assert.NotNull(constructor);

        var callCount = 0;
        return (RuntimeViewMaterializer)constructor!.Invoke(
            [
                stateStore,
                new FrontendRuntimeManifestBuilder(),
                new LocalizationRuntimeManifestBuilder(),
                new MetadataRuntimeManifestBuilder(),
                (Action<string, string>)((source, destination) =>
                {
                    callCount++;
                    if (callCount == failOnCallNumber)
                    {
                        throw new IOException(message);
                    }

                    Directory.Move(source, destination);
                })
            ]);
    }

    private static string WriteModel(string modelsRoot, string modelName)
    {
        var path = Path.Combine(modelsRoot, $"{modelName}.model.json");
        File.WriteAllText(
            path,
            $$"""
            {
              "schemaVersion": "1.0",
              "type": "model",
              "{{modelName}}": {
                "kind": "Entity",
                "fields": []
              }
            }
            """);
        return path;
    }

    private static string WriteModelWithoutField(
        string sourceModelsRoot,
        string targetModelsRoot,
        string modelName,
        string fieldName)
    {
        var sourcePath = Path.Combine(sourceModelsRoot, $"{modelName}.model.json");
        var root = JsonNode.Parse(File.ReadAllBytes(sourcePath))!.AsObject();
        var fields = root[modelName]!["fields"]!.AsArray();
        var field = fields
            .Select(static node => node!.AsObject())
            .SingleOrDefault(candidate => candidate["name"]!.GetValue<string>() == fieldName);
        if (field is not null)
        {
            fields.Remove(field);
        }
        var targetPath = Path.Combine(targetModelsRoot, $"{modelName}.model.json");
        File.WriteAllText(
            targetPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        return targetPath;
    }

    private static string CopyModel(string sourceModelsRoot, string targetModelsRoot, string modelName)
    {
        var targetPath = Path.Combine(targetModelsRoot, $"{modelName}.model.json");
        File.Copy(Path.Combine(sourceModelsRoot, $"{modelName}.model.json"), targetPath);
        return targetPath;
    }

    private static bool HasField(string path, string modelName, string fieldName)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        return document.RootElement
            .GetProperty(modelName)
            .GetProperty("fields")
            .EnumerateArray()
            .Any(candidate => candidate.GetProperty("name").GetString() == fieldName);
    }

    private static string RuntimeModelPath(string installRoot, string modelName) =>
        Path.Combine(
            installRoot,
            "runtime",
            "modules",
            "Organization",
            "metadata",
            "models",
            $"{modelName}.model.json");

    private static string? ReadMode(
        string path,
        string modelName,
        string fieldName,
        string scenario)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var field = document.RootElement
            .GetProperty(modelName)
            .GetProperty("fields")
            .EnumerateArray()
            .Single(candidate => candidate.GetProperty("name").GetString() == fieldName);
        return field.TryGetProperty("form", out var form)
            && form.TryGetProperty(scenario, out var rule)
            && rule.TryGetProperty("mode", out var mode)
            ? mode.GetString()
            : null;
    }

    private string CreatePackageWithLocalizationMetadata(bool includeEnglishTranslation = true)
    {
        var packageRoot = _testData.CreateValidPackage();
        AddLocalizationMetadata(packageRoot, "Audit", includeEnglishTranslation);
        return packageRoot;
    }

    private string CreatePlatformPackageWithLanguages()
    {
        var packageRoot = _testData.CreateValidPackageWithMetadataAndResources("Platform", "1.0.0", "zhs");
        var modulesRoot = Path.Combine(Path.Combine(packageRoot, "modules", "Platform"), "metadata");
        Directory.CreateDirectory(Path.Combine(modulesRoot, "dictionaries"));
        File.WriteAllText(
            Path.Combine(modulesRoot, "languages.json"),
            JsonSerializer.Serialize(
                new
                {
                    defaultLanguage = "zhs",
                    supportedLanguages = new[]
                    {
                        new { code = "zhs", displayName = "简体中文", locale = "zh-CN" },
                        new { code = "en", displayName = "English", locale = "en-US" }
                    }
                },
                new JsonSerializerOptions { WriteIndented = true }));
        return packageRoot;
    }

    private static void AddLocalizationMetadata(string packageRoot, string moduleId, bool includeEnglishTranslation)
    {
        var dictionariesRoot = Path.Combine(Path.Combine(packageRoot, "modules", moduleId), "metadata", "dictionaries");
        Directory.CreateDirectory(dictionariesRoot);

        File.WriteAllText(
            Path.Combine(dictionariesRoot, "Common.dic.json"),
            JsonSerializer.Serialize(
                includeEnglishTranslation
                    ? new Dictionary<string, object?>
                    {
                        ["schemaVersion"] = "1.0",
                        ["type"] = "dic",
                        ["Common"] = new Dictionary<string, object?>
                        {
                            ["Add"] = new Dictionary<string, string>
                            {
                                ["zhs"] = "添加",
                                ["en"] = "Add"
                            },
                            ["Upload"] = new Dictionary<string, string>
                            {
                                ["zhs"] = "上传",
                                ["en"] = "Upload"
                            }
                        }
                    }
                    : new Dictionary<string, object?>
                    {
                        ["schemaVersion"] = "1.0",
                        ["type"] = "dic",
                        ["Common"] = new Dictionary<string, object?>
                        {
                            ["Add"] = new Dictionary<string, string>
                            {
                                ["zhs"] = "添加"
                            }
                        }
                    },
                new JsonSerializerOptions { WriteIndented = true }));

        File.WriteAllText(
            Path.Combine(dictionariesRoot, "Pages.dic.json"),
            JsonSerializer.Serialize(
                new Dictionary<string, object?>
                {
                    ["schemaVersion"] = "1.0",
                    ["type"] = "dic",
                    ["Pages"] = new Dictionary<string, object?>
                    {
                        ["ListTitle"] = new Dictionary<string, string>
                        {
                            ["zhs"] = "页面标题",
                            ["en"] = "List title"
                        }
                    }
                },
                new JsonSerializerOptions { WriteIndented = true }));

        File.WriteAllText(
            Path.Combine(dictionariesRoot, "ValueLists.dic.json"),
            File.ReadAllText(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(FindRepositoryRoot(), "ConfigCenter"), "metadata", "dictionaries", "ValueLists.dic.json")));
    }

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

    public void Dispose()
    {
        _testData.Dispose();

        foreach (var installRoot in _installRoots)
        {
            if (Directory.Exists(installRoot))
            {
                Directory.Delete(installRoot, recursive: true);
            }
        }
    }

    private string CreateInstallRoot()
    {
        var installRoot = Path.Combine(Path.GetTempPath(), $"plugin_runtime_{Guid.NewGuid():N}");
        Directory.CreateDirectory(installRoot);
        _installRoots.Add(installRoot);
        return installRoot;
    }

    private void EnableInstalledPackage(string packageRoot, string installRoot, string moduleId)
    {
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var installResult = installer.Install(packageRoot, installRoot);
        Assert.Equal("Installed", installResult.Status);
        Assert.True(stateStore.TryLoad(moduleId, out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        record!.CurrentEnabledVersion = record.Version;
        Assert.True(stateStore.TrySave(record, out var saveError));
        Assert.Null(saveError);
    }
}
