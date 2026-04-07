using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class UnifiedPluginPackageScannerTests : IDisposable
{
    private readonly UnifiedPluginPackageTestData _testData;

    public UnifiedPluginPackageScannerTests()
    {
        _testData = new UnifiedPluginPackageTestData();
    }

    public void Dispose()
    {
        _testData.Dispose();
    }

    [Fact]
    public void ValidPackage_ReturnsSuccess_WithPreservedManifestAndResolvedPaths()
    {
        // Arrange
        var packagePath = _testData.CreateValidPackage();
        var scanner = new UnifiedPluginPackageScanner();

        // Act
        var result = scanner.ScanPackage(packagePath);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("Audit", result.ModuleId);
        Assert.Equal("backend/plugin.backend.json", result.BackendArtifactPath);
        Assert.Equal("frontend/plugin.frontend.json", result.FrontendArtifactPath);

        Assert.NotNull(result.Manifest);
        Assert.Equal("icp.module.audit", result.Manifest.PluginId);
        Assert.Equal("Audit", result.Manifest.ModuleId);
        Assert.Equal("Audit", result.Manifest.DisplayName);
        Assert.Equal("1.0.0", result.Manifest.Version);
        Assert.Equal("Unified plugin package example", result.Manifest.Description);
        Assert.NotNull(result.Manifest.Distribution);
        Assert.Equal("internal", result.Manifest.Distribution.Channel);
        Assert.Equal("ICP Platform Team", result.Manifest.Distribution.Publisher);
        Assert.NotNull(result.Manifest.HostVersion);
        Assert.Equal("1.0.0", result.Manifest.HostVersion.Min);
        Assert.Equal("2.0.0", result.Manifest.HostVersion.Max);
        Assert.NotNull(result.Manifest.Artifacts);
        Assert.Equal("backend/plugin.backend.json", result.Manifest.Artifacts.Backend);
        Assert.Equal("frontend/plugin.frontend.json", result.Manifest.Artifacts.Frontend);
        Assert.NotNull(result.Manifest.Checksums);
        Assert.Equal("checksums/sha256sums.json", result.Manifest.Checksums.Sha256Manifest);

        Assert.Equal(packagePath, result.PackageRoot);
        Assert.Equal(Path.Combine(packagePath, "backend", "plugin.backend.json"), result.ResolvedBackendManifestPath);
        Assert.Equal(Path.Combine(packagePath, "frontend", "plugin.frontend.json"), result.ResolvedFrontendManifestPath);
    }

    [Fact]
    public void PackageMissingFrontendArtifact_ReturnsPackageArtifactFailure_WithFrontendPathInMessage()
    {
        // Arrange
        var packagePath = _testData.CreatePackageMissingFrontendManifest();
        var scanner = new UnifiedPluginPackageScanner();

        // Act
        var result = scanner.ScanPackage(packagePath);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(UnifiedPluginPackageScanFailureType.ArtifactNotFound, result.FailureType);
        Assert.Contains("frontend/plugin.frontend.json", result.ErrorMessage);
    }

    [Theory]
    [InlineData(true, UnifiedPluginPackageScanFailureType.ManifestParseError)]
    [InlineData(false, UnifiedPluginPackageScanFailureType.ManifestNotFound)]
    public void MissingOrInvalidPluginJson_ReturnsRootManifestFailure(
        bool invalidJson,
        UnifiedPluginPackageScanFailureType expectedFailureType)
    {
        // Arrange
        var packagePath = invalidJson
            ? _testData.CreatePackageWithInvalidPluginJson()
            : _testData.CreatePackageMissingPluginJson();
        var scanner = new UnifiedPluginPackageScanner();

        // Act
        var result = scanner.ScanPackage(packagePath);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(expectedFailureType, result.FailureType);
        Assert.Contains("plugin.json", result.ErrorMessage);
    }

    [Fact]
    public void PackageWithInvalidBackendManifest_ReturnsManifestParseFailure()
    {
        var packagePath = _testData.CreatePackageWithInvalidBackendManifest();
        var scanner = new UnifiedPluginPackageScanner();

        var result = scanner.ScanPackage(packagePath);

        Assert.False(result.IsSuccess);
        Assert.Equal(UnifiedPluginPackageScanFailureType.ManifestParseError, result.FailureType);
        Assert.Contains("plugin.backend.json", result.ErrorMessage);
    }

    [Fact]
    public void PackageWithMissingBackendEntryAssembly_ReturnsArtifactFailure()
    {
        var packagePath = _testData.CreatePackageMissingBackendEntryAssembly();
        var scanner = new UnifiedPluginPackageScanner();

        var result = scanner.ScanPackage(packagePath);

        Assert.False(result.IsSuccess);
        Assert.Equal(UnifiedPluginPackageScanFailureType.ArtifactNotFound, result.FailureType);
        Assert.Contains("AuditModule.dll", result.ErrorMessage);
    }

    [Fact]
    public void PackageWithUnsafeBackendEntryAssemblyPath_ReturnsArtifactFailure()
    {
        var packagePath = _testData.CreatePackageWithUnsafeBackendEntryAssemblyPath();
        var scanner = new UnifiedPluginPackageScanner();

        var result = scanner.ScanPackage(packagePath);

        Assert.False(result.IsSuccess);
        Assert.Equal(UnifiedPluginPackageScanFailureType.ArtifactNotFound, result.FailureType);
        Assert.Contains("unsafe", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EntryAssembly", result.ErrorMessage);
    }

    [Fact]
    public void PackageWithInvalidFrontendManifest_ReturnsManifestParseFailure()
    {
        var packagePath = _testData.CreatePackageWithInvalidFrontendManifest();
        var scanner = new UnifiedPluginPackageScanner();

        var result = scanner.ScanPackage(packagePath);

        Assert.False(result.IsSuccess);
        Assert.Equal(UnifiedPluginPackageScanFailureType.ManifestParseError, result.FailureType);
        Assert.Contains("plugin.frontend.json", result.ErrorMessage);
    }

    [Fact]
    public void PackageWithMissingFrontendReferencedFile_ReturnsArtifactFailure()
    {
        var packagePath = _testData.CreatePackageMissingFrontendEntryAsset();
        var scanner = new UnifiedPluginPackageScanner();

        var result = scanner.ScanPackage(packagePath);

        Assert.False(result.IsSuccess);
        Assert.Equal(UnifiedPluginPackageScanFailureType.ArtifactNotFound, result.FailureType);
        Assert.Contains("assets/entry.js", result.ErrorMessage);
    }

    [Fact]
    public void PackageWithUnsafeFrontendEntryManifestPath_ReturnsArtifactFailure()
    {
        var packagePath = _testData.CreatePackageWithUnsafeFrontendEntryManifestPath();
        var scanner = new UnifiedPluginPackageScanner();

        var result = scanner.ScanPackage(packagePath);

        Assert.False(result.IsSuccess);
        Assert.Equal(UnifiedPluginPackageScanFailureType.ArtifactNotFound, result.FailureType);
        Assert.Contains("unsafe", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EntryManifest", result.ErrorMessage);
    }

    [Fact]
    public void MissingPackageRoot_ReturnsRootNotFoundFailure()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"missing-package-root-{Guid.NewGuid():N}");
        var scanner = new UnifiedPluginPackageScanner();

        var result = scanner.ScanPackage(packagePath);

        Assert.False(result.IsSuccess);
        Assert.Equal(UnifiedPluginPackageScanFailureType.PackageRootNotFound, result.FailureType);
        Assert.Contains("Package root not found", result.ErrorMessage);
    }
}
