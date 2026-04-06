using ICP.Platform.Kernel.PluginDiscovery;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class PluginDiscoveryTests
{
    private readonly string _testDataPath;

    public PluginDiscoveryTests()
    {
        _testDataPath = Path.Combine(Directory.GetCurrentDirectory(), "TestData");
    }

    [Fact]
    public void PluginScanner_ValidPlugin_ReturnsSuccess()
    {
        // Arrange
        var pluginPath = Path.Combine(_testDataPath, "ValidPlugin");
        var scanner = new PluginScanner();

        // Act
        var result = scanner.ScanPlugin(pluginPath);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Manifest);
        Assert.Equal("TestPlugin", result.Manifest!.ModuleId);
        Assert.Equal("Test Plugin", result.Manifest.DisplayName);
        Assert.Equal("1.0.0", result.Manifest.Version);
        Assert.Equal("A test plugin", result.Manifest.Description);
        Assert.Equal("TestPlugin.dll", result.Manifest.EntryAssembly);
        Assert.Single(result.Manifest.Dependencies);
        Assert.Contains("Core", result.Manifest.Dependencies);
        Assert.NotNull(result.Manifest.HostVersion);
        Assert.Equal("1.0.0", result.Manifest.HostVersion!.Min);
        Assert.Equal("2.0.0", result.Manifest.HostVersion.Max);
    }

    [Fact]
    public void PluginScanner_MissingDirectory_ReturnsFailure()
    {
        // Arrange
        var pluginPath = Path.Combine(_testDataPath, "NonExistentPlugin");
        var scanner = new PluginScanner();

        // Act
        var result = scanner.ScanPlugin(pluginPath);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Manifest);
        Assert.Equal(PluginScanErrorType.DirectoryNotFound, result.ErrorType);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void PluginScanner_MalformedManifest_ReturnsFailure()
    {
        // Arrange
        var pluginPath = Path.Combine(_testDataPath, "MalformedPlugin");
        var scanner = new PluginScanner();

        // Act
        var result = scanner.ScanPlugin(pluginPath);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Manifest);
        Assert.Equal(PluginScanErrorType.InvalidManifest, result.ErrorType);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void PluginScanner_MissingEntryAssembly_ReturnsFailure()
    {
        // Arrange
        var pluginPath = Path.Combine(_testDataPath, "MissingAssemblyPlugin");
        var scanner = new PluginScanner();

        // Act
        var result = scanner.ScanPlugin(pluginPath);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Manifest);
        Assert.Equal(PluginScanErrorType.EntryAssemblyNotFound, result.ErrorType);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void PluginDiscoveryService_MissingRootDirectory_ReturnsEmpty()
    {
        // Arrange
        var nonExistentRoot = Path.Combine(_testDataPath, "NonExistentRoot");
        var service = new PluginDiscoveryService();

        // Act
        var results = service.DiscoverPlugins(nonExistentRoot);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void PluginDiscoveryService_ValidPluginsDirectory_ReturnsDiscoveredPlugins()
    {
        // Arrange
        var service = new PluginDiscoveryService();

        // Act
        var results = service.DiscoverPlugins(_testDataPath);

        // Assert
        Assert.NotEmpty(results);
        var validPlugin = results.FirstOrDefault(r => r.IsSuccess && r.Manifest?.ModuleId == "TestPlugin");
        Assert.NotNull(validPlugin);
    }

    [Fact]
    public void PluginDiscoveryService_MixedPlugins_ReturnsAllResults()
    {
        // Arrange
        var service = new PluginDiscoveryService();

        // Act
        var results = service.DiscoverPlugins(_testDataPath);

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.IsSuccess);
        Assert.Contains(results, r => !r.IsSuccess);
    }

    [Fact]
    public void HostVersionChecker_CompatibleVersion_ReturnsTrue()
    {
        // Arrange
        var checker = new HostVersionChecker("1.5.0");
        var hostVersion = new HostVersionRequirement { Min = "1.0.0", Max = "2.0.0" };

        // Act
        var isCompatible = checker.IsCompatible(hostVersion);

        // Assert
        Assert.True(isCompatible);
    }

    [Fact]
    public void HostVersionChecker_IncompatibleVersion_ReturnsFalse()
    {
        // Arrange
        var checker = new HostVersionChecker("1.5.0");
        var hostVersion = new HostVersionRequirement { Min = "5.0.0", Max = "6.0.0" };

        // Act
        var isCompatible = checker.IsCompatible(hostVersion);

        // Assert
        Assert.False(isCompatible);
    }

    [Fact]
    public void HostVersionChecker_OnlyMinVersion_ChecksCorrectly()
    {
        // Arrange
        var checker = new HostVersionChecker("2.0.0");
        var hostVersion = new HostVersionRequirement { Min = "1.0.0" };

        // Act
        var isCompatible = checker.IsCompatible(hostVersion);

        // Assert
        Assert.True(isCompatible);
    }

    [Fact]
    public void HostVersionChecker_BelowMinVersion_ReturnsFalse()
    {
        // Arrange
        var checker = new HostVersionChecker("0.5.0");
        var hostVersion = new HostVersionRequirement { Min = "1.0.0" };

        // Act
        var isCompatible = checker.IsCompatible(hostVersion);

        // Assert
        Assert.False(isCompatible);
    }

    [Fact]
    public void PluginCompatibilityValidator_ValidatesHostVersion()
    {
        // Arrange
        var validator = new PluginCompatibilityValidator("1.5.0");
        var manifest = new PluginManifest
        {
            ModuleId = "Test",
            DisplayName = "Test",
            Version = "1.0.0",
            EntryAssembly = "Test.dll",
            HostVersion = new HostVersionRequirement { Min = "1.0.0", Max = "2.0.0" }
        };

        // Act
        var result = validator.Validate(manifest);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void PluginCompatibilityValidator_IncompatibleHostVersion_ReturnsError()
    {
        // Arrange
        var validator = new PluginCompatibilityValidator("1.5.0");
        var manifest = new PluginManifest
        {
            ModuleId = "Test",
            DisplayName = "Test",
            Version = "1.0.0",
            EntryAssembly = "Test.dll",
            HostVersion = new HostVersionRequirement { Min = "5.0.0", Max = "6.0.0" }
        };

        // Act
        var result = validator.Validate(manifest);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("host version"));
    }

    [Fact]
    public void PluginCompatibilityValidator_MissingHostVersion_Passes()
    {
        // Arrange
        var validator = new PluginCompatibilityValidator("1.5.0");
        var manifest = new PluginManifest
        {
            ModuleId = "Test",
            DisplayName = "Test",
            Version = "1.0.0",
            EntryAssembly = "Test.dll"
        };

        // Act
        var result = validator.Validate(manifest);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void PluginScanner_ValidPlugin_ReturnsResolvedPaths()
    {
        // Arrange
        var pluginPath = Path.Combine(_testDataPath, "ValidPlugin");
        var scanner = new PluginScanner();

        // Act
        var result = scanner.ScanPlugin(pluginPath);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.PluginDirectory);
        Assert.Equal(pluginPath, result.PluginDirectory);
        Assert.NotNull(result.ResolvedEntryAssemblyPath);
        Assert.Equal(Path.Combine(pluginPath, "TestPlugin.dll"), result.ResolvedEntryAssemblyPath);
    }

    [Fact]
    public void PluginDiscoveryService_WithHostVersion_SurfacesIncompatiblePlugins()
    {
        // Arrange
        var service = new PluginDiscoveryService("1.5.0");

        // Act
        var results = service.DiscoverPlugins(_testDataPath);

        // Assert
        var incompatiblePlugin = results.FirstOrDefault(r =>
            r.Manifest?.ModuleId == "IncompatiblePlugin");
        Assert.NotNull(incompatiblePlugin);
        Assert.False(incompatiblePlugin.IsSuccess);
        Assert.Equal(PluginScanErrorType.IncompatibleHostVersion, incompatiblePlugin.ErrorType);
        Assert.NotNull(incompatiblePlugin.ErrorMessage);
        Assert.Contains("host version", incompatiblePlugin.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PluginDiscoveryService_IncompatiblePlugin_StillExposesManifest()
    {
        // Arrange
        var service = new PluginDiscoveryService("1.5.0");

        // Act
        var results = service.DiscoverPlugins(_testDataPath);

        // Assert
        var incompatiblePlugin = results.FirstOrDefault(r =>
            r.Manifest?.ModuleId == "IncompatiblePlugin");
        Assert.NotNull(incompatiblePlugin);
        Assert.NotNull(incompatiblePlugin.Manifest);
        Assert.Equal("IncompatiblePlugin", incompatiblePlugin.Manifest.ModuleId);
    }

    [Fact]
    public void PluginDiscoveryService_AllResults_IncludeResolvedPaths()
    {
        // Arrange
        var service = new PluginDiscoveryService("1.5.0");

        // Act
        var results = service.DiscoverPlugins(_testDataPath);

        // Assert
        var successfulResults = results.Where(r => r.IsSuccess).ToList();
        Assert.NotEmpty(successfulResults);
        foreach (var result in successfulResults)
        {
            Assert.NotNull(result.PluginDirectory);
            Assert.NotNull(result.ResolvedEntryAssemblyPath);
            Assert.True(Directory.Exists(result.PluginDirectory));
            Assert.True(File.Exists(result.ResolvedEntryAssemblyPath));
        }
    }

    [Fact]
    public void PluginDiscoveryService_DisabledPlugin_ReturnsFailureWithDisabledError()
    {
        // Arrange
        var disabledPluginIds = new[] { "TestPlugin" };
        var service = new PluginDiscoveryService("1.5.0", disabledPluginIds);

        // Act
        var results = service.DiscoverPlugins(_testDataPath);

        // Assert
        var disabledPlugin = results.FirstOrDefault(r => r.Manifest?.ModuleId == "TestPlugin");
        Assert.NotNull(disabledPlugin);
        Assert.False(disabledPlugin.IsSuccess);
        Assert.Equal(PluginScanErrorType.PluginDisabled, disabledPlugin.ErrorType);
        Assert.NotNull(disabledPlugin.ErrorMessage);
        Assert.Contains("disabled", disabledPlugin.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PluginDiscoveryService_DisabledPluginCaseInsensitive_ReturnsFailure()
    {
        // Arrange
        var disabledPluginIds = new[] { "testplugin" }; // lowercase
        var service = new PluginDiscoveryService("1.5.0", disabledPluginIds);

        // Act
        var results = service.DiscoverPlugins(_testDataPath);

        // Assert
        var disabledPlugin = results.FirstOrDefault(r => r.Manifest?.ModuleId == "TestPlugin");
        Assert.NotNull(disabledPlugin);
        Assert.False(disabledPlugin.IsSuccess);
        Assert.Equal(PluginScanErrorType.PluginDisabled, disabledPlugin.ErrorType);
    }

    [Fact]
    public void PluginDiscoveryService_PluginWithValidSha256_Succeeds()
    {
        // Arrange
        var service = new PluginDiscoveryService("1.5.0");

        // Act
        var results = service.DiscoverPlugins(_testDataPath);

        // Assert
        var pluginWithHash = results.FirstOrDefault(r => r.Manifest?.ModuleId == "PluginWithValidHash");
        Assert.NotNull(pluginWithHash);
        Assert.True(pluginWithHash.IsSuccess);
    }

    [Fact]
    public void PluginDiscoveryService_PluginWithInvalidSha256_ReturnsFailure()
    {
        // Arrange
        var service = new PluginDiscoveryService("1.5.0");

        // Act
        var results = service.DiscoverPlugins(_testDataPath);

        // Assert
        var pluginWithBadHash = results.FirstOrDefault(r => r.Manifest?.ModuleId == "PluginWithInvalidHash");
        Assert.NotNull(pluginWithBadHash);
        Assert.False(pluginWithBadHash.IsSuccess);
        Assert.Equal(PluginScanErrorType.EntryAssemblyHashMismatch, pluginWithBadHash.ErrorType);
        Assert.NotNull(pluginWithBadHash.ErrorMessage);
        Assert.Contains("SHA-256", pluginWithBadHash.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PluginDiscoveryService_PluginWithoutSha256_SkipsValidation()
    {
        // Arrange
        var service = new PluginDiscoveryService("1.5.0");

        // Act
        var results = service.DiscoverPlugins(_testDataPath);

        // Assert - TestPlugin has no SHA-256 in manifest, should succeed
        var pluginWithoutHash = results.FirstOrDefault(r => r.Manifest?.ModuleId == "TestPlugin");
        Assert.NotNull(pluginWithoutHash);
        Assert.True(pluginWithoutHash.IsSuccess);
    }
}
