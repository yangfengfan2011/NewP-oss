using System.Text.Json.Nodes;
using ICP.Platform.Modeling.Metadata;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public sealed class ModuleAuthoringPathsTests
{
    [Fact]
    public void NestedLegacyMetadata_ResolvesAndEnumeratesItsOwnOwner()
    {
        var root = Path.Combine(Path.GetTempPath(), "NewP-nested-authoring-" + Guid.NewGuid().ToString("N"));
        try
        {
            var owner = Path.Combine(root, "frontend", "modules", "platform", "config-center");
            Directory.CreateDirectory(Path.Combine(owner, "metadata"));
            Assert.Equal(owner, ModuleAuthoringPaths.ResolveModuleRoot(root, "ConfigCenter"));
            Assert.Equal([owner], ModuleAuthoringPaths.EnumerateModuleRoots(root));
            var model = Path.Combine(owner, "metadata", "models", "ConfigItem.model.json");
            Directory.CreateDirectory(Path.GetDirectoryName(model)!);
            File.Copy(Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(FindRepository(), "ConfigCenter"), "metadata", "models", "ConfigItem.model.json"), model);
            Assert.Equal("ConfigCenter", new JsonModelMetadataDocumentLoader().LoadFromFile(model).ModuleId);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RealPartRegroupedUnderInforCenter_ResolvesItsOwnAuthoringRoot()
    {
        var repository = FindRepository();
        var temporary = Path.Combine(Path.GetTempPath(), "NewP-269-" + Guid.NewGuid().ToString("N"));
        try
        {
            var parent = Path.Combine(temporary, "frontend", "modules", "infor-center");
            var member = Path.Combine(parent, "part");
            Directory.CreateDirectory(member);
            var capsule = JsonNode.Parse(File.ReadAllBytes(Path.Combine(repository, "frontend", "modules", "infor-center", "newp.plugin.json")))!;
            capsule["composition"]!["members"] = new JsonArray("Part");
            File.WriteAllText(Path.Combine(parent, "newp.plugin.json"), capsule.ToJsonString());
            File.Copy(Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(repository, "Part"), "newp.plugin.json"), Path.Combine(member, "newp.plugin.json"));

            var modelPath = Path.Combine(member, "metadata", "models", "Part.model.json");
            Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
            File.Copy(Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(repository, "Part"), "metadata", "models", "Part.model.json"), modelPath);
            Assert.Equal(member, ModuleAuthoringPaths.ResolveModuleRoot(temporary, "Part"));
            Assert.Equal("Part", new JsonModelMetadataDocumentLoader().LoadFromFile(modelPath).ModuleId);
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    private static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ICP-Platform.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("NewP repository not found.");
    }
}
