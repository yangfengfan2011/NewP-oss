using System.Text.Json;

namespace ICP.Platform.HostApi.Presentation;

/// <summary>
/// 持有当前 Host 启动时已激活 localization manifest 的规范 locale 集合。
/// </summary>
public sealed class ActiveLocalizationManifest
{
    private readonly string _defaultLocale;
    private readonly HashSet<string> _supportedLocales;

    /// <summary>使用已验证的默认 locale 与支持集合创建不可变校验快照。</summary>
    private ActiveLocalizationManifest(string defaultLocale, IEnumerable<string> supportedLocales)
    {
        _defaultLocale = defaultLocale;
        _supportedLocales = new HashSet<string>(supportedLocales, StringComparer.Ordinal);
    }

    /// <summary>从已激活的 runtime manifest 文件加载请求校验快照。</summary>
    public static ActiveLocalizationManifest Load(string manifestPath) =>
        Parse(File.ReadAllText(manifestPath));

    /// <summary>解析并校验请求校验所需的默认 locale 与支持集合。</summary>
    public static ActiveLocalizationManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new JsonException("Active localization manifest deserialized to null.");
        var languages = manifest.SupportedLanguages ?? [];
        var defaultLanguage = languages.SingleOrDefault(language =>
            string.Equals(language.Code, manifest.DefaultLanguage, StringComparison.Ordinal));
        if (defaultLanguage is null || string.IsNullOrWhiteSpace(defaultLanguage.Locale))
        {
            throw new JsonException("Active localization manifest default language has no canonical locale.");
        }

        var locales = languages.Select(language => language.Locale).ToArray();
        if (locales.Any(string.IsNullOrWhiteSpace)
            || locales.Distinct(StringComparer.Ordinal).Count() != locales.Length)
        {
            throw new JsonException("Active localization manifest contains an empty or duplicate canonical locale.");
        }

        return new ActiveLocalizationManifest(defaultLanguage.Locale, locales);
    }

    /// <summary>解析请求 locale；缺失时回退到 manifest 默认语言的规范 locale。</summary>
    public bool TryResolveLocale(string? requestedLocale, out string locale)
    {
        if (string.IsNullOrWhiteSpace(requestedLocale))
        {
            locale = _defaultLocale;
            return true;
        }

        locale = requestedLocale;
        return _supportedLocales.Contains(requestedLocale);
    }

    private sealed class Manifest
    {
        public string DefaultLanguage { get; set; } = string.Empty;

        public Language[]? SupportedLanguages { get; set; }
    }

    private sealed class Language
    {
        public string Code { get; set; } = string.Empty;

        public string Locale { get; set; } = string.Empty;
    }
}
