using System.Globalization;
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
        var moduleOrder = EnumerateModules(runtimeRoot).ToArray();
        return Build(runtimeRoot, moduleOrder);
    }

    /// <summary>
    /// 根据显式模块顺序生成 localization manifest。
    /// 这里由调用方提供最终生效顺序，builder 不再从目录枚举顺序推导覆盖语义。
    /// </summary>
    public string Build(string runtimeRoot, IReadOnlyList<string> moduleOrder)
    {
        var languagesManifestPath = Path.Combine(runtimeRoot, "modules", "Platform", "metadata", "languages.json");
        if (!File.Exists(languagesManifestPath))
        {
            return SerializeManifest(DefaultLanguageCode, Array.Empty<LocalizationLanguage>(), new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase));
        }

        var languagesManifest = JsonSerializer.Deserialize<LanguagesManifest>(File.ReadAllText(languagesManifestPath), ManifestJsonOptions)
            ?? throw new JsonException($"Localization manifest '{languagesManifestPath}' deserialized to null.");

        var supportedLanguages = NormalizeSupportedLanguages(languagesManifest, languagesManifestPath);
        if (string.IsNullOrWhiteSpace(languagesManifest.DefaultLanguage)
            || !supportedLanguages.ContainsKey(languagesManifest.DefaultLanguage))
        {
            throw new JsonException(
                $"Localization manifest '{languagesManifestPath}' default language '{languagesManifest.DefaultLanguage}' must exist in supportedLanguages.");
        }

        var catalogs = supportedLanguages.Values.ToDictionary(
            language => language.Code,
            _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var moduleId in moduleOrder)
        {
            foreach (var metadataFile in EnumerateMetadataJsonFiles(runtimeRoot, moduleId))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(metadataFile));
                if (!IsDicDocument(document.RootElement))
                {
                    continue;
                }

                MaterializeDictionary(metadataFile, document.RootElement, supportedLanguages, catalogs);
            }
        }

        return SerializeManifest(languagesManifest.DefaultLanguage, supportedLanguages.Values, catalogs);
    }

    private static IEnumerable<string> EnumerateModules(string runtimeRoot)
    {
        var modulesRoot = Path.Combine(runtimeRoot, "modules");
        if (!Directory.Exists(modulesRoot))
        {
            yield break;
        }

        foreach (var moduleDirectory in Directory.EnumerateDirectories(modulesRoot)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.GetFileName(moduleDirectory);
        }
    }

    private static IEnumerable<string> EnumerateMetadataJsonFiles(string runtimeRoot, string moduleId)
    {
        var metadataRoot = Path.Combine(runtimeRoot, "modules", moduleId, "metadata");
        if (!Directory.Exists(metadataRoot))
        {
            yield break;
        }

        foreach (var metadataFile in Directory
                     .EnumerateFiles(metadataRoot, "*.json", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            yield return metadataFile;
        }
    }

    private static void MaterializeDictionary(
        string dicPath,
        JsonElement root,
        IReadOnlyDictionary<string, LocalizationLanguage> supportedLanguages,
        IDictionary<string, Dictionary<string, string>> catalogs)
    {
        var dicRoot = GetSingleBusinessRoot(root, dicPath);
        var dicKey = dicRoot.Name;
        var fileStem = GetDictionaryFileStem(dicPath);

        if (!string.Equals(fileStem, dicKey, StringComparison.Ordinal))
        {
            throw new JsonException($"Dictionary '{dicPath}' file name '{fileStem}' does not match business root '{dicKey}'.");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase)
                || string.Equals(property.Name, "type", StringComparison.OrdinalIgnoreCase)
                || string.Equals(property.Name, dicKey, StringComparison.Ordinal))
            {
                continue;
            }

            throw new JsonException($"Dictionary '{dicPath}' contains unsupported top-level member '{property.Name}'.");
        }

        if (dicRoot.Value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Dictionary '{dicPath}' business root '{dicKey}' must be an object.");
        }

        foreach (var entry in dicRoot.Value.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException($"Dictionary '{dicPath}' entry '{entry.Name}' must be an object.");
            }

            // localKey 中的点号会被部分查询实现当成路径分隔符；这里只允许运行时投影阶段生成 dicKey.localKey。
            if (entry.Name.Contains('.', StringComparison.Ordinal))
            {
                throw new JsonException($"Dictionary '{dicPath}' entry key '{entry.Name}' must not contain '.'.");
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

            var catalogKey = ProjectCatalogKey(dicKey, entry.Name);

            foreach (var translation in translations)
            {
                catalogs[translation.Key][catalogKey] = translation.Value;
            }
        }
    }

    private static bool IsDicDocument(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("type", out var typeElement)
            && typeElement.ValueKind == JsonValueKind.String
            && string.Equals(typeElement.GetString(), "dic", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonProperty GetSingleBusinessRoot(JsonElement root, string dicPath)
    {
        JsonProperty dicRoot = default;
        var rootCount = 0;

        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("schemaVersion") || property.NameEquals("type"))
            {
                continue;
            }

            dicRoot = property;
            rootCount++;
        }

        if (rootCount != 1)
        {
            throw new JsonException($"Dictionary '{dicPath}' must contain exactly one business root object.");
        }

        return dicRoot;
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
        var canonicalLocales = new HashSet<string>(StringComparer.Ordinal);
        foreach (var language in languagesManifest.SupportedLanguages ?? Array.Empty<LocalizationLanguage>())
        {
            if (string.IsNullOrWhiteSpace(language.Code))
            {
                throw new JsonException($"Localization manifest '{languagesManifestPath}' contains a supported language with an empty code.");
            }

            if (string.IsNullOrWhiteSpace(language.Locale))
            {
                throw new JsonException($"Localization manifest '{languagesManifestPath}' language '{language.Code}' has an empty canonical locale.");
            }

            string canonicalLocale;
            try
            {
                canonicalLocale = CultureInfo.GetCultureInfo(language.Locale).Name;
            }
            catch (CultureNotFoundException)
            {
                throw new JsonException(
                    $"Localization manifest '{languagesManifestPath}' language '{language.Code}' has unsupported canonical locale '{language.Locale}'.");
            }

            if (!string.Equals(language.Locale, canonicalLocale, StringComparison.Ordinal))
            {
                throw new JsonException(
                    $"Localization manifest '{languagesManifestPath}' language '{language.Code}' has non-canonical locale '{language.Locale}'; use '{canonicalLocale}'.");
            }

            if (supportedLanguages.ContainsKey(language.Code))
            {
                throw new JsonException(
                    $"Localization manifest '{languagesManifestPath}' contains duplicate language code '{language.Code}'.");
            }

            if (!canonicalLocales.Add(language.Locale))
            {
                throw new JsonException(
                    $"Localization manifest '{languagesManifestPath}' contains duplicate canonical locale '{language.Locale}'.");
            }

            supportedLanguages.Add(language.Code, new LocalizationLanguage
            {
                Code = language.Code,
                DisplayName = language.DisplayName ?? string.Empty,
                Locale = language.Locale
            });
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
                    displayName = language.DisplayName,
                    locale = language.Locale
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

    private static string GetDictionaryFileStem(string dicPath)
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

        public string Locale { get; set; } = string.Empty;
    }
}
