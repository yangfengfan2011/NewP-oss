using ICP.Platform.Modules.MetadataConfigCenter.Adapters;
using ICP.Platform.Modules.MetadataConfigCenter.Services;
using ICP.Platform.Modules.MetadataConfigCenter.Storage;
using System.IO;
using System.Reflection;
using Xunit;

namespace ICP.Platform.Modules.MetadataConfigCenter.Tests;

public class SettingDefinitionApplyServiceTests
{
    [Fact]
    public async Task ApplySettings_WhenRuntimeProjectionDiffers_CopiesSourceMetadataIntoRuntime()
    {
        using var fixture = await MetadataApplyTestRepository.CreateAsync(includeRuntimeProjection: true);
        var sourceContent = await File.ReadAllTextAsync(fixture.SourceSettingsFilePath);
        sourceContent = sourceContent.Replace(
            "\"displayName\": \"启用配置缓存\"",
            "\"displayName\": \"启用配置缓存（已应用）\"",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(fixture.SourceSettingsFilePath, sourceContent);

        var service = fixture.CreateApplyService();

        var result = await service.ApplySettingDefinitionsAsync("ConfigCenter");

        var runtimeContent = await File.ReadAllTextAsync(fixture.RuntimeSettingsFilePath!);
        Assert.Contains("启用配置缓存（已应用）", runtimeContent);
        Assert.False(result.HasUnappliedChanges);
        Assert.Equal("已应用", result.Status);
    }

    [Fact]
    public async Task ApplySettings_WhenRuntimeProjectionAlreadyMatchesSource_ReturnsNoPendingStatus()
    {
        using var fixture = await MetadataApplyTestRepository.CreateAsync(includeRuntimeProjection: true);
        var service = fixture.CreateApplyService();

        var result = await service.ApplySettingDefinitionsAsync("ConfigCenter");

        Assert.False(result.HasUnappliedChanges);
        Assert.Equal("当前没有待处理变更", result.Status);
    }

    [Fact]
    public async Task ApplySettings_WhenRuntimeSwapFails_RestoresPreviousRuntimeProjection()
    {
        using var fixture = await MetadataApplyTestRepository.CreateAsync(includeRuntimeProjection: true);
        var sourceContent = await File.ReadAllTextAsync(fixture.SourceSettingsFilePath);
        sourceContent = sourceContent.Replace(
            "\"displayName\": \"启用配置缓存\"",
            "\"displayName\": \"启用配置缓存（新版本）\"",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(fixture.SourceSettingsFilePath, sourceContent);

        var previousRuntimeContent = await File.ReadAllTextAsync(fixture.RuntimeSettingsFilePath!);
        var service = fixture.CreateApplyServiceWithSwapFailure(2, "simulated apply swap failure");

        var exception = await Assert.ThrowsAsync<IOException>(() => service.ApplySettingDefinitionsAsync("ConfigCenter"));

        Assert.Contains("swap failure", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(previousRuntimeContent, await File.ReadAllTextAsync(fixture.RuntimeSettingsFilePath!));
    }

    private sealed class MetadataApplyTestRepository : IDisposable
    {
        private readonly string _rootPath;
        private readonly string? _runtimeRoot;

        private MetadataApplyTestRepository(string rootPath, string? runtimeRoot)
        {
            _rootPath = rootPath;
            _runtimeRoot = runtimeRoot;
            SourceSettingsFilePath = Path.Combine(rootPath, "modules", "ConfigCenter", "metadata", "settings", "ConfigCenter.settings.json");
            RuntimeSettingsFilePath = string.IsNullOrWhiteSpace(runtimeRoot)
                ? null
                : Path.Combine(runtimeRoot, "modules", "ConfigCenter", "metadata", "settings", "ConfigCenter.settings.json");
        }

        public string SourceSettingsFilePath { get; }

        public string? RuntimeSettingsFilePath { get; }

        public static async Task<MetadataApplyTestRepository> CreateAsync(bool includeRuntimeProjection)
        {
            var sourceRoot = FindRepositoryRoot();
            var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-config-center-apply-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "modules", "ConfigCenter"));

            var sourceMetadata = Path.Combine(sourceRoot, "modules", "ConfigCenter", "metadata");
            var targetMetadata = Path.Combine(tempRoot, "modules", "ConfigCenter", "metadata");
            CopyDirectory(sourceMetadata, targetMetadata);

            string? runtimeRoot = null;
            if (includeRuntimeProjection)
            {
                runtimeRoot = Path.Combine(tempRoot, "plugins", "runtime");
                var runtimeMetadata = Path.Combine(runtimeRoot, "modules", "ConfigCenter", "metadata");
                CopyDirectory(sourceMetadata, runtimeMetadata);
            }

            await Task.CompletedTask;
            return new MetadataApplyTestRepository(tempRoot, runtimeRoot);
        }

        public SettingDefinitionApplyService CreateApplyService()
        {
            var manifestService = new MetadataManifestService(_rootPath, _runtimeRoot);
            var store = new ModuleMetadataStore(_rootPath, _runtimeRoot);
            return new SettingDefinitionApplyService(manifestService, store);
        }

        public SettingDefinitionApplyService CreateApplyServiceWithSwapFailure(int failOnCallNumber, string message)
        {
            var manifestService = new MetadataManifestService(_rootPath, _runtimeRoot);
            var store = new ModuleMetadataStore(_rootPath, _runtimeRoot);
            var constructor = typeof(SettingDefinitionApplyService).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                [typeof(IMetadataManifestService), typeof(ModuleMetadataStore), typeof(Action<string, string>)],
                modifiers: null);

            Assert.NotNull(constructor);

            var callCount = 0;
            return (SettingDefinitionApplyService)constructor!.Invoke(
                [
                    manifestService,
                    store,
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
