using System.Text.Json;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class LocalizationRuntimeManifestBuilderTests : IDisposable
{
    private readonly List<string> _tempRoots = new();

    [Fact]
    public void Build_WhenDicFileLivesOutsideDictionariesDirectory_StillProjectsCatalog()
    {
        var runtimeRoot = CreateRuntimeRoot();
        WriteLanguages(runtimeRoot);
        WriteMetadataFile(
            runtimeRoot,
            "Audit",
            Path.Combine("presentation", "PersonListView.dic.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "dic",
              "PersonListView": {
                "Title": {
                  "zhs": "人员中心",
                  "en": "People Center"
                }
              }
            }
            """);

        var builder = new LocalizationRuntimeManifestBuilder();

        var manifest = builder.Build(runtimeRoot);

        using var document = JsonDocument.Parse(manifest);
        Assert.Equal(
            "人员中心",
            document.RootElement
                .GetProperty("catalogs")
                .GetProperty("zhs")
                .GetProperty("PersonListView.Title")
                .GetString());
    }

    [Fact]
    public void Build_ProjectsCanonicalLocaleForEverySupportedLanguage()
    {
        var runtimeRoot = CreateRuntimeRoot();
        WriteLanguages(runtimeRoot);

        var manifest = new LocalizationRuntimeManifestBuilder().Build(runtimeRoot);

        using var document = JsonDocument.Parse(manifest);
        var languages = document.RootElement.GetProperty("supportedLanguages")
            .EnumerateArray()
            .ToDictionary(language => language.GetProperty("code").GetString()!);
        Assert.Equal("zh-CN", languages["zhs"].GetProperty("locale").GetString());
        Assert.Equal("en-US", languages["en"].GetProperty("locale").GetString());
    }

    [Fact]
    public void Build_WhenLocaleIsNotCanonical_ThrowsClearError()
    {
        var runtimeRoot = CreateRuntimeRoot();
        WriteLanguages(runtimeRoot, zhsLocale: "zh-cn");

        var exception = Assert.Throws<JsonException>(() =>
            new LocalizationRuntimeManifestBuilder().Build(runtimeRoot));

        Assert.Contains("canonical locale", exception.Message, StringComparison.Ordinal);
        Assert.Contains("zh-cn", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WhenDefaultLanguageIsUnsupported_ThrowsClearError()
    {
        var runtimeRoot = CreateRuntimeRoot();
        WriteLanguages(runtimeRoot, defaultLanguage: "fr");

        var exception = Assert.Throws<JsonException>(() =>
            new LocalizationRuntimeManifestBuilder().Build(runtimeRoot));

        Assert.Contains("default language 'fr'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("supportedLanguages", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WhenDicEntryKeyContainsDot_ThrowsClearError()
    {
        var runtimeRoot = CreateRuntimeRoot();
        WriteLanguages(runtimeRoot);
        WriteMetadataFile(
            runtimeRoot,
            "Audit",
            Path.Combine("presentation", "PersonListView.dic.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "dic",
              "PersonListView": {
                "Query.Keyword": {
                  "zhs": "关键字",
                  "en": "Keyword"
                }
              }
            }
            """);

        var builder = new LocalizationRuntimeManifestBuilder();

        var exception = Assert.Throws<JsonException>(() => builder.Build(runtimeRoot));
        Assert.Contains("must not contain '.'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Query.Keyword", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (var tempRoot in _tempRoots)
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private string CreateRuntimeRoot()
    {
        var runtimeRoot = Path.Combine(Path.GetTempPath(), $"localization-builder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(runtimeRoot);
        _tempRoots.Add(runtimeRoot);
        return runtimeRoot;
    }

    private static void WriteLanguages(
        string runtimeRoot,
        string zhsLocale = "zh-CN",
        string defaultLanguage = "zhs")
    {
        var platformMetadataRoot = Path.Combine(runtimeRoot, "modules", "Platform", "metadata");
        Directory.CreateDirectory(platformMetadataRoot);
        File.WriteAllText(
            Path.Combine(platformMetadataRoot, "languages.json"),
            """
            {
              "defaultLanguage": "__DEFAULT_LANGUAGE__",
              "supportedLanguages": [
                {
                  "code": "zhs",
                  "displayName": "简体中文",
                  "locale": "__ZHS_LOCALE__"
                },
                {
                  "code": "en",
                  "displayName": "English",
                  "locale": "en-US"
                }
              ]
            }
            """
                .Replace("__ZHS_LOCALE__", zhsLocale, StringComparison.Ordinal)
                .Replace("__DEFAULT_LANGUAGE__", defaultLanguage, StringComparison.Ordinal));
    }

    private static void WriteMetadataFile(string runtimeRoot, string moduleId, string relativePath, string content)
    {
        var filePath = Path.Combine(runtimeRoot, "modules", moduleId, "metadata", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content);
    }
}
