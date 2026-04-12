using ICP.Platform.Modules.MetadataConfigCenter.Services;
using System.IO;
using Xunit;

namespace ICP.Platform.Modules.MetadataConfigCenter.Tests;

public class MetadataManifestServiceTests
{
    [Fact]
    public async Task GetModuleManifests_ReadsConfigCenterSettingsManifest()
    {
        var repositoryRoot = FindRepositoryRoot();
        var service = new MetadataManifestService(repositoryRoot);

        var manifests = await service.GetModuleManifestsAsync();

        var configCenterManifest = Assert.Single(manifests, manifest => manifest.ModuleId == "ConfigCenter");
        var settings = Assert.Contains("settings", configCenterManifest.Types);
        Assert.True(settings.Enabled);
        Assert.Equal("settings", settings.SourcePath);
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
