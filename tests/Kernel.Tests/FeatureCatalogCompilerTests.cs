using ICP.Platform.Kernel.ModulePlanning;
using Xunit;
namespace ICP.Platform.Kernel.Tests;
public sealed class FeatureCatalogCompilerTests
{
    [Fact]
    public void Compile_PreservesPlacementAndRequiresItsOwnEntry()
    {
        var catalog = FeatureCatalogCompiler.Compile([
            new("Authorization", "workbench", "navigation", "功能授权", null, "authorization.workbench.read"),
            new("Authorization", "save", "action", "保存", "workbench", "authorization.workbench.save")]);
        Assert.Equal("Authorization/navigation:workbench/action:save", catalog[1].Position);
        Assert.Equal(["Authorization/navigation:workbench"], catalog[1].EntryDependencies);
        Assert.Throws<InvalidOperationException>(() => FeatureCatalogCompiler.ValidateSelection(catalog, [catalog[1].Position]));
        FeatureCatalogCompiler.ValidateSelection(catalog, catalog.Select(p => p.Position).ToArray());
    }
    [Fact]
    public void CanonicalPlan_ContainsFeaturesInItsHashAndRejectsBrokenCandidate()
    {
        var candidate = new ModuleCapsuleCandidate("Authorization", "1.0.0", "Authorization", null,
            "packages/Authorization", null, null, null, null, "legacy-module", [], [], [], [], [], [], [],
            new("fixture", "capsule", "/")) { Features = [new("Authorization", "workbench", "navigation", "功能授权", null, "read")] };
        var compiler = new CanonicalModulePlanCompiler();
        var first = compiler.Compile("feature-test", [candidate]);
        Assert.True(first.IsSuccess);
        Assert.Single(first.Plan!.Modules[0].Features);
        var changed = compiler.Compile("feature-test", [candidate with { Features = [candidate.Features[0] with { Title = "新名称" }] }]);
        Assert.NotEqual(first.Plan.PlanHash, changed.Plan!.PlanHash);
        var broken = compiler.Compile("feature-test", [candidate with { Features = [candidate.Features[0] with { ParentKey = "missing" }] }]);
        Assert.False(broken.IsSuccess);
        Assert.Null(broken.Plan);
    }
    [Theory]
    [InlineData("page", null)]
    [InlineData("action", null)]
    [InlineData("navigation", "missing")]
    public void Compile_RejectsIncompleteOrNonAuthorizationNodes(string kind, string? parent) =>
        Assert.Throws<InvalidOperationException>(() => FeatureCatalogCompiler.Compile([new("A", "a", kind, "A", parent, "read")]));
    [Fact]
    public void Compile_RejectsDuplicateAndMissingBindings()
    {
        FeatureDeclaration node = new("A", "a", "navigation", "A", null, "read");
        Assert.Throws<InvalidOperationException>(() => FeatureCatalogCompiler.Compile([node, node]));
        Assert.Throws<InvalidOperationException>(() => FeatureCatalogCompiler.Compile([node with { BehaviorKey = "" }]));
    }
    [Fact]
    public void Selection_RejectsNullPositionAsValidationFailure() =>
        Assert.Throws<InvalidOperationException>(() => FeatureCatalogCompiler.ValidateSelection([], [null!]));
    [Fact]
    public void Compile_SeparatesDeclarationReferencesFromBranchLocalKeys()
    {
        var catalog = FeatureCatalogCompiler.Compile([
            new("Organization", "management", "navigation", "组织管理", null, "read"),
            new("Organization", "persons", "tab", "人员", "management", "read") { ReferenceKey = "list" },
            new("Organization", "structure", "tab", "结构", "management", "tree"),
            new("Organization", "edit", "action", "编辑", "list", "person.update") { ReferenceKey = "list.edit" },
            new("Organization", "edit", "action", "编辑", "structure", "person.update") { ReferenceKey = "tree.edit", StaticScope = "person" }]);
        Assert.Contains(catalog, point => point.Position == "Organization/navigation:management/tab:persons/action:edit");
        Assert.Contains(catalog, point => point.Position == "Organization/navigation:management/tab:structure/node-type:person/action:edit");
        Assert.Equal(5, catalog.Count);
        var treeEdit = catalog.Single(point => point.ReferenceKey == "tree.edit");
        Assert.Equal(["Organization/navigation:management", "Organization/navigation:management/tab:structure"], treeEdit.EntryDependencies);
        Assert.Throws<InvalidOperationException>(() => FeatureCatalogCompiler.ValidateSelection(catalog,
            [treeEdit.Position, "Organization/navigation:management", "Organization/navigation:management/tab:persons"]));
    }
    [Fact]
    public void StandardView_DeclaresContainerReadAndSeparateCreate()
    {
        var documents = new[]
        {
            """{"navigation":{"items":[{"key":"management","featureAuthorization":true,"page":{"pageKey":"platform.tabs","parameters":{"viewKey":"management"}}}]}}""",
            """{"view":{"key":"management","mode":"tab","tabs":[{"key":"persons","titleKey":"人员","pageRef":{"pageKey":"platform.list","parameters":{"viewKey":"persons"}}}]}}""",
            """{"view":{"key":"persons","mode":"list","resourceKey":"organization.person","pageActions":[{"key":"create","labelKey":"创建","standardAction":"create"}]}}"""
        }.Select(value => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(value));
        var catalog = FeatureCatalogCompiler.Compile(StandardFeatureMetadataCompiler.Read("Organization", documents));
        Assert.Equal(3, catalog.Count);
        Assert.True(catalog.Single(point => point.Key == "management").Container);
        var entry = catalog.Single(point => point.Key == "persons");
        Assert.False(entry.Container);
        Assert.Equal("organization.person.read", entry.BehaviorKey);
        Assert.Equal("organization.person.create", catalog.Single(point => point.Key == "create").BehaviorKey);
    }
    [Fact]
    public void RealOrganizationAuthoring_CompilesSeparateListTreeAndDetailActions()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "modules", "Organization", "newp.plugin.json"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var source = Path.Combine(repository.FullName, "modules", "Organization");
        var root = Path.Combine(repository.FullName, ".codex-temp", "feature-authoring-" + Guid.NewGuid().ToString("N"));
        try
        {
            var module = Path.Combine(root, "Organization");
            Directory.CreateDirectory(module);
            File.Copy(Path.Combine(source, "newp.plugin.json"), Path.Combine(module, "plugin.json"));
            foreach (var file in Directory.EnumerateFiles(Path.Combine(source, "metadata"), "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(module, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var candidate = Assert.Single(new ModuleCapsuleCandidateReader().ReadBuiltIn(root));
            var points = FeatureCatalogCompiler.Compile(candidate.Features);
            Assert.Contains(points, point => point.Position == "Organization/navigation:organization/navigation:organization.structure/tab:persons/surface:list.rowActions/action:edit");
            Assert.Contains(points, point => point.Position == "Organization/navigation:organization/navigation:organization.structure/tab:structure/node-type:person/surface:contextMenu/action:edit");
            Assert.Contains(points, point => point.Position == "Organization/navigation:organization/navigation:organization.structure/tab:structure/node-type:person/surface:detail.pageActions/action:edit");
            Assert.DoesNotContain(points, point => point.Kind is "page" or "group");
            Assert.All(points.Where(point => point.Key == "edit"), point => Assert.Equal("编辑", point.Title));
            Assert.DoesNotContain(points, point => point.Title.StartsWith("Navigation.", StringComparison.Ordinal));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
