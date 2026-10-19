using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class StandardReadExecutionContractTests
{
    [Fact]
    public void FormalStandardRead_ProvidesTheSameCompiledRequirementsToExistingMetadataConsumers()
    {
        var result = new CanonicalModulePlanCompiler().Compile("standard-read-consumers",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var providers = new[] { "standardCrud.list", "standardCrud.read", "standardCrud.options", "standardCrud.export" };
        var requirements = result.Plan!.ActionDataAccessCatalog.Where(item => providers.Contains(item.Provider, StringComparer.Ordinal)).ToArray();
        var json = JsonSerializer.Serialize(requirements, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        var path = Path.Combine(root!.FullName, "tests", "Kernel.Tests", "Fixtures", "StandardReadDataAccess.contract.json");
        if (Environment.GetEnvironmentVariable("NEWP_STANDARD_READ_CONSUMER_FIXTURE_EXPORT") == "1") File.WriteAllText(path, json);
        else Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path))),
            JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json)));
    }

    [Fact]
    public void FormalStandardRead_DetailKeepsEveryActualListReadOccurrenceWithoutAddingAFeature()
    {
        var result = new CanonicalModulePlanCompiler().Compile("detail-read-occurrences", new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        foreach (var resource in new[] { "example.record", "organization.person", "organization.company", "organization.department", "organization.position", "identity.account", "authorization.role", "part.part", "document.document" })
        {
            var list = Assert.Single(result.Plan!.ActionDataAccessCatalog, item => item.Execution == resource + ".list");
            var detail = Assert.Single(result.Plan.ActionDataAccessCatalog, item => item.Execution == resource + ".read");
            Assert.Empty(list.FeaturePositions.Except(detail.FeaturePositions, StringComparer.Ordinal));
            Assert.Equal("target", Assert.Single(detail.Requirements!).Role);
            Assert.Equal("object", Assert.Single(detail.Requirements!).Shape);
        }
    }

    [Fact]
    public void FormalStandardRead_ProjectsRegisteredLookupSourcesAtTheirExistingPositions()
    {
        var result = new CanonicalModulePlanCompiler().Compile("standard-read", new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var requirements = result.Plan!.ActionDataAccessCatalog.Where(item => item.ModuleId == "Organization" && item.Provider?.StartsWith("standardCrud.", StringComparison.Ordinal) == true).ToArray();
        var lookup = Assert.Single(requirements, item => item.Execution == "organization.position.options" && item.ViewKey == "organization.person.list");
        var detailLookup = Assert.Single(requirements, item => item.Execution == "organization.position.options" && item.ViewKey == "organization.person.detail");
        Assert.NotEqual(lookup.Id, detailLookup.Id);
        Assert.Contains(detailLookup.FeaturePositions, position => position.Contains("action:edit", StringComparison.Ordinal));
        Assert.Equal("lookupBindings", lookup.Surface); Assert.Equal("organization.positions", lookup.ActionKey);
        Assert.Contains(lookup.FeaturePositions, position => position.Contains("action:create", StringComparison.Ordinal));
        Assert.Contains(lookup.FeaturePositions, position => position.Contains("action:edit", StringComparison.Ordinal));
        Assert.Equal(new[] { "query", "objects" }, lookup.Requirements!.Select(role => role.Shape));
        Assert.All(lookup.Requirements!, role => { Assert.Equal(0, role.MinimumCount); Assert.Null(role.MaximumCount); });
        var json = JsonSerializer.Serialize(requirements, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        if (Environment.GetEnvironmentVariable("NEWP_STANDARD_READ_FIXTURE_EXPORT_PATH") is { Length: > 0 } path) File.WriteAllText(path, json);
        else
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
            var frozen = File.ReadAllText(Path.Combine(root!.FullName, "tests", "Kernel.Tests", "Fixtures", "OrganizationStandardReadDataAccess.contract.json"));
            Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json)), JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(frozen)));
        }
    }
}
