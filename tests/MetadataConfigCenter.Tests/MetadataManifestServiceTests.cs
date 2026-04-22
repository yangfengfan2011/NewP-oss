using ICP.Platform.Modules.MetadataConfigCenter.Services;
using System.Text;
using Xunit;

namespace ICP.Platform.Modules.MetadataConfigCenter.Tests;

public class MetadataManifestServiceTests
{
    [Fact]
    public async Task GetModuleManifests_ReadsMetadataFilesAndFileTypeMappings()
    {
        using var fixture = await MetadataManifestFixture.CreateAsync(
            moduleId: "ConfigCenter",
            relativeFilePath: Path.Combine("settings", "ConfigCenter.settings.json"),
            fileContent: """
            {
              "schemaVersion": "1.0",
              "type": "settings",
              "settings": {
                "ConfigCenter.CacheEnabled": {
                  "value": "true"
                }
              }
            }
            """,
            mappingContent: """
            {
              "schemaVersion": "1.0",
              "mappings": {
                "settings": {
                  "adapter": "SettingDefinitionAuthoringAdapter",
                  "mergeMode": "replace"
                },
                "enum": {
                  "adapter": "EnumerationAuthoringAdapter",
                  "mergeMode": "replace"
                },
                "model": {
                  "adapter": "ModelAuthoringAdapter",
                  "mergeMode": "replace"
                },
                "nav": {
                  "adapter": "NavigationAuthoringAdapter",
                  "mergeMode": "replace"
                },
                "dic": {
                  "adapter": "DictionaryAuthoringAdapter",
                  "mergeMode": "dic-entry"
                }
              }
            }
            """);

        var service = new MetadataManifestService(fixture.RepositoryRoot, runtimeRoot: null);

        var manifests = await service.GetModuleManifestsAsync();

        var manifest = Assert.Single(manifests, item => item.ModuleId == "ConfigCenter");
        var descriptor = Assert.Single(manifest.Types, pair => pair.Key == "settings").Value;
        Assert.True(descriptor.Enabled);
        Assert.Equal("settings", descriptor.SourcePath);
        Assert.Equal("SettingDefinitionAuthoringAdapter", descriptor.Adapter);
        Assert.Equal("replace", descriptor.MergeMode);
        Assert.Equal("1.0", descriptor.SchemaVersion);
        Assert.Contains(Path.Combine("settings", "ConfigCenter.settings.json"), descriptor.SourceFiles);
    }

