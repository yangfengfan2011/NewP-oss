using ICP.Platform.Kernel.PluginPackages;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class MetadataRuntimeManifestBuilderTests : IDisposable
{
    private readonly List<string> _runtimeRoots = new();

    [Fact]
    public void Build_WhenRuntimeContainsNavViewAndModelFiles_ProjectsMetadataEntries()
    {
        var runtimeRoot = CreateRuntimeRoot();
        WriteMetadataFile(
            runtimeRoot,
            "Organization",
            Path.Combine("navigation", "Organization.nav.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "nav",
              "navigation": { "items": [] }
            }
            """);
        WriteMetadataFile(
            runtimeRoot,
            "Organization",
            Path.Combine("views", "PersonList.view.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "view",
              "view": { "key": "organization.person.list", "mode": "list", "model": "Person" }
            }
            """);
        WriteMetadataFile(
            runtimeRoot,
            "Organization",
            Path.Combine("models", "Person.model.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "model",
              "Person": { "kind": "Entity" }
            }
            """);

        var builder = new MetadataRuntimeManifestBuilder();

        var manifest = builder.Build(runtimeRoot, ["Organization"], ["Organization"]);

        using var document = JsonDocument.Parse(manifest);
        var modules = document.RootElement.GetProperty("modules");
        Assert.Single(modules.EnumerateArray());
        Assert.Equal("Organization", modules[0].GetProperty("moduleId").GetString());
        Assert.Equal("/plugins/runtime/modules/Organization/metadata", modules[0].GetProperty("metadataBasePath").GetString());
        Assert.Equal("navigation/Organization.nav.json", modules[0].GetProperty("navFiles")[0].GetString());
        Assert.Equal("views/PersonList.view.json", modules[0].GetProperty("viewFiles")[0].GetString());
        Assert.Equal("models/Person.model.json", modules[0].GetProperty("modelFiles")[0].GetString());
    }

    [Fact]
    public void Build_WhenModuleHasNoNavigationViewOrModelFiles_SkipsModule()
    {
        var runtimeRoot = CreateRuntimeRoot();
        WriteMetadataFile(
            runtimeRoot,
            "Organization",
            Path.Combine("dictionaries", "PersonListView.dic.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "dic",
              "PersonListView": {}
            }
            """);

        var builder = new MetadataRuntimeManifestBuilder();

        var manifest = builder.Build(runtimeRoot, ["Organization"], ["Organization"]);

        using var document = JsonDocument.Parse(manifest);
        Assert.Empty(document.RootElement.GetProperty("modules").EnumerateArray());
    }

    [Fact]
    public void Build_WhenModuleOrderContainsDuplicateCasing_UsesDistinctModulesAndStableFileOrdering()
    {
        var runtimeRoot = CreateRuntimeRoot();
        WriteMetadataFile(
            runtimeRoot,
            "Organization",
            Path.Combine("views", "z-last.view.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "view",
              "view": { "key": "organization.person.last" }
            }
            """);
        WriteMetadataFile(
            runtimeRoot,
            "Organization",
            Path.Combine("views", "a-first.view.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "view",
              "view": { "key": "organization.person.first" }
            }
            """);
        WriteMetadataFile(
            runtimeRoot,
            "Audit",
            Path.Combine("navigation", "Audit.nav.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "nav",
              "navigation": { "items": [] }
            }
            """);

        var builder = new MetadataRuntimeManifestBuilder();

        var manifest = builder.Build(
            runtimeRoot,
            ["Organization", "organization", "Audit"],
            ["Organization", "Audit"]);

        using var document = JsonDocument.Parse(manifest);
        var modules = document.RootElement.GetProperty("modules");
        Assert.Equal(2, modules.GetArrayLength());
        Assert.Equal("Organization", modules[0].GetProperty("moduleId").GetString());
        Assert.Equal("Audit", modules[1].GetProperty("moduleId").GetString());
        Assert.Equal("views/a-first.view.json", modules[0].GetProperty("viewFiles")[0].GetString());
        Assert.Equal("views/z-last.view.json", modules[0].GetProperty("viewFiles")[1].GetString());
    }

    [Fact]
    public void Build_WhenBackendOnlyModuleHasMetadata_DoesNotPublishItToFrontendManifest()
    {
        var runtimeRoot = CreateRuntimeRoot();
        WriteMetadataFile(
            runtimeRoot,
            "Organization",
            Path.Combine("navigation", "Organization.nav.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "nav",
              "navigation": { "items": [] }
            }
            """);
        WriteMetadataFile(
            runtimeRoot,
            "Authorization",
            Path.Combine("models", "Permission.model.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "model",
              "Permission": { "kind": "Entity" }
            }
            """);

        var builder = new MetadataRuntimeManifestBuilder();

        var manifest = builder.Build(
            runtimeRoot,
            ["Organization", "Authorization"],
            ["Organization"]);

        using var document = JsonDocument.Parse(manifest);
        var modules = document.RootElement.GetProperty("modules");
        Assert.Single(modules.EnumerateArray());
        Assert.Equal("Organization", modules[0].GetProperty("moduleId").GetString());
    }

    public void Dispose()
    {
        foreach (var runtimeRoot in _runtimeRoots)
        {
            if (Directory.Exists(runtimeRoot))
            {
                Directory.Delete(runtimeRoot, recursive: true);
            }
        }
    }

    private string CreateRuntimeRoot()
    {
        var runtimeRoot = Path.Combine(Path.GetTempPath(), $"metadata_runtime_{Guid.NewGuid():N}");
        Directory.CreateDirectory(runtimeRoot);
        _runtimeRoots.Add(runtimeRoot);
        return runtimeRoot;
    }

    private static void WriteMetadataFile(string runtimeRoot, string moduleId, string relativePath, string content)
    {
        var fullPath = Path.Combine(runtimeRoot, "modules", moduleId, "metadata", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
