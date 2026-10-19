using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class OrganizationExecutionContractTests
{
    [Fact]
    public void PersonCompanyLookup_ProjectsQueryAndCurrentWithoutChangingItsFeatureBehavior()
    {
        var result = new CanonicalModulePlanCompiler().Compile("organization-execution",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var requirements = result.Plan!.ActionDataAccessCatalog.Where(item => item.Execution == "organization.person.company.lookup").ToArray();
        Assert.Equal(new[] { "organization.person.detail", "organization.person.list" }, requirements.Select(item => item.ViewKey).Order());
        Assert.All(requirements, requirement =>
        {
            Assert.Equal("lookupBindings", requirement.Surface);
            Assert.Equal("organization.companies", requirement.ActionKey);
            Assert.Equal("organization.backend", requirement.Provider);
            Assert.Equal(new[] { "query", "current" }, requirement.Requirements!.Select(role => role.Role));
            Assert.All(requirement.Requirements!, role =>
            {
                Assert.Equal(new[] { "organization.company" }, role.Resources);
                Assert.Equal(new[] { "read" }, Assert.Single(role.AnyOf));
                Assert.Equal(0, role.MinimumCount); Assert.Null(role.MaximumCount);
            });
            Assert.All(requirement.FeaturePositions, position =>
            {
                var feature = result.Plan.FeatureCatalog.Single(point => point.Position == position);
                Assert.True(feature.BehaviorKey == "organization.person.company.lookup" || feature.ReadBehaviors.Contains("organization.person.company.lookup"));
            });
        });
    }
    [Fact]
    public void FourResources_ReuseStandardFloorsAndOnlyTheirExistingActionOccurrences()
    {
        var result = new CanonicalModulePlanCompiler().Compile("organization-writes",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        foreach (var resource in new[] { "company", "department", "position", "person" })
        foreach (var operation in new[] { "create", "update", "delete" })
        {
            var execution = "organization." + resource + "." + operation;
            var entries = result.Plan!.ActionDataAccessCatalog.Where(item => item.Execution == execution).ToArray();
            Assert.NotEmpty(entries);
            Assert.All(entries, entry =>
            {
                Assert.Equal("standardCrud." + operation, entry.Provider);
                var submit = Assert.Single(entry.Requirements!, role => role.Stage == "submit");
                Assert.Equal(new[] { "organization." + resource }, submit.Resources);
                Assert.Equal(operation == "create" ? "candidate" : "target", submit.Role);
                Assert.Equal(operation == "create" ? "candidate" : "object", submit.Shape);
                Assert.Equal(operation == "create" ? new[] { "create" } : new[] { "read", operation == "update" ? "edit" : "delete" }, Assert.Single(submit.AnyOf));
                if (operation == "update")
                    Assert.Equal(new[] { "read" }, Assert.Single(Assert.Single(entry.Requirements!, role => role.Stage == "prepare").AnyOf));
                Assert.All(entry.FeaturePositions, position =>
                {
                    var point = result.Plan.FeatureCatalog.Single(feature => feature.Position == position);
                    Assert.Equal("organization." + resource + "." + operation, point.BehaviorKey);
                });
            });
        }
    }

    [Fact]
    public void FormalOrganizationExecutions_ProjectTheCurrentConsumerFixture()
    {
        var result = new CanonicalModulePlanCompiler().Compile("organization-consumer",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var requirements = result.Plan!.ActionDataAccessCatalog.Where(item => item.ModuleId == "Organization").ToArray();
        if (Environment.GetEnvironmentVariable("NEWP_ORGANIZATION_FEATURE_SNAPSHOT") is { Length: > 0 } snapshot)
            File.WriteAllText(snapshot, JsonSerializer.Serialize(new { FeatureCatalog = result.Plan.FeatureCatalog }));
        var sources = new[] { "organization.person.company.lookup", "organization.person.department.lookup",
            "organization.department.company.lookup", "organization.department.parent.lookup" };
        Assert.All(sources, source => Assert.Contains(requirements, item => item.Execution == source));
        var tree = Assert.Single(requirements, item => item.Execution == "organization.structure.read");
        var role = Assert.Single(tree.Requirements!);
        Assert.Equal("query", role.Shape); Assert.Equal(new[] { "read" }, Assert.Single(role.AnyOf));
        Assert.Equal(new[] { "organization.company", "organization.department", "organization.person" }, role.Resources);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        var json = JsonSerializer.Serialize(requirements, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        if (Environment.GetEnvironmentVariable("NEWP_ORGANIZATION_DATA_ACCESS_FIXTURE_EXPORT") is { Length: > 0 } export)
            File.WriteAllText(export, json.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        else
            Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json)), JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(
                File.ReadAllText(Path.Combine(root!.FullName, "tests", "Kernel.Tests", "Fixtures", "OrganizationDataAccess.contract.json")))));
    }

}
