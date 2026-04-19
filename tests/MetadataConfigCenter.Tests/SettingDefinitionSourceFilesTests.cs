using ICP.Platform.Modules.MetadataConfigCenter.Adapters;
using ICP.Platform.Modules.MetadataConfigCenter.Services;
using ICP.Platform.Modules.MetadataConfigCenter.Storage;
using Xunit;

namespace ICP.Platform.Modules.MetadataConfigCenter.Tests;

public sealed class SettingDefinitionSourceFilesTests
{
    [Fact]
    public async Task GetSettingDefinitionsAsync_WhenSourceFilesSpanMultipleDirectories_ReadsExactFiles()
    {
        await using var fixture = await MetadataSourceFilesFixture.CreateAsync();
        var service = fixture.CreateAuthoringService();

        var definitions = await service.GetSettingDefinitionsAsync("ConfigCenter");

        Assert.Contains(definitions, item => item.Key == "ConfigCenter.BaseEnabled");
        Assert.Contains(definitions, item => item.Key == "ConfigCenter.AdvancedEnabled");
    }

    [Fact]
    public void ParseDefinitions_WhenTypeIsNotSettings_Throws()
    {
        var adapter = new SettingDefinitionAuthoringAdapter();

        var exception = Assert.Throws<InvalidOperationException>(() => adapter.ParseDefinitions(
            "Wrong.settings.json",
            """
            {
              "schemaVersion": "1.0",
              "type": "enum",
              "settings": {}
            }
            """));

        Assert.Contains("type", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("settings", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplySettingDefinitionsAsync_WhenSourceFilesSpanMultipleDirectories_ProjectsExactFiles()
    {
        await using var fixture = await MetadataSourceFilesFixture.CreateAsync(includeRuntimeProjection: true);
        var baseSourcePath = Path.Combine(fixture.RootPath, "modules", "ConfigCenter", "metadata", "settings", "Base.settings.json");
        await File.WriteAllTextAsync(
            baseSourcePath,
            """
            {
              "schemaVersion": "1.0",
              "type": "settings",
              "settings": {
                "ConfigCenter.BaseEnabled": {
                  "group": "基础",
                  "displayName": "基础开关（已应用）",
                  "valueType": "Boolean",
                  "value": "true",
                  "required": true,
                  "isSensitive": false,
                  "supportsOverride": true
                }
              }
            }
            """);

        var service = fixture.CreateApplyService();

        var result = await service.ApplySettingDefinitionsAsync("ConfigCenter");

        var runtimeBasePath = Path.Combine(fixture.RuntimeRoot!, "modules", "ConfigCenter", "metadata", "settings", "Base.settings.json");
        var runtimeAdvancedPath = Path.Combine(fixture.RuntimeRoot!, "modules", "ConfigCenter", "metadata", "domain", "Advanced.settings.json");
        Assert.Contains("基础开关（已应用）", await File.ReadAllTextAsync(runtimeBasePath));
        Assert.True(File.Exists(runtimeAdvancedPath));
        Assert.Equal("已应用", result.Status);
    }

    private sealed class MetadataSourceFilesFixture : IAsyncDisposable
    {
        private MetadataSourceFilesFixture(string rootPath, string? runtimeRoot)
        {
            RootPath = rootPath;
            RuntimeRoot = runtimeRoot;
        }

        public string RootPath { get; }

        public string? RuntimeRoot { get; }

        public static async Task<MetadataSourceFilesFixture> CreateAsync(bool includeRuntimeProjection = false)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-config-center-source-files-tests", Guid.NewGuid().ToString("N"));
            var platformMetadataRoot = Path.Combine(tempRoot, "server", "platform", "metadata");
            var moduleMetadataRoot = Path.Combine(tempRoot, "modules", "ConfigCenter", "metadata");

            Directory.CreateDirectory(platformMetadataRoot);
            Directory.CreateDirectory(moduleMetadataRoot);

            await File.WriteAllTextAsync(
                Path.Combine(platformMetadataRoot, "file-type-mappings.json"),
                """
                {
                  "schemaVersion": "1.0",
                  "mappings": {
                    "settings": {
                      "adapter": "SettingDefinitionAuthoringAdapter",
                      "mergeMode": "replace"
                    }
                  }
                }
                """);

            await WriteSettingsFileAsync(
                Path.Combine(moduleMetadataRoot, "settings", "Base.settings.json"),
                """
                {
                  "schemaVersion": "1.0",
                  "type": "settings",
                  "settings": {
                    "ConfigCenter.BaseEnabled": {
                      "group": "基础",
                      "displayName": "基础开关",
                      "valueType": "Boolean",
                      "value": "true",
                      "required": true,
                      "isSensitive": false,
                      "supportsOverride": true
                    }
                  }
                }
                """);

            await WriteSettingsFileAsync(
                Path.Combine(moduleMetadataRoot, "domain", "Advanced.settings.json"),
                """
                {
                  "schemaVersion": "1.0",
                  "type": "settings",
                  "settings": {
                    "ConfigCenter.AdvancedEnabled": {
                      "group": "高级",
                      "displayName": "高级开关",
                      "valueType": "Boolean",
                      "value": "false",
                      "required": true,
                      "isSensitive": false,
                      "supportsOverride": true
                    }
                  }
                }
                """);

            string? runtimeRoot = null;
            if (includeRuntimeProjection)
            {
                runtimeRoot = Path.Combine(tempRoot, "plugins", "runtime");
                await WriteSettingsFileAsync(
                    Path.Combine(runtimeRoot, "modules", "ConfigCenter", "metadata", "settings", "Base.settings.json"),
                    """
                    {
                      "schemaVersion": "1.0",
                      "type": "settings",
                      "settings": {
                        "ConfigCenter.BaseEnabled": {
                          "group": "基础",
                          "displayName": "基础开关",
                          "valueType": "Boolean",
                          "value": "true",
                          "required": true,
                          "isSensitive": false,
                          "supportsOverride": true
                        }
                      }
                    }
                    """);
                await WriteSettingsFileAsync(
                    Path.Combine(runtimeRoot, "modules", "ConfigCenter", "metadata", "domain", "Advanced.settings.json"),
                    """
                    {
                      "schemaVersion": "1.0",
                      "type": "settings",
                      "settings": {
                        "ConfigCenter.AdvancedEnabled": {
                          "group": "高级",
                          "displayName": "高级开关",
                          "valueType": "Boolean",
                          "value": "false",
                          "required": true,
                          "isSensitive": false,
                          "supportsOverride": true
                        }
                      }
                    }
                    """);
            }

            return new MetadataSourceFilesFixture(tempRoot, runtimeRoot);
        }

        public SettingDefinitionAuthoringService CreateAuthoringService()
        {
            var manifestService = new MetadataManifestService(RootPath, RuntimeRoot);
            var metadataStore = new ModuleMetadataStore(RootPath, RuntimeRoot);
            var adapter = new SettingDefinitionAuthoringAdapter();
            return new SettingDefinitionAuthoringService(manifestService, metadataStore, adapter);
        }

        public SettingDefinitionApplyService CreateApplyService()
        {
            var manifestService = new MetadataManifestService(RootPath, RuntimeRoot);
            var metadataStore = new ModuleMetadataStore(RootPath, RuntimeRoot);
            return new SettingDefinitionApplyService(manifestService, metadataStore);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        private static async Task WriteSettingsFileAsync(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
        }
    }
}
