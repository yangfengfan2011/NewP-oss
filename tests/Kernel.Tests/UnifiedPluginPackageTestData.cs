using System.Text.Json;

namespace ICP.Platform.Kernel.Tests;

public class UnifiedPluginPackageTestData : IDisposable
{
    private const string ModuleId = "Audit";
    private const string PluginId = "icp.module.audit";
    private const string BackendArtifact = "backend/plugin.backend.json";
    private const string FrontendArtifact = "frontend/plugin.frontend.json";
    private const string ChecksumManifest = "checksums/sha256sums.json";
    private readonly List<string> _tempDirectories = new();

    public string CreateValidPackage()
    {
        return CreatePackage(includePluginJson: true, validPluginJson: true, includeFrontend: true);
    }

    public string CreatePackageMissingFrontendManifest()
    {
        return CreatePackage(includePluginJson: true, validPluginJson: true, includeFrontend: false);
    }

    public string CreatePackageMissingPluginJson()
    {
        return CreatePackage(includePluginJson: false, validPluginJson: false, includeFrontend: true);
    }

    public string CreatePackageWithInvalidPluginJson()
    {
        return CreatePackage(includePluginJson: true, validPluginJson: false, includeFrontend: true);
    }

    private string CreatePackage(bool includePluginJson, bool validPluginJson, bool includeFrontend)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"plugin_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempPath);
        _tempDirectories.Add(tempPath);

        if (includePluginJson)
        {
            var pluginJsonPath = Path.Combine(tempPath, "plugin.json");
            if (validPluginJson)
            {
                var pluginJson = new
                {
                    PluginId,
                    ModuleId,
                    DisplayName = "Audit",
                    Version = "1.0.0",
                    Description = "Unified plugin package example",
                    Distribution = new
                    {
                        Channel = "internal",
                        Publisher = "ICP Platform Team"
                    },
                    HostVersion = new
                    {
                        Min = "1.0.0",
                        Max = "2.0.0"
                    },
                    Artifacts = new
                    {
                        Backend = BackendArtifact,
                        Frontend = FrontendArtifact
                    },
                    Checksums = new
                    {
                        Sha256Manifest = ChecksumManifest
                    }
                };

                File.WriteAllText(
                    pluginJsonPath,
                    JsonSerializer.Serialize(pluginJson, new JsonSerializerOptions { WriteIndented = true })
                );
            }
            else
            {
                File.WriteAllText(pluginJsonPath, "{ invalid json");
            }
        }

        var backendPath = Path.Combine(tempPath, "backend");
        Directory.CreateDirectory(backendPath);
        File.WriteAllText(
            Path.Combine(backendPath, "plugin.backend.json"),
            JsonSerializer.Serialize(new { ModuleId }, new JsonSerializerOptions { WriteIndented = true })
        );

        if (includeFrontend)
        {
            var frontendPath = Path.Combine(tempPath, "frontend");
            Directory.CreateDirectory(frontendPath);
            File.WriteAllText(
                Path.Combine(frontendPath, "plugin.frontend.json"),
                JsonSerializer.Serialize(new { ModuleId }, new JsonSerializerOptions { WriteIndented = true })
            );
        }

        var checksumsPath = Path.Combine(tempPath, "checksums");
        Directory.CreateDirectory(checksumsPath);
        File.WriteAllText(
            Path.Combine(checksumsPath, "sha256sums.json"),
            JsonSerializer.Serialize(
                new
                {
                    Files = new Dictionary<string, string>
                    {
                        ["plugin.json"] = "manifest-sha256",
                        [BackendArtifact] = "backend-sha256",
                        [FrontendArtifact] = includeFrontend ? "frontend-sha256" : string.Empty
                    }
                },
                new JsonSerializerOptions { WriteIndented = true }
            )
        );

        return tempPath;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirectories)
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
