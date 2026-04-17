using System.Text.Json;

namespace ICP.Platform.Kernel.PluginPackages;

/// <summary>
/// 基于已落盘的 runtime 目录生成纯 localization manifest。
/// 这里只负责把 authoring 词典投影成运行时可消费的扁平 catalog，不处理其他元数据结构。
/// </summary>
public class LocalizationRuntimeManifestBuilder
{
    private const string DefaultLanguageCode = "zhs";
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 根据 runtime/modules 下的 dic 词典和语言清单生成纯 localization manifest。
    /// </summary>
    /// <param name="runtimeRoot">staging 后的 runtime 根目录。</param>
    /// <returns>只包含 defaultLanguage、supportedLanguages、catalogs 的 manifest JSON。</returns>
    public string Build(string runtimeRoot)
    {
        var languagesManifestPath = Path.Combine(runtimeRoot, "modules", "Platform", "metadata", "languages.json");
        if (!File.Exists(languagesManifestPath))
        {
            return SerializeManifest(DefaultLanguageCode, Array.Empty<LocalizationLanguage>(), new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase));
        }

        var languagesManifest = JsonSerializer.Deserialize<LanguagesManifest>(File.ReadAllText(languagesManifestPath), ManifestJsonOptions)
            ?? throw new JsonException($"Localization manifest '{languagesManifestPath}' deserialized to null.");

        var supportedLanguages = NormalizeSupportedLanguages(languagesManifest, languagesManifestPath);
        var catalogs = supportedLanguages.Values.ToDictionary(
            language => language.Code,
            _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var dicPath in EnumerateDictionaryFiles(runtimeRoot))
        {
            MaterializeDictionary(dicPath, supportedLanguages, catalogs);
        }

        return SerializeManifest(languagesManifest.DefaultLanguage, supportedLanguages.Values, catalogs);
    }

    private static IEnumerable<string> EnumerateDictionaryFiles(string runtimeRoot)
    {
        var modulesRoot = Path.Combine(runtimeRoot, "modules");
        if (!Directory.Exists(modulesRoot))
        {
            yield break;
        }

        foreach (var metadataDirectory in Directory.EnumerateDirectories(modulesRoot, "metadata", SearchOption.AllDirectories))
        {
            var dictionariesDirectory = Path.Combine(metadataDirectory, "dictionaries");
            if (!Directory.Exists(dictionariesDirectory))
            {
                continue;
            }

            foreach (var dicFile in Directory.EnumerateFiles(dictionariesDirectory, "*.dic.json", SearchOption.TopDirectoryOnly))
            {
                yield return dicFile;
            }
        }
    }

    private static void MaterializeDictionary(
        string dicPath,
        IReadOnlyDictionary<string, LocalizationLanguage> supportedLanguages,
        IDictionary<string, Dictionary<string, string>> catalogs)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(dicPath));
        var root = document.RootElement;
        var themeName = GetThemeName(dicPath);

        if (!root.TryGetProperty(themeName, out var themeObject) || themeObject.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Dictionary '{dicPath}' must contain root theme '{themeName}'.");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase)
                || string.Equals(property.Name, "type", StringComparison.OrdinalIgnoreCase)
                || string.Equals(property.Name, themeName, StringComparison.Ordinal))
            {
                continue;
            }

            throw new JsonException($"Dictionary '{dicPath}' contains unsupported top-level member '{property.Name}'.");
        }

        foreach (var entry in themeObject.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException($"Dictionary '{dicPath}' entry '{entry.Name}' must be an object.");
            }

            var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var translation in entry.Value.EnumerateObject())
            {
                if (!supportedLanguages.TryGetValue(translation.Name, out var canonicalLanguage))
                {
                    throw new JsonException(
                        $"Dictionary '{dicPath}' entry '{entry.Name}' contains unsupported language '{translation.Name}'.");
                }

                if (translation.Value.ValueKind != JsonValueKind.String)
                {
                    throw new JsonException(
                        $"Dictionary '{dicPath}' entry '{entry.Name}' language '{translation.Name}' must be a string.");
                }

                translations[canonicalLanguage.Code] = translation.Value.GetString() ?? string.Empty;
            }

            foreach (var supportedLanguage in supportedLanguages.Values)
            {
                if (!translations.ContainsKey(supportedLanguage.Code))
                {
                    throw new JsonException(
                        $"Dictionary '{dicPath}' entry '{entry.Name}' is missing supported language '{supportedLanguage.Code}'.");
                }
            }

            var catalogKey = ProjectCatalogKey(themeName, entry.Name);

            foreach (var translation in translations)
            {
                var languageCatalog = catalogs[translation.Key];
                if (languageCatalog.TryGetValue(catalogKey, out var existingValue))
                {
                    if (!string.Equals(existingValue, translation.Value, StringComparison.Ordinal))
                    {
                        throw new JsonException(
                            $"Dictionary '{dicPath}' entry '{catalogKey}' conflicts with an existing localization value for language '{translation.Key}'.");
                    }

                    continue;
                }

                languageCatalog[catalogKey] = translation.Value;
            }
        }
    }

    private static string ProjectCatalogKey(string themeName, string entryName)
    {
        if (entryName.StartsWith($"{themeName}.", StringComparison.OrdinalIgnoreCase))
        {
            return entryName;
        }

        return $"{themeName}.{entryName}";
    }

    private static IReadOnlyDictionary<string, LocalizationLanguage> NormalizeSupportedLanguages(
        LanguagesManifest languagesManifest,
        string languagesManifestPath)
    {
        var supportedLanguages = new Dictionary<string, LocalizationLanguage>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in languagesManifest.SupportedLanguages ?? Array.Empty<LocalizationLanguage>())
        {
            if (string.IsNullOrWhiteSpace(language.Code))
            {
                throw new JsonException($"Localization manifest '{languagesManifestPath}' contains a supported language with an empty code.");
            }

            supportedLanguages[language.Code] = new LocalizationLanguage
            {
                Code = language.Code,
                DisplayName = language.DisplayName ?? string.Empty
            };
        }

        return supportedLanguages;
    }

    private static string SerializeManifest(
        string defaultLanguage,
        IEnumerable<LocalizationLanguage> supportedLanguages,
        IDictionary<string, Dictionary<string, string>> catalogs)
    {
        var manifest = new
        {
            defaultLanguage,
            supportedLanguages = supportedLanguages
                .OrderBy(language => language.Code, StringComparer.OrdinalIgnoreCase)
                .Select(language => new
                {
                    code = language.Code,
                    displayName = language.DisplayName
                })
                .ToArray(),
            catalogs = catalogs
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value
                        .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase)
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        }) + Environment.NewLine;
    }

    private static string GetThemeName(string dicPath)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(dicPath);
        const string suffix = ".dic";
        if (fileNameWithoutExtension.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return fileNameWithoutExtension[..^suffix.Length];
        }

        return fileNameWithoutExtension;
    }

    private sealed class LanguagesManifest
    {
        public string DefaultLanguage { get; set; } = string.Empty;

        public LocalizationLanguage[]? SupportedLanguages { get; set; }
    }

    private sealed class LocalizationLanguage
    {
        public string Code { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;
    }
}
