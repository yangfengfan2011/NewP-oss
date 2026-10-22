using ICP.Platform.Kernel.PluginPackages;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class UnifiedPluginPackageInstallerTests : IDisposable
{
    private readonly UnifiedPluginPackageTestData _testData;
    private readonly List<string> _installRoots = new();

    public UnifiedPluginPackageInstallerTests()
    {
        _testData = new UnifiedPluginPackageTestData();
    }

    [Fact]
    public void InstallPackage_WritesVersionedInstallRecord_AndCopiesArtifacts()
    {
        var packageRoot = _testData.CreateValidPackage();
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        var result = installer.Install(packageRoot, installRoot);

        Assert.Equal("Installed", result.Status);
        Assert.Equal("Audit", result.ModuleId);
        Assert.Equal("1.0.0", result.Version);
        Assert.True(File.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0", "plugin.json")));
        Assert.True(File.Exists(Path.Combine(installRoot, "state", "Audit.json")));
    }

    [Fact]
    public void InstallPackage_RelativeInstallRoot_WritesAbsolutePackageRootToState()
    {
        var packageRoot = _testData.CreateValidPackage();
        var installRoot = CreateInstallRoot();
        var relativeInstallRoot = Path.GetRelativePath(Environment.CurrentDirectory, installRoot);
        var stateStore = new InstalledPluginStateStore(relativeInstallRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        var result = installer.Install(packageRoot, relativeInstallRoot);

        Assert.Equal("Installed", result.Status);
        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Equal(Path.Combine(installRoot, "packages", "Audit", "1.0.0"), record!.PackageRoot);
    }

    [Theory]
    [InlineData("..", "1.0.0")]
    [InlineData("Audit", "../1.0.0")]
    [InlineData("Audit/../escape", "1.0.0")]
    [InlineData("Audit", "1.0.0/../../escape")]
    public void InstallPackage_RejectsUnsafeManifestPathSegments(string moduleId, string version)
    {
        var packageRoot = _testData.CreateValidPackage(moduleId, version);
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        var result = installer.Install(packageRoot, installRoot);

        var packagesRoot = Path.Combine(installRoot, "packages");

        Assert.Equal("Failed", result.Status);
        Assert.False(File.Exists(Path.Combine(packagesRoot, "Audit", "1.0.0", "plugin.json")));
        Assert.False(Directory.Exists(packagesRoot) && Directory.EnumerateFileSystemEntries(packagesRoot, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public void InstallPackage_RejectsMalformedChildManifest_BeforeWritingInstallOutput()
    {
        var packageRoot = _testData.CreatePackageWithInvalidBackendManifest();
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        var result = installer.Install(packageRoot, installRoot);

        Assert.Equal("Failed", result.Status);
        Assert.Contains("plugin.backend.json", result.ErrorMessage);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0")));
        Assert.False(File.Exists(Path.Combine(installRoot, "state", "Audit.json")));
    }

    [Fact]
    public void InstallPackage_RejectsMissingFrontendReferencedFile_BeforeWritingInstallOutput()
    {
        var packageRoot = _testData.CreatePackageMissingFrontendEntryAsset();
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        var result = installer.Install(packageRoot, installRoot);

        Assert.Equal("Failed", result.Status);
        Assert.Contains("assets/entry.js", result.ErrorMessage);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0")));
        Assert.False(File.Exists(Path.Combine(installRoot, "state", "Audit.json")));
    }

    [Fact]
    public void InstallPackage_RejectsUnsafeFrontendChildManifestPath_BeforeWritingInstallOutput()
    {
        var packageRoot = _testData.CreatePackageWithUnsafeFrontendEntryManifestPath();
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        var result = installer.Install(packageRoot, installRoot);

        Assert.Equal("Failed", result.Status);
        Assert.Contains("unsafe", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0")));
        Assert.False(File.Exists(Path.Combine(installRoot, "state", "Audit.json")));
    }

    [Fact]
    public void InstallPackage_ReinstallingSameVersion_ReplacesExistingDirectoryContents()
    {
        var packageRoot = _testData.CreateValidPackage();
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        var firstResult = installer.Install(packageRoot, installRoot);
        Assert.Equal("Installed", firstResult.Status);

        var installedVersionRoot = Path.Combine(installRoot, "packages", "Audit", "1.0.0");
        var staleFile = Path.Combine(installedVersionRoot, "stale.txt");
        File.WriteAllText(staleFile, "stale");
        Assert.True(File.Exists(staleFile));

        var secondResult = installer.Install(packageRoot, installRoot);

        Assert.Equal("Installed", secondResult.Status);
        Assert.False(File.Exists(staleFile));
        Assert.True(File.Exists(Path.Combine(installedVersionRoot, "plugin.json")));
    }

    [Fact]
    public void InstallPackage_CorruptExistingState_ReturnsFailed_AndDoesNotLeaveInstallOutput()
    {
        var packageRoot = _testData.CreateValidPackage();
        var installRoot = CreateInstallRoot();
        Directory.CreateDirectory(Path.Combine(installRoot, "state"));
        File.WriteAllText(Path.Combine(installRoot, "state", "Audit.json"), "{ invalid json");
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        var result = installer.Install(packageRoot, installRoot);

        Assert.Equal("Failed", result.Status);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0")));
        Assert.Equal("{ invalid json", File.ReadAllText(Path.Combine(installRoot, "state", "Audit.json")));
    }

    [Fact]
    public void InstallPackage_SaveFailure_ReturnsFailed_AndCleansUpPartialInstall()
    {
        var packageRoot = _testData.CreateValidPackage();
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        Directory.CreateDirectory(Path.Combine(installRoot, "state", "Audit.json"));

        var result = installer.Install(packageRoot, installRoot);

        Assert.Equal("Failed", result.Status);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0")));
    }

    [Fact]
    public void InstallPackage_CopyFailure_ReturnsFailed_AndCleansUpPartialInstall()
    {
        var packageRoot = _testData.CreateValidPackage();
        var lockedFilePath = Path.Combine(packageRoot, "frontend", "assets", "entry.js");
        using var lockedFile = new FileStream(
            lockedFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var destinationRoot = Path.Combine(installRoot, "packages", "Audit", "1.0.0");

        var result = installer.Install(packageRoot, installRoot);

        Assert.Equal("Failed", result.Status);
        Assert.Contains("Failed to install plugin package", result.ErrorMessage);
        Assert.False(Directory.Exists(destinationRoot));
        Assert.False(File.Exists(Path.Combine(installRoot, "state", "Audit.json")));
    }

    [Fact]
    public void InstallPackage_ReinstallCopyFailure_WithRestoreFailure_ReturnsFailedWithoutThrowing()
    {
        var originalPackageRoot = _testData.CreateValidPackage();
        var replacementPackageRoot = _testData.CreateValidPackage();
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = CreateInstallerWithRestoreFailure(stateStore, "Simulated restore failure");

        var firstResult = installer.Install(originalPackageRoot, installRoot);
        Assert.Equal("Installed", firstResult.Status);

        var installedPluginJsonPath = Path.Combine(installRoot, "packages", "Audit", "1.0.0", "plugin.json");
        var originalPluginJson = File.ReadAllText(installedPluginJsonPath, Encoding.UTF8);

        var replacementPluginJson = Path.Combine(replacementPackageRoot, "plugin.json");
        var replacementManifest = JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(replacementPluginJson, Encoding.UTF8))!;
        replacementManifest["Description"] = "replacement package payload";
        File.WriteAllText(
            replacementPluginJson,
            JsonSerializer.Serialize(replacementManifest, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);

        using var lockedFile = new FileStream(
            Path.Combine(replacementPackageRoot, "frontend", "assets", "entry.js"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var secondResult = installer.Install(replacementPackageRoot, installRoot);

        Assert.Equal("Failed", secondResult.Status);
        Assert.Contains("Failed to install plugin package", secondResult.ErrorMessage);
        Assert.Contains("restore failed", secondResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0")));
        Assert.True(File.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0.backup", "plugin.json")));
        Assert.Equal(originalPluginJson, File.ReadAllText(Path.Combine(installRoot, "packages", "Audit", "1.0.0.backup", "plugin.json"), Encoding.UTF8));

        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Equal("1.0.0", record!.Version);
        Assert.Equal("Installed", record.Status);
        Assert.Single(record.InstalledVersions);
        Assert.Equal("1.0.0", record.InstalledVersions[0]);
    }

    [Fact]
    public void StateStore_TryLoadLiteralNull_ReturnsFailed()
    {
        var installRoot = CreateInstallRoot();
        Directory.CreateDirectory(Path.Combine(installRoot, "state"));
        File.WriteAllText(Path.Combine(installRoot, "state", "Audit.json"), "null");
        var stateStore = new InstalledPluginStateStore(installRoot);

        var success = stateStore.TryLoad("Audit", out var record, out var errorMessage);

        Assert.False(success);
        Assert.Null(record);
        Assert.Contains("corrupt", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StateStore_TryLoadNullInstalledVersions_ReturnsFailed()
    {
        var installRoot = CreateInstallRoot();
        Directory.CreateDirectory(Path.Combine(installRoot, "state"));
        var validPackageRoot = Path.Combine(installRoot, "packages", "Audit", "1.0.0");
        File.WriteAllText(
            Path.Combine(installRoot, "state", "Audit.json"),
            $$"""
            {
              "ModuleId": "Audit",
              "Version": "1.0.0",
              "Status": "Installed",
              "PackageRoot": "{{validPackageRoot.Replace("\\", "\\\\")}}",
              "InstalledVersions": null
            }
            """);
        var stateStore = new InstalledPluginStateStore(installRoot);

        var success = stateStore.TryLoad("Audit", out var record, out var errorMessage);

        Assert.False(success);
        Assert.Null(record);
        Assert.Contains("InstalledVersions", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StateStore_TrySaveReplaceFailure_PreservesExistingStateFile()
    {
        var installRoot = CreateInstallRoot();
        Directory.CreateDirectory(Path.Combine(installRoot, "state"));
        var statePath = Path.Combine(installRoot, "state", "Audit.json");
        var originalJson = """
            {
              "ModuleId": "Audit",
              "Version": "0.9.0",
              "Status": "Installed",
              "PackageRoot": "C:\\plugins\\Audit\\0.9.0",
              "InstalledVersions": ["0.9.0"]
            }
            """;
        File.WriteAllText(statePath, originalJson);

        var stateStore = CreateStateStoreWithReplaceFailure(installRoot, "Simulated replace failure");

        var success = stateStore.TrySave(
            new InstalledPluginRecord
            {
                ModuleId = "Audit",
                Version = "1.0.0",
                Status = "Installed",
                PackageRoot = Path.Combine(installRoot, "packages", "Audit", "1.0.0"),
                InstalledVersions = ["0.9.0", "1.0.0"]
            },
            out var errorMessage);

        Assert.False(success);
        Assert.Contains("replace failure", errorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(originalJson, File.ReadAllText(statePath));
        Assert.Empty(Directory.GetFiles(Path.Combine(installRoot, "state"), "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void InstallPackage_PreservesCurrentEnabledVersion_WhenInstallingNewerVersion()
    {
        var versionOnePackageRoot = _testData.CreateValidPackage("Audit", "1.0.0");
        var versionTwoPackageRoot = _testData.CreateValidPackage("Audit", "2.0.0");
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        Assert.Equal("Installed", installer.Install(versionOnePackageRoot, installRoot).Status);
        Assert.True(stateStore.TryLoad("Audit", out var existingRecord, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(existingRecord);
        existingRecord!.CurrentEnabledVersion = "1.0.0";
        Assert.True(stateStore.TrySave(existingRecord, out var saveError));
        Assert.Null(saveError);

        var result = installer.Install(versionTwoPackageRoot, installRoot);

        Assert.Equal("Installed", result.Status);
        Assert.True(stateStore.TryLoad("Audit", out var record, out loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Equal("2.0.0", record!.Version);
        Assert.Equal("1.0.0", record.CurrentEnabledVersion);
        Assert.Equal(["1.0.0", "2.0.0"], record.InstalledVersions);
        Assert.Equal(Path.Combine(installRoot, "packages", "Audit", "2.0.0"), record.PackageRoot);
    }

    [Fact]
    public void InstallPackage_PreservesDisabledFlag_WhenInstallingNewerVersion()
    {
        var versionOnePackageRoot = _testData.CreateValidPackage("Audit", "1.0.0");
        var versionTwoPackageRoot = _testData.CreateValidPackage("Audit", "2.0.0");
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        Assert.Equal("Installed", installer.Install(versionOnePackageRoot, installRoot).Status);
        Assert.True(stateStore.TryLoad("Audit", out var existingRecord, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(existingRecord);
        existingRecord!.IsDisabled = true;
        Assert.True(stateStore.TrySave(existingRecord, out var saveError));
        Assert.Null(saveError);

        var result = installer.Install(versionTwoPackageRoot, installRoot);

        Assert.Equal("Installed", result.Status);
        Assert.True(stateStore.TryLoad("Audit", out var record, out loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.True(record!.IsDisabled);
        Assert.Null(record.CurrentEnabledVersion);
    }

    [Fact]
    public void InstallPackage_WhenThirdVersionIsEnabled_PrunesOldestVersionDirectory_AndKeepsRollbackVersions()
    {
        var versionOnePackageRoot = _testData.CreateValidPackage("Audit", "1.0.0");
        var versionTwoPackageRoot = _testData.CreateValidPackage("Audit", "2.0.0");
        var versionThreePackageRoot = _testData.CreateValidPackage("Audit", "3.0.0");
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var operations = new UnifiedPluginPackageOperations(stateStore);

        Assert.Equal("Installed", installer.Install(versionOnePackageRoot, installRoot).Status);
        Assert.Equal("Installed", installer.Install(versionTwoPackageRoot, installRoot).Status);
        Assert.Equal("Installed", installer.Install(versionThreePackageRoot, installRoot).Status);
        Assert.Equal("Enabled", operations.Enable("Audit", "3.0.0").Status);

        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Equal("3.0.0", record!.Version);
        Assert.Equal("3.0.0", record.CurrentEnabledVersion);
        Assert.Equal(Path.Combine(installRoot, "packages", "Audit", "3.0.0"), record.PackageRoot);
        Assert.Equal(["2.0.0", "3.0.0"], record.InstalledVersions);

        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "2.0.0")));
        Assert.True(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "3.0.0")));
    }

    [Fact]
    public void Install_WithMissingDependency_ReturnsInstalledStatus_AndWarningMessage()
    {
        var packageRoot = CreatePackageWithDependencies("Feature", "1.0.0", "MissingPlugin");
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        var result = installer.Install(packageRoot, installRoot);

        Assert.Equal("Installed", result.Status);
        Assert.Contains("MissingPlugin", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallPackage_InstallingThirdVersion_PrunesOldestInactiveVersion_AndUpdatesRecord()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "1.0.0"), installRoot).Status);
        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "2.0.0"), installRoot).Status);
        Assert.Equal("Installed", installer.Install(_testData.CreateValidPackage("Audit", "3.0.0"), installRoot).Status);

        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0")));
        Assert.True(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "2.0.0")));
        Assert.True(Directory.Exists(Path.Combine(installRoot, "packages", "Audit", "3.0.0")));

        Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        Assert.Equal("3.0.0", record!.Version);
        Assert.Equal(["2.0.0", "3.0.0"], record.InstalledVersions);
        Assert.Equal(Path.Combine(installRoot, "packages", "Audit", "3.0.0"), record.PackageRoot);
    }

    [Fact]
    public void StateStore_LoadAllEnabled_UsesCurrentEnabledVersionPackageRoot_WhenDifferentFromLatestInstalledVersion()
    {
        var installRoot = CreateInstallRoot();
        var versionOnePackageRoot = Path.Combine(installRoot, "packages", "Audit", "1.0.0");
        Directory.CreateDirectory(versionOnePackageRoot);
        var versionTwoPackageRoot = Path.Combine(installRoot, "packages", "Audit", "2.0.0");
        Directory.CreateDirectory(versionTwoPackageRoot);

        var stateStore = new InstalledPluginStateStore(installRoot);
        Assert.True(stateStore.TrySave(
            new InstalledPluginRecord
            {
                ModuleId = "Audit",
                Version = "2.0.0",
                Status = "Installed",
                PackageRoot = versionTwoPackageRoot,
                CurrentEnabledVersion = "1.0.0",
                InstalledVersions = ["1.0.0", "2.0.0"]
            },
            out var saveError));
        Assert.Null(saveError);

        var enabledRecords = stateStore.LoadAllEnabled();

        var record = Assert.Single(enabledRecords);
        Assert.Equal("Audit", record.ModuleId);
        Assert.Equal("1.0.0", record.Version);
        Assert.Equal("1.0.0", record.CurrentEnabledVersion);
        Assert.Equal(versionOnePackageRoot, record.PackageRoot);
        Assert.Equal(["1.0.0", "2.0.0"], record.InstalledVersions);
    }

    [Fact]
    public void StateStore_TryLoadEnabledDisabledConflict_ReturnsFailed()
    {
        var installRoot = CreateInstallRoot();
        Directory.CreateDirectory(Path.Combine(installRoot, "state"));
        var packageRoot = Path.Combine(installRoot, "packages", "Audit", "1.0.0");
        Directory.CreateDirectory(packageRoot);
        File.WriteAllText(
            Path.Combine(installRoot, "state", "Audit.json"),
            $$"""
            {
              "ModuleId": "Audit",
              "Version": "1.0.0",
              "Status": "Installed",
              "PackageRoot": "{{packageRoot.Replace("\\", "\\\\")}}",
              "CurrentEnabledVersion": "1.0.0",
              "InstalledVersions": ["1.0.0"],
              "IsDisabled": true
            }
            """);
        var stateStore = new InstalledPluginStateStore(installRoot);

        var success = stateStore.TryLoad("Audit", out var record, out var errorMessage);

        Assert.False(success);
        Assert.Null(record);
        Assert.Contains("IsDisabled", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StateStore_TryLoadCurrentEnabledVersionNotInInstalledVersions_ReturnsFailed()
    {
        var installRoot = CreateInstallRoot();
        Directory.CreateDirectory(Path.Combine(installRoot, "state"));
        var versionTwoPackageRoot = Path.Combine(installRoot, "packages", "Audit", "2.0.0");
        Directory.CreateDirectory(versionTwoPackageRoot);
        File.WriteAllText(
            Path.Combine(installRoot, "state", "Audit.json"),
            $$"""
            {
              "ModuleId": "Audit",
              "Version": "2.0.0",
              "Status": "Installed",
              "PackageRoot": "{{versionTwoPackageRoot.Replace("\\", "\\\\")}}",
              "CurrentEnabledVersion": "1.0.0",
              "InstalledVersions": ["2.0.0"]
            }
            """);
        var stateStore = new InstalledPluginStateStore(installRoot);

        var success = stateStore.TryLoad("Audit", out var record, out var errorMessage);

        Assert.False(success);
        Assert.Null(record);
        Assert.Contains("CurrentEnabledVersion", errorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InstalledVersions", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StateStore_LoadAllEnabled_Throws_WhenCurrentEnabledVersionIsNotInstalled()
    {
        var installRoot = CreateInstallRoot();
        Directory.CreateDirectory(Path.Combine(installRoot, "state"));
        var versionTwoPackageRoot = Path.Combine(installRoot, "packages", "Audit", "2.0.0");
        Directory.CreateDirectory(versionTwoPackageRoot);
        File.WriteAllText(
            Path.Combine(installRoot, "state", "Audit.json"),
            $$"""
            {
              "ModuleId": "Audit",
              "Version": "2.0.0",
              "Status": "Installed",
              "PackageRoot": "{{versionTwoPackageRoot.Replace("\\", "\\\\")}}",
              "CurrentEnabledVersion": "1.0.0",
              "InstalledVersions": ["2.0.0"]
            }
            """);
        var stateStore = new InstalledPluginStateStore(installRoot);

        var exception = Assert.Throws<IOException>(() => stateStore.LoadAllEnabled());

        Assert.Contains("CurrentEnabledVersion", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StateStore_TryLoadRootedIllegalPackageRoot_ReturnsFailed()
    {
        var installRoot = CreateInstallRoot();
        Directory.CreateDirectory(Path.Combine(installRoot, "state"));
        var invalidPackageRoot = $"{Path.GetPathRoot(installRoot)}\u0000bad";
        File.WriteAllText(
            Path.Combine(installRoot, "state", "Audit.json"),
            $$"""
            {
              "ModuleId": "Audit",
              "Version": "1.0.0",
              "Status": "Installed",
              "PackageRoot": "{{JsonSerializer.Serialize(invalidPackageRoot).Trim('"')}}",
              "InstalledVersions": ["1.0.0"]
            }
            """);
        var stateStore = new InstalledPluginStateStore(installRoot);

        var success = stateStore.TryLoad("Audit", out var record, out var errorMessage);

        Assert.False(success);
        Assert.Null(record);
        Assert.Contains("PackageRoot", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StateStore_LoadAllEnabled_SkipsRecord_WhenCurrentEnabledVersionIsEmpty()
    {
        var installRoot = CreateInstallRoot();
        var versionTwoPackageRoot = Path.Combine(installRoot, "packages", "Audit", "2.0.0");
        Directory.CreateDirectory(versionTwoPackageRoot);

        var stateStore = new InstalledPluginStateStore(installRoot);
        Assert.True(stateStore.TrySave(
            new InstalledPluginRecord
            {
                ModuleId = "Audit",
                Version = "2.0.0",
                Status = "Installed",
                PackageRoot = versionTwoPackageRoot,
                CurrentEnabledVersion = string.Empty,
                InstalledVersions = ["2.0.0"]
            },
            out var saveError));
        Assert.Null(saveError);

        var enabledRecords = stateStore.LoadAllEnabled();

        Assert.Empty(enabledRecords);
    }

    [Fact]
    public void StateStore_TryLoadUnsafeModuleId_ReturnsFailed()
    {
        var installRoot = CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);

        var success = stateStore.TryLoad("..\\escape", out var record, out var errorMessage);

        Assert.False(success);
        Assert.Null(record);
        Assert.Contains("unsafe", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static UnifiedPluginPackageInstaller CreateInstallerWithRestoreFailure(InstalledPluginStateStore stateStore, string message)
    {
        var constructor = typeof(UnifiedPluginPackageInstaller).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(InstalledPluginStateStore), typeof(UnifiedPluginPackageScanner), typeof(Action<string, string>)],
            modifiers: null);

        Assert.NotNull(constructor);

        return (UnifiedPluginPackageInstaller)constructor!.Invoke(
            [
                stateStore,
                new UnifiedPluginPackageScanner(),
                (Action<string, string>)((_, _) => throw new IOException(message))
            ]);
    }

    private static InstalledPluginStateStore CreateStateStoreWithReplaceFailure(string installRoot, string message)
    {
        var constructor = typeof(InstalledPluginStateStore).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(string), typeof(Action<string, string, string?>), typeof(Action<string, string>)],
            modifiers: null);

        Assert.NotNull(constructor);

        return (InstalledPluginStateStore)constructor!.Invoke(
            [
                installRoot,
                (Action<string, string, string?>)((_, _, _) => throw new IOException(message)),
                (Action<string, string>)((source, destination) => File.Move(source, destination))
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
        var installRoot = Path.Combine(Path.GetTempPath(), $"plugin_install_{Guid.NewGuid():N}");
        Directory.CreateDirectory(installRoot);
        _installRoots.Add(installRoot);
        return installRoot;
    }

    private string CreatePackageWithDependencies(string moduleId, string version, params string[] dependencies)
    {
        var packageRoot = _testData.CreateValidPackage(moduleId, version);
        var pluginJsonPath = Path.Combine(packageRoot, "backend", "plugin.backend.json");
        var manifest = JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(pluginJsonPath, Encoding.UTF8))!;
        manifest["Dependencies"] = dependencies;
        File.WriteAllText(
            pluginJsonPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
        return packageRoot;
    }
}
