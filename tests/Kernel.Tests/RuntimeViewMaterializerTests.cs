using ICP.Platform.Kernel.PluginDiscovery;
using ICP.Platform.Kernel.PluginPackages;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class RuntimeViewMaterializerTests : IDisposable
{
    private readonly UnifiedPluginPackageTestData _testData;
    private readonly List<string> _installRoots = new();

    public RuntimeViewMaterializerTests()
    {
        _testData = new UnifiedPluginPackageTestData();
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
            [typeof(InstalledPluginStateStore), typeof(FrontendRuntimeManifestBuilder), typeof(Action<string, string>)],
            modifiers: null);

        Assert.NotNull(constructor);

        var callCount = 0;
        return (RuntimeViewMaterializer)constructor!.Invoke(
            [
                stateStore,
                new FrontendRuntimeManifestBuilder(),
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