    [Fact]
    public async Task GetModuleManifests_ThrowsWhenTypeIsUnknown()
    {
        using var fixture = await MetadataManifestFixture.CreateAsync(
            moduleId: "ConfigCenter",
            relativeFilePath: Path.Combine("settings", "ConfigCenter.settings.json"),
            fileContent: """
            {
              "schemaVersion": "1.0",
              "type": "unknown",
              "settings": {}
            }
            """);

        var service = new MetadataManifestService(fixture.RepositoryRoot, runtimeRoot: null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetModuleManifestsAsync());

        Assert.Contains("ConfigCenter", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConfigCenter.settings.json", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unknown", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetModuleManifests_ThrowsWhenSchemaVersionIsUnsupported()
    {
        using var fixture = await MetadataManifestFixture.CreateAsync(
            moduleId: "ConfigCenter",
            relativeFilePath: Path.Combine("settings", "ConfigCenter.settings.json"),
            fileContent: """
            {
              "schemaVersion": "2.0",
              "type": "settings",
              "settings": {}
            }
            """);

        var service = new MetadataManifestService(fixture.RepositoryRoot, runtimeRoot: null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetModuleManifestsAsync());

        Assert.Contains("ConfigCenter", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConfigCenter.settings.json", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2.0", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetModuleManifests_ThrowsWhenMergeModeIsInvalid()
    {
        using var fixture = await MetadataManifestFixture.CreateAsync(
            moduleId: "ConfigCenter",
            relativeFilePath: Path.Combine("settings", "ConfigCenter.settings.json"),
            fileContent: """
            {
              "schemaVersion": "1.0",
              "type": "settings",
              "settings": {}
            }
            """,
            mappingContent: """
            {
              "schemaVersion": "1.0",
              "mappings": {
                "settings": {
                  "adapter": "SettingDefinitionAuthoringAdapter",
                  "mergeMode": "append"
                }
              }
            }
            """);

        var service = new MetadataManifestService(fixture.RepositoryRoot, runtimeRoot: null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetModuleManifestsAsync());

        Assert.Contains("file-type-mappings.json", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("append", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetModuleManifests_AcceptsNavMetadataWhenMappingExists()
    {
        using var fixture = await MetadataManifestFixture.CreateAsync(
            moduleId: "ConfigCenter",
            relativeFilePath: Path.Combine("navigation", "ConfigCenter.nav.json"),
            fileContent: """
            {
              "schemaVersion": "1.0",
              "type": "nav",
              "navigation": {
                "items": []
              }
            }
            """);

        var service = new MetadataManifestService(fixture.RepositoryRoot, runtimeRoot: null);

        var manifests = await service.GetModuleManifestsAsync();

        var manifest = Assert.Single(manifests, item => item.ModuleId == "ConfigCenter");
        var descriptor = Assert.Single(manifest.Types, pair => pair.Key == "nav").Value;
        Assert.Equal("navigation", descriptor.SourcePath);
        Assert.Equal("NavigationAuthoringAdapter", descriptor.Adapter);
        Assert.Equal("replace", descriptor.MergeMode);
        Assert.Contains(Path.Combine("navigation", "ConfigCenter.nav.json"), descriptor.SourceFiles);
    }

    [Fact]
    public async Task GetModuleManifests_ThrowsWhenDicEntryIsUsedForNonDicType()
    {
        using var fixture = await MetadataManifestFixture.CreateAsync(
            moduleId: "ConfigCenter",
            relativeFilePath: Path.Combine("settings", "ConfigCenter.settings.json"),
            fileContent: """
            {
              "schemaVersion": "1.0",
              "type": "settings",
              "settings": {}
            }
            """,
            mappingContent: """
            {
              "schemaVersion": "1.0",
              "mappings": {
                "settings": {
                  "adapter": "DictionaryAuthoringAdapter",
                  "mergeMode": "dic-entry"
                }
              }
            }
            """);

        var service = new MetadataManifestService(fixture.RepositoryRoot, runtimeRoot: null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetModuleManifestsAsync());

        Assert.Contains("ConfigCenter", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConfigCenter.settings.json", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dic-entry", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MetadataManifestFixture : IDisposable
    {
        private MetadataManifestFixture(string repositoryRoot, string tempRoot)
        {
            RepositoryRoot = repositoryRoot;
            _tempRoot = tempRoot;
        }

        private string _tempRoot { get; }

        public string RepositoryRoot { get; }

        public static async Task<MetadataManifestFixture> CreateAsync(
            string moduleId,
            string relativeFilePath,
            string fileContent,
            string? mappingContent = null)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), $"MetadataConfigCenter.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);

            var repositoryRoot = Path.Combine(tempRoot, "repository");
            Directory.CreateDirectory(repositoryRoot);

            var platformMetadataDirectory = Path.Combine(repositoryRoot, "server", "platform", "metadata");
            Directory.CreateDirectory(platformMetadataDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(platformMetadataDirectory, "file-type-mappings.json"),
                mappingContent ?? DefaultMappingContent);

            var moduleMetadataDirectory = Path.Combine(repositoryRoot, "modules", moduleId, "metadata");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(moduleMetadataDirectory, relativeFilePath))!);
            await File.WriteAllTextAsync(Path.Combine(moduleMetadataDirectory, relativeFilePath), fileContent);

            return new MetadataManifestFixture(repositoryRoot, tempRoot);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }

        private static string DefaultMappingContent => """
        {
          "schemaVersion": "1.0",
          "mappings": {
            "settings": {
              "adapter": "SettingDefinitionAuthoringAdapter",
              "mergeMode": "replace"
            },
            "enum": {
              "adapter": "EnumerationAuthoringAdapter",
              "mergeMode": "replace"
            },
            "model": {
              "adapter": "ModelAuthoringAdapter",
              "mergeMode": "replace"
            },
            "nav": {
              "adapter": "NavigationAuthoringAdapter",
              "mergeMode": "replace"
            },
            "dic": {
              "adapter": "DictionaryAuthoringAdapter",
              "mergeMode": "dic-entry"
            }
          }
        }
        """;
    }
}
