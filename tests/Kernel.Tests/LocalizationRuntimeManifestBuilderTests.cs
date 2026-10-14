using System.Text.Json;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class LocalizationRuntimeManifestBuilderTests : IDisposable
{
    private readonly List<string> _tempRoots = new();

    [Fact]
    public void Build_CurrentDocumentDictionary_ProjectsFileUploadAndDownloadCatalogs()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        var relativePath = Path.Combine("frontend", "modules", "infor-center", "document", "metadata", "dictionaries", "Document.dic.json");
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, relativePath)))
            repository = repository.Parent;
        Assert.NotNull(repository);
        var runtimeRoot = CreateRuntimeRoot();
        WriteLanguages(runtimeRoot);
        // 保留 authoring 原始成员，真实 builder 必须发现同一文件内重复键，不能先转成 Dictionary 覆盖。
        WriteMetadataFile(runtimeRoot, "Document", "Document.dic.json", File.ReadAllText(Path.Combine(repository.FullName, relativePath)));

        using var manifest = JsonDocument.Parse(new LocalizationRuntimeManifestBuilder().Build(runtimeRoot));

        var catalogs = manifest.RootElement.GetProperty("catalogs");
        var chinese = catalogs.GetProperty("zhs");
        Assert.Equal("文件", chinese.GetProperty("Document.Block_Files").GetString());
        Assert.Equal("主文件", chinese.GetProperty("Document.MainFile").GetString());
        Assert.Equal("选择主文件", chinese.GetProperty("Document.SelectMainFile").GetString());
        Assert.Equal("上传进度", chinese.GetProperty("Document.UploadProgress").GetString());
        Assert.Equal("取消上传", chinese.GetProperty("Document.CancelUpload").GetString());
        Assert.Equal("主文件已上传，保存后生效。", chinese.GetProperty("Document.Staged").GetString());
        Assert.Equal("下载", chinese.GetProperty("Document.Download").GetString());
        var english = catalogs.GetProperty("en");
        Assert.Equal("Choose main file", english.GetProperty("Document.SelectMainFile").GetString());
        Assert.Equal("Main file", english.GetProperty("Document.MainFile").GetString());
        Assert.Equal("Download", english.GetProperty("Document.Download").GetString());
    }

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
    public void Build_WhenLanguageCodeIsDuplicated_ThrowsClearError()
    {
        var runtimeRoot = CreateRuntimeRoot();
        WriteLanguages(runtimeRoot, englishCode: "ZHS");

        var exception = Assert.Throws<JsonException>(() =>
            new LocalizationRuntimeManifestBuilder().Build(runtimeRoot));

        Assert.Contains("duplicate language code", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ZHS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WhenCanonicalLocaleIsDuplicated_ThrowsClearError()
    {
        var runtimeRoot = CreateRuntimeRoot();
        WriteLanguages(runtimeRoot, englishLocale: "zh-CN");

        var exception = Assert.Throws<JsonException>(() =>
            new LocalizationRuntimeManifestBuilder().Build(runtimeRoot));

        Assert.Contains("duplicate canonical locale", exception.Message, StringComparison.Ordinal);
        Assert.Contains("zh-CN", exception.Message, StringComparison.Ordinal);
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

    [Fact]
    public void Build_ComplementarySharedRootPreservesBothContributions()
    {
        var root = CreateRuntimeRoot();
        WriteLanguages(root);
        WriteDictionary(root, "Identity", "Account", "账号", "Account");
        WriteDictionary(root, "Organization", "Person", "人员", "Person");
        using var manifest = JsonDocument.Parse(new LocalizationRuntimeManifestBuilder().Build(root));
        var catalog = manifest.RootElement.GetProperty("catalogs").GetProperty("en");
        Assert.Equal("Account", catalog.GetProperty("Navigation.Account").GetString());
        Assert.Equal("Person", catalog.GetProperty("Navigation.Person").GetString());
    }

    [Fact]
    public void Build_DuplicateCompleteKeyReportsBothSources()
    {
        var root = CreateRuntimeRoot();
        WriteLanguages(root);
        WriteDictionary(root, "Identity", "Account", "账号", "Account");
        WriteDictionary(root, "Organization", "Account", "其他账号", "Other account");
        var error = Assert.Throws<JsonException>(() => new LocalizationRuntimeManifestBuilder().Build(root));
        Assert.Contains("Navigation.Account", error.Message);
        Assert.Contains("Identity", error.Message);
        Assert.Contains("Organization", error.Message);
    }

    [Theory]
    [InlineData("{count} 条", "{limit} items", "parameters")]
    [InlineData("条目", null, "missing supported language 'en'")]
    public void Build_InvalidTranslationReportsSourceAndCompleteKey(string chinese, string? english, string diagnostic)
    {
        var root = CreateRuntimeRoot();
        WriteLanguages(root);
        WriteDictionary(root, "Organization", "Person", chinese, english);
        var error = Assert.Throws<JsonException>(() => new LocalizationRuntimeManifestBuilder().Build(root));
        Assert.Contains("Navigation.Person", error.Message);
        Assert.Contains("Organization", error.Message);
        Assert.Contains(diagnostic, error.Message);
    }

    private static void WriteDictionary(string root, string module, string key, string chinese, string? english)
    {
        var translations = new Dictionary<string, string> { ["zhs"] = chinese };
        if (english is not null) translations["en"] = english;
        WriteMetadataFile(root, module, "Navigation.dic.json", JsonSerializer.Serialize(new
        {
            type = "dic", Navigation = new Dictionary<string, object> { [key] = translations }
        }));
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
        string defaultLanguage = "zhs",
        string englishCode = "en",
        string englishLocale = "en-US")
    {
        var platformMetadataRoot = Path.Combine(Path.Combine(runtimeRoot, "modules", "Platform"), "metadata");
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
                  "code": "__ENGLISH_CODE__",
                  "displayName": "English",
                  "locale": "__ENGLISH_LOCALE__"
                }
              ]
            }
            """
                .Replace("__ZHS_LOCALE__", zhsLocale, StringComparison.Ordinal)
                .Replace("__DEFAULT_LANGUAGE__", defaultLanguage, StringComparison.Ordinal)
                .Replace("__ENGLISH_CODE__", englishCode, StringComparison.Ordinal)
                .Replace("__ENGLISH_LOCALE__", englishLocale, StringComparison.Ordinal));
    }

    private static void WriteMetadataFile(string runtimeRoot, string moduleId, string relativePath, string content)
    {
        var filePath = Path.Combine(Path.Combine(runtimeRoot, "modules", moduleId), "metadata", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content);
    }
}
