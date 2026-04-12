using ICP.Platform.Modules.MetadataConfigCenter.Adapters;
using ICP.Platform.Modules.MetadataConfigCenter.DTOs;
using ICP.Platform.Modules.MetadataConfigCenter.Services;
using ICP.Platform.Modules.MetadataConfigCenter.Storage;
using System.IO;
using Xunit;

namespace ICP.Platform.Modules.MetadataConfigCenter.Tests;

public class SettingDefinitionAuthoringServiceTests
{
    [Fact]
    public async Task GetSettings_ReadsDefinitionsFromConfigCenterSettingsFile()
    {
        using var fixture = await MetadataTestRepository.CreateAsync();
        var service = fixture.CreateSettingDefinitionService();

        var definitions = await service.GetSettingDefinitionsAsync("ConfigCenter");

        Assert.True(definitions.Count >= 4);

        var cacheEnabled = Assert.Single(definitions, definition => definition.Key == "ConfigCenter.CacheEnabled");
        Assert.Equal("启用配置缓存", cacheEnabled.DisplayName);
        Assert.Equal("性能", cacheEnabled.Group);
        Assert.Equal("true", cacheEnabled.DefaultValue);

        var refreshInterval = Assert.Single(definitions, definition => definition.Key == "ConfigCenter.CacheRefreshIntervalSeconds");
        Assert.Equal("30", refreshInterval.DefaultValue);

        var auditRetention = Assert.Single(definitions, definition => definition.Key == "ConfigCenter.AuditRetentionDays");
        Assert.Equal("180", auditRetention.DefaultValue);
    }

    [Fact]
    public async Task SaveSettings_UpdatesFileAndReturnsSavedButNotAppliedStatus()
    {
        using var fixture = await MetadataTestRepository.CreateAsync();
        var service = fixture.CreateSettingDefinitionService();
        var definitions = await service.GetSettingDefinitionsAsync("ConfigCenter");

        definitions[0].DisplayName = "启用缓存（已更新）";

        var result = await service.SaveSettingDefinitionsAsync(
            "ConfigCenter",
            new SaveSettingDefinitionsRequest
            {
                Definitions = definitions
            });

        Assert.Equal("已保存未应用", result.Status);

        var updatedContent = await File.ReadAllTextAsync(fixture.SettingsFilePath);
        Assert.Contains("启用缓存（已更新）", updatedContent);
    }

    private sealed class MetadataTestRepository : IDisposable
    {
        private readonly string _rootPath;

        private MetadataTestRepository(string rootPath)
        {
            _rootPath = rootPath;
            SettingsFilePath = Path.Combine(rootPath, "modules", "ConfigCenter", "metadata", "settings", "ConfigCenter.settings.json");
        }

        public string SettingsFilePath { get; }

        public static async Task<MetadataTestRepository> CreateAsync()
        {
            var sourceRoot = FindRepositoryRoot();
            var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-config-center-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "modules", "ConfigCenter"));

            var sourceMetadata = Path.Combine(sourceRoot, "modules", "ConfigCenter", "metadata");
            var targetMetadata = Path.Combine(tempRoot, "modules", "ConfigCenter", "metadata");
            CopyDirectory(sourceMetadata, targetMetadata);

            await Task.CompletedTask;
            return new MetadataTestRepository(tempRoot);
        }

        public SettingDefinitionAuthoringService CreateSettingDefinitionService()
        {
            var manifestService = new MetadataManifestService(_rootPath);
            var store = new ModuleMetadataStore(_rootPath);
            var adapter = new SettingDefinitionAuthoringAdapter();
            return new SettingDefinitionAuthoringService(manifestService, store, adapter);
        }

        public void Dispose()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var targetFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, targetFile, overwrite: true);
            }

            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                var targetDirectory = Path.Combine(destinationDir, Path.GetFileName(directory));
                CopyDirectory(directory, targetDirectory);
            }
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

            throw new DirectoryNotFoundException("Unable to locate repository root.");
        }
    }
}
