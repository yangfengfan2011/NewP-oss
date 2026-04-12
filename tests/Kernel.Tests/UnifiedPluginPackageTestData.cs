using System.Text.Json;

namespace ICP.Platform.Kernel.Tests;

public class UnifiedPluginPackageTestData : IDisposable
{
    private const string DefaultModuleId = "Audit";
    private const string DefaultPluginId = "icp.module.audit";
    private const string BackendArtifact = "backend/plugin.backend.json";
    private const string FrontendArtifact = "frontend/plugin.frontend.json";
    private const string BackendEntryAssembly = "AuditModule.dll";
    private const string FrontendEntryManifest = "manifest.json";
    private const string FrontendAssetRoot = "assets";
    private const string FrontendEntryAsset = "assets/entry.js";
    private const string FrontendCssAsset = "assets/styles.css";
    private const string ChecksumManifest = "checksums/sha256sums.json";
    private readonly List<string> _tempDirectories = new();

    public string CreateValidPackage()
    {
        return CreatePackage(includePluginJson: true, validPluginJson: true, includeFrontend: true);
    }

    public string CreateValidPackageWithMetadataAndResources(
        string moduleId = DefaultModuleId,
        string version = "1.0.0",
        string platformDefaultLanguage = "zhs")
    {
        return CreatePackage(
            includePluginJson: true,
            validPluginJson: true,
            includeFrontend: true,
            moduleId: moduleId,
            version: version,
            includeModuleMetadata: true,
            includePlatformResources: true,
            platformDefaultLanguage: platformDefaultLanguage);
    }

