using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class AuthorizationManagementReadContractTests
{
    [Fact]
    public void ActualUserRoleAndWorkbenchSources_KeepExistingPositionsAndFixedReadRequirements()
    {
        var result = new CanonicalModulePlanCompiler().Compile("authorization-management-read",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var plan = result.Plan!;
        if (Environment.GetEnvironmentVariable("NEWP_FEATURE_CATALOG_COMPARE_EXPORT") is { Length: > 0 } featurePath)
            File.WriteAllText(featurePath, JsonSerializer.Serialize(plan.FeatureCatalog, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        foreach (var (view, surface, featureSurface) in new[]
        {
            ("authorization.role.list", "rowActions", "list.rowActions"),
            ("authorization.role.detail", "pageActions", "detail.pageActions")
        })
        {
            var source = Assert.Single(plan.ActionDataAccessCatalog, item => item.ViewKey == view && item.Surface == surface && item.ActionKey == "userRoles");
            Assert.Equal("authorization.role.list", source.Execution);
            Assert.Equal("standardCrud.list", source.Provider);
            Assert.Equal("Authorization/navigation:authorization.roles/surface:" + featureSurface + "/action:userRoles", Assert.Single(source.FeaturePositions));
            var query = Assert.Single(source.Requirements!);
            Assert.Equal("query", query.Shape); Assert.Equal("read", query.Stage);
            Assert.Equal("read", Assert.Single(Assert.Single(query.AnyOf)));
        }
        var workbench = Assert.Single(plan.ActionDataAccessCatalog, item => item.ViewKey == "authorization.workbench");
        Assert.Equal("read", workbench.Surface);
        Assert.Equal("Authorization/navigation:authorization.workbench", Assert.Single(workbench.FeaturePositions));
        Assert.Equal(new[] { "catalog", "detail" }, workbench.Requirements!.Select(item => item.Branch));
        Assert.All(workbench.Requirements!, role =>
        {
            Assert.Equal("read", role.Stage);
            Assert.Equal("authorization.role", Assert.Single(role.Resources));
            Assert.Equal("read", Assert.Single(Assert.Single(role.AnyOf)));
        });
        Assert.DoesNotContain(plan.FeatureCatalog, point => point.Key == "delete" && point.ModuleId == "Authorization");
        var json = JsonSerializer.Serialize(new { moduleId = "Authorization",
                features = plan.FeatureCatalog.Where(item => item.ModuleId == "Authorization"),
                dataAccess = plan.ActionDataAccessCatalog.Where(item => item.ModuleId == "Authorization"),
                identityFeatures = plan.FeatureCatalog.Where(item => item.ModuleId == "Identity"),
                identityDataAccess = plan.ActionDataAccessCatalog.Where(item => item.ModuleId == "Identity") },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        if (Environment.GetEnvironmentVariable("NEWP_AUTHORIZATION_READ_FIXTURE_EXPORT") is { Length: > 0 } path) File.WriteAllText(path, json);
        else
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
            var frozen = File.ReadAllText(Path.Combine(root!.FullName, "tests", "Kernel.Tests", "Fixtures", "AuthorizationManagementRuntime.contract.json"));
            Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(frozen)), JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json)));
        }
    }

    [Fact]
    public void IdentityAndRoleObjectManagement_UsesExistingNonPresentedSourcesAndOneProviderFloor()
    {
        var result = new CanonicalModulePlanCompiler().Compile("platform-object-management",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        foreach (var (resource, owner, view) in new[] { ("authorization.role", "Authorization", "authorization.role.detail"),
            ("identity.account", "Identity", "identity.account.detail") })
        {
            var source = Assert.Single(result.Plan!.ActionDataAccessCatalog, item => item.Execution == resource + ".authorization.manage");
            Assert.Equal(owner, source.ModuleId); Assert.Equal(view, source.ViewKey);
            Assert.Equal("featureActions", source.Surface); Assert.Equal("authorize", source.ActionKey);
            Assert.Equal("authorization.object.manage", source.Provider); Assert.Equal("Authorization", source.ProviderModuleId);
            var feature = Assert.Single(result.Plan.FeatureCatalog, item => item.ModuleId == owner && item.BehaviorKey == resource + ".authorize");
            Assert.Equal("detail.custom", feature.ActionScope);
            Assert.Equal(feature.Position, Assert.Single(source.FeaturePositions));
            var targets = source.Requirements!.Where(role => role.Role == "target").ToArray();
            Assert.Equal(new[] { "prepare", "submit" }, targets.Select(item => item.Stage));
            Assert.All(targets, role => Assert.Equal(new[] { "read", "authorize" }, Assert.Single(role.AnyOf)));
            var viewRole = Assert.Single(source.Requirements!, role => role.Role == "view");
            Assert.Equal(("prepare", "object"), (viewRole.Stage, viewRole.Shape));
            Assert.Equal(new[] { "read" }, Assert.Single(viewRole.AnyOf));
        }
    }
}
