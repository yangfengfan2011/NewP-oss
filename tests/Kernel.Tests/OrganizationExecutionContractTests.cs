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
    public void FourResources_DeclareOnlyTheirButtonsAndPreserveEachExistingFeatureOccurrence()
    {
        var result = new CanonicalModulePlanCompiler().Compile("organization-writes",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        foreach (var resource in new[] { "company", "department", "position", "person" })
        foreach (var operation in new[] { "create", "update", "delete" })
        {
            var business = "organization." + resource + "." + operation;
            var entries = result.Plan!.ActionDataAccessCatalog.Where(item => item.BusinessBinding == business).ToArray();
            Assert.NotEmpty(entries);
            Assert.DoesNotContain(result.Plan.Modules.SelectMany(module => module.DataExecutions), contract => contract.Key == business);
            Assert.DoesNotContain(result.Plan.ActionDataAccessCatalog, item => item.Execution == business);
            Assert.All(entries, entry =>
            {
                Assert.Equal(char.ToUpperInvariant(resource[0]) + resource[1..], entry.Object);
                Assert.Equal(new[] { operation == "update" ? "edit" : operation }, entry.Actions);
                Assert.Equal(new[] { "organization." + resource }, entry.Resources);
                Assert.Empty(entry.Execution);
                Assert.Null(entry.Requirements);
                Assert.All(entry.FeaturePositions, position =>
                {
                    var point = result.Plan.FeatureCatalog.Single(feature => feature.Position == position);
                    Assert.Equal(business, point.BehaviorKey);
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
        var positionButtons = requirements.Where(item => item.BusinessBinding == "organization.position.update").ToArray();
        Assert.Equal(new[] { "pageActions", "rowActions" }, positionButtons.Select(item => item.Surface).Order());
        Assert.All(positionButtons, item => Assert.Equal("Position", item.Object));
        Assert.Empty(positionButtons[0].FeaturePositions.Intersect(positionButtons[1].FeaturePositions));
        var positions = requirements.Where(item => item.ViewKey is "organization.position.list" or "organization.position.detail")
            .SelectMany(item => item.FeaturePositions).ToHashSet(StringComparer.Ordinal);
        var featureJson = JsonSerializer.Serialize(result.Plan.FeatureCatalog.Where(item => positions.Contains(item.Position)
                || positions.Any(position => position.StartsWith(item.Position + "/", StringComparison.Ordinal))),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        if (Environment.GetEnvironmentVariable("NEWP_POSITION_BUTTON_FEATURES_EXPORT") is { Length: > 0 } featureExport)
            File.WriteAllText(featureExport, featureJson.Replace("\r\n", "\n").Replace("\n", "\r\n"));
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
        Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(featureJson)), JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(
            File.ReadAllText(Path.Combine(root!.FullName, "tests", "Kernel.Tests", "Fixtures", "PositionButtonFeatures.contract.json")))));
        var json = JsonSerializer.Serialize(requirements, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        if (Environment.GetEnvironmentVariable("NEWP_ORGANIZATION_DATA_ACCESS_FIXTURE_EXPORT") is { Length: > 0 } export)
            File.WriteAllText(export, json.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        else
            Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json)), JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(
                File.ReadAllText(Path.Combine(root!.FullName, "tests", "Kernel.Tests", "Fixtures", "OrganizationDataAccess.contract.json")))));
    }

}