    public string CreateValidPackage(string moduleId, string version)
    {
        return CreatePackage(includePluginJson: true, validPluginJson: true, includeFrontend: true, moduleId: moduleId, version: version);
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

    public string CreatePackageWithInvalidBackendManifest()
    {
        return CreatePackage(includePluginJson: true, validPluginJson: true, includeFrontend: true, mutateBackendManifest: _ => "{ invalid json");
    }

    public string CreatePackageMissingBackendEntryAssembly()
    {
        return CreatePackage(includePluginJson: true, validPluginJson: true, includeFrontend: true, includeBackendEntryAssembly: false);
    }

    public string CreatePackageWithUnsafeBackendEntryAssemblyPath()
    {
        return CreatePackage(includePluginJson: true, validPluginJson: true, includeFrontend: true, backendEntryAssemblyPath: "../AuditModule.dll");
    }

    public string CreatePackageWithInvalidFrontendManifest()
    {
        return CreatePackage(includePluginJson: true, validPluginJson: true, includeFrontend: true, mutateFrontendManifest: _ => "{ invalid json");
    }

    public string CreatePackageMissingFrontendEntryAsset()
    {
        return CreatePackage(includePluginJson: true, validPluginJson: true, includeFrontend: true, includeFrontendEntryAsset: false);
    }

    public string CreatePackageWithUnsafeFrontendEntryManifestPath()
    {
        return CreatePackage(includePluginJson: true, validPluginJson: true, includeFrontend: true, frontendEntryManifestPath: "../manifest.json");
    }

    private string CreatePackage(
        bool includePluginJson,
        bool validPluginJson,
        bool includeFrontend,
        string moduleId = DefaultModuleId,
        string version = "1.0.0",
        bool includeBackendEntryAssembly = true,
        string backendEntryAssemblyPath = BackendEntryAssembly,
        Func<string, string>? mutateBackendManifest = null,
        string frontendEntryManifestPath = FrontendEntryManifest,
        string frontendAssetRoot = FrontendAssetRoot,
        bool includeFrontendEntryAsset = true,
        Func<string, string>? mutateFrontendManifest = null,
        bool includeModuleMetadata = false,
        bool includePlatformResources = false,
        string platformDefaultLanguage = "zhs")
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
                    PluginId = $"icp.module.{moduleId.ToLowerInvariant()}",
                    ModuleId = moduleId,
                    DisplayName = moduleId,
                    Version = version,
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
        var backendManifestJson = JsonSerializer.Serialize(
            new
            {
                ModuleId = moduleId,
                DisplayName = moduleId,
                Version = version,
                Description = "Unified plugin package example",
                EntryAssembly = backendEntryAssemblyPath,
                Dependencies = Array.Empty<string>(),
                HostVersion = new
                {
                    Min = "1.0.0",
                    Max = "2.0.0"
                }
            },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(
            Path.Combine(backendPath, "plugin.backend.json"),
            mutateBackendManifest is null ? backendManifestJson : mutateBackendManifest(backendManifestJson)
        );
        if (includeBackendEntryAssembly)
        {
            File.WriteAllText(Path.Combine(backendPath, BackendEntryAssembly), "backend-binary");
        }

        if (includeFrontend)
        {
            var frontendPath = Path.Combine(tempPath, "frontend");
            var frontendAssetsPath = Path.Combine(frontendPath, frontendAssetRoot);
            Directory.CreateDirectory(frontendAssetsPath);
            var frontendManifestJson = JsonSerializer.Serialize(
                new
                {
                    ModuleId = moduleId,
                    EntryManifest = frontendEntryManifestPath,
                    AssetRoot = frontendAssetRoot,
                    PublicBasePath = $"/plugins/{moduleId}/{version}"
                },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(
                Path.Combine(frontendPath, "plugin.frontend.json"),
                mutateFrontendManifest is null ? frontendManifestJson : mutateFrontendManifest(frontendManifestJson)
            );
            File.WriteAllText(
                Path.Combine(frontendPath, FrontendEntryManifest),
                JsonSerializer.Serialize(
                    new
                    {
                        Entry = FrontendEntryAsset,
                        Css = new[] { FrontendCssAsset }
                    },
                    new JsonSerializerOptions { WriteIndented = true })
            );
            if (includeFrontendEntryAsset)
            {
                File.WriteAllText(Path.Combine(frontendPath, "assets", "entry.js"), "export default 'audit';\n");
            }
            File.WriteAllText(Path.Combine(frontendPath, "assets", "styles.css"), "body { color: black; }\n");
        }

        if (includeModuleMetadata)
        {
            var metadataRoot = Path.Combine(tempPath, "modules", moduleId, "metadata");
            var settingsRoot = Path.Combine(metadataRoot, "settings");
            Directory.CreateDirectory(settingsRoot);
            File.WriteAllText(
                Path.Combine(metadataRoot, "metadata.manifest.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = "1.0",
                        moduleId,
                        metadataRoot = "metadata",
                        types = new
                        {
                            settings = new
                            {
                                enabled = true,
                                sourcePath = "settings",
                                adapter = "SettingDefinitionAuthoringAdapter",
                                authoring = new
                                {
                                    editable = true
                                }
                            }
                        }
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(
                Path.Combine(settingsRoot, $"{moduleId}.settings.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        moduleId,
                        type = "settings",
                        group = "插件默认",
                        definitions = new[]
                        {
                            new
                            {
                                key = $"{moduleId}.SampleSetting",
                                displayName = "样例设置",
                                description = "来自插件包的默认元数据",
                                valueType = "String",
                                defaultValue = "plugin-default",
                                required = true,
                                isSensitive = false,
                                supportsOverride = true
                            }
                        }
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        if (includePlatformResources)
        {
            var resourcesRoot = Path.Combine(tempPath, "resources");
            var localizationRoot = Path.Combine(resourcesRoot, "localization");
            var enumerationRoot = Path.Combine(resourcesRoot, "enumerations");
            Directory.CreateDirectory(localizationRoot);
            Directory.CreateDirectory(enumerationRoot);
            File.WriteAllText(
                Path.Combine(localizationRoot, "languages.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        defaultLanguage = platformDefaultLanguage,
                        supportedLanguages = new[]
                        {
                            new
                            {
                                code = platformDefaultLanguage,
                                displayName = platformDefaultLanguage.Equals("en", StringComparison.OrdinalIgnoreCase)
                                    ? "English"
                                    : "简体中文"
                            }
                        }
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(
                Path.Combine(enumerationRoot, "Theme.json"),
                JsonSerializer.Serialize(
                    new[]
                    {
                        new
                        {
                            key = "default",
                            displayName = "默认主题"
                        }
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
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
