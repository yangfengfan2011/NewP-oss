using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class PageCompositionMetadataTests
{
    [Fact]
    public void RealOrganizationAuthoring_UsesCanonicalReaderWithoutViewPageIdentities()
    {
        var root = RepositoryRoot();
        var source = Path.Combine(root, "modules", "Organization");
        var temporary = Directory.CreateTempSubdirectory("NewPPageComposition_");
        try
        {
            var module = Directory.CreateDirectory(Path.Combine(temporary.FullName, "Organization")).FullName;
            File.Copy(Path.Combine(source, "newp.plugin.json"), Path.Combine(module, "plugin.json"));
            foreach (var file in Directory.GetFiles(Path.Combine(source, "metadata"), "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(module, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            var candidate = Assert.Single(new ModuleCapsuleCandidateReader().ReadBuiltIn(temporary.FullName));
            Assert.Contains("organization.tree", candidate.FrontendBindingReferences);
            Assert.DoesNotContain("organization.company.tabs", candidate.FrontendBindingReferences);
            Assert.Contains("/organization/structure", candidate.Routes);
        }
        finally { temporary.Delete(true); }
    }

    [Fact]
    public void ConfigurationChains_AllowSamePageAndSiblingReuseButRejectDirectAndTreeCycles()
    {
        var documents = ReadDocuments();
        var company = documents.Single(document => document["view"]?["key"]?.GetValue<string>() == "organization.company.tabs");
        var tabs = company["view"]!["tabs"]!.AsArray();
        tabs.Add(tabs[0]!.DeepClone()); tabs[^1]!["key"] = "another";
        Validate(documents);
        tabs[^1]!["pageRef"] = JsonNode.Parse("""{"pageKey":"platform.tabs","parameters":{"viewKey":"organization.company.tabs"}}""");
        Assert.Throws<JsonException>(() => Validate(documents));
        tabs[^1]!["pageRef"] = JsonNode.Parse("""{"pageKey":"platform.tree","parameters":{"viewKey":"organization.structure"}}""");
        Assert.Throws<JsonException>(() => Validate(documents));
    }

    [Theory]
    [InlineData("defaultTabKey")]
    [InlineData("titleKey")]
    public void InvalidRequiredCompositionReferences_RejectAuthoring(string property)
    {
        var documents = ReadDocuments();
        var view = documents.Single(document => document["view"]?["key"]?.GetValue<string>() == "organization.company.tabs")["view"]!;
        if (property == "defaultTabKey") view[property] = "missing";
        else view["tabs"]![0]![property] = "missing";
        Assert.Throws<JsonException>(() => Validate(documents));
    }
    private static void Validate(JsonNode[] documents) => PageCompositionMetadataValidator.Validate(
        documents.Select(document => JsonSerializer.Deserialize<JsonElement>(document.ToJsonString())), null);
    private static JsonNode[] ReadDocuments() => Directory.GetFiles(Path.Combine(RepositoryRoot(), "modules/Organization/metadata"), "*.json", SearchOption.AllDirectories)
        .Select(path => JsonNode.Parse(File.ReadAllText(path))!).ToArray();
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "modules/Organization/newp.plugin.json"))) directory = directory.Parent;
        return directory!.FullName;
    }
}
