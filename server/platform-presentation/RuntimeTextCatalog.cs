using System.Collections.Frozen;
using System.Text.Json;
using ICP.Platform.Kernel.Localization;

namespace ICP.Platform.Presentation;

/// <summary>同一活动 localization manifest 的不可变只读消费；不扫描 authoring 目录。</summary>
public sealed class RuntimeTextCatalog : IRuntimeTextCatalog
{
    private readonly string _defaultLocale;
    private readonly FrozenDictionary<string, FrozenDictionary<string, string>> _catalogs;

    // 快照在组合时冻结，逐请求查词仅字典读取及消息长度内的参数替换。
    private RuntimeTextCatalog(string defaultLocale, Dictionary<string, FrozenDictionary<string, string>> catalogs)
    {
        _defaultLocale = defaultLocale;
        _catalogs = catalogs.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>读取由 Host 提供的已激活 manifest 路径，构造不可变查词快照。</summary>
    public static RuntimeTextCatalog Load(string activeManifestPath) => Parse(File.ReadAllText(activeManifestPath));

    /// <summary>解析正式 manifest；语言与 catalog 必须一一对应，默认语言必须受支持。</summary>
    public static RuntimeTextCatalog Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var defaultCode = root.GetProperty("defaultLanguage").GetString();
        string? defaultLocale = null;
        var catalogs = new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var language in root.GetProperty("supportedLanguages").EnumerateArray())
        {
            var code = language.GetProperty("code").GetString()!;
            var locale = language.GetProperty("locale").GetString()!;
            if (code == defaultCode) defaultLocale = locale;
            catalogs.Add(locale, root.GetProperty("catalogs").GetProperty(code).EnumerateObject()
                .ToFrozenDictionary(entry => entry.Name, entry => entry.Value.GetString()!, StringComparer.Ordinal));
        }
        if (string.IsNullOrWhiteSpace(defaultLocale)) throw new JsonException("LOCALIZATION_DEFAULT_LANGUAGE_INVALID");
        return new RuntimeTextCatalog(defaultLocale, catalogs);
    }

    /// <inheritdoc />
    public string GetText(string key, string? locale = null, IReadOnlyDictionary<string, object?>? args = null)
    {
        if (!_catalogs.TryGetValue(locale ?? _defaultLocale, out var catalog))
            throw new InvalidOperationException($"LOCALIZATION_LOCALE_UNSUPPORTED: {locale}");
        if (!catalog.TryGetValue(key, out var template))
            throw new InvalidOperationException($"LOCALIZATION_KEY_MISSING: {key}");
        return LocalizationTextTemplate.Format(template, args);
    }
}
