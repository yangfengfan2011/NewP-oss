using System.Text.Json;

namespace ICP.Platform.HostApi.Presentation;

public sealed class ActiveLocalizationManifest
{
    private readonly string _defaultLocale;
    private readonly HashSet<string> _supportedLocales;

    private ActiveLocalizationManifest(string defaultLocale, IEnumerable<string> supportedLocales)
    {
        _defaultLocale = defaultLocale;
        _supportedLocales = new HashSet<string>(supportedLocales, StringComparer.Ordinal);
    }

    public static ActiveLocalizationManifest Load(string manifestPath) =>
        Parse(File.ReadAllText(manifestPath));

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
