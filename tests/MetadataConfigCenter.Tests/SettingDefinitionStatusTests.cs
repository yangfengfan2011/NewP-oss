using ICP.Platform.Modules.MetadataConfigCenter.Adapters;
using ICP.Platform.Modules.MetadataConfigCenter.DTOs;
using ICP.Platform.Modules.MetadataConfigCenter.Services;
using ICP.Platform.Modules.MetadataConfigCenter.Storage;
using System.IO;
using Xunit;

namespace ICP.Platform.Modules.MetadataConfigCenter.Tests;

public class SettingDefinitionStatusTests
{
    [Fact]
    public async Task SaveSettingDefinitions_ReturnsPendingApplyAndGitSyncStatus()
    {
        using var fixture = await MetadataStatusTestRepository.CreateAsync();
        var service = fixture.CreateSettingDefinitionService();
        var definitions = await service.GetSettingDefinitionsAsync("ConfigCenter");

        definitions[0].DisplayName = "启用缓存（状态测试）";

        var result = await service.SaveSettingDefinitionsAsync(
            "ConfigCenter",
            new SaveSettingDefinitionsRequest
            {
                Definitions = definitions
            });

        Assert.False(result.HasUnsavedChanges);
        Assert.True(result.HasUnappliedChanges);
        Assert.True(result.GitSyncPending);
    }

    private sealed class MetadataStatusTestRepository : IDisposable
    {
        private readonly string _rootPath;

        private MetadataStatusTestRepository(string rootPath)
        {
            _rootPath = rootPath;
        }

        public static async Task<MetadataStatusTestRepository> CreateAsync()
        {
            var sourceRoot = FindRepositoryRoot();
            var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-config-center-status-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "modules", "ConfigCenter"));

            var sourceMetadata = Path.Combine(sourceRoot, "modules", "ConfigCenter", "metadata");
            var targetMetadata = Path.Combine(tempRoot, "modules", "ConfigCenter", "metadata");
            CopyDirectory(sourceMetadata, targetMetadata);

            await Task.CompletedTask;
            return new MetadataStatusTestRepository(tempRoot);
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
