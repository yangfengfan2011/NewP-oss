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

    private static void WriteLanguages(string runtimeRoot)
    {
        var platformMetadataRoot = Path.Combine(runtimeRoot, "modules", "Platform", "metadata");
        Directory.CreateDirectory(platformMetadataRoot);
        File.WriteAllText(
            Path.Combine(platformMetadataRoot, "languages.json"),
            """
            {
              "defaultLanguage": "zhs",
              "supportedLanguages": [
                {
                  "code": "zhs",
                  "displayName": "简体中文"
                },
                {
                  "code": "en",
                  "displayName": "English"
                }
              ]
            }
            """);
    }

    private static void WriteMetadataFile(string runtimeRoot, string moduleId, string relativePath, string content)
    {
        var filePath = Path.Combine(runtimeRoot, "modules", moduleId, "metadata", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content);
    }
}
