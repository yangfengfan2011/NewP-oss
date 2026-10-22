using ICP.Platform.Kernel.ModulePlanning;
using Xunit;
namespace ICP.Platform.Kernel.Tests;
[Trait("Category", "ModuleMetadataConsumer")]
public sealed class FeatureCatalogCompilerTests
{
    [Fact]
    public void CrossModuleNavigation_KeepsExecutionOwnerAndRequiresTheWorkspaceEntry()
    {
        var documents = new[]
        {
            """{"navigation":{"items":[{"key":"documents","titleKey":"Documents","featureAuthorization":true,"parentModuleId":"Workspace","parentKey":"workspace","targetKey":"documents"}]}}""",
            """{"view":{"key":"documents","mode":"detail","resourceKey":"document.document","featureActions":[{"key":"download","titleKey":"Download","behaviorKey":"document.document.download"}]}}"""
        }.Select(value => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(value));
        var declarations = StandardFeatureMetadataCompiler.Read("Document", documents);
        var catalog = FeatureCatalogCompiler.Compile(declarations.Prepend(new("Workspace", "workspace", "navigation", "工作空间", null, "workspace.spaces.access")));
        var workspace = catalog.Single(point => point.ModuleId == "Workspace");
        var document = catalog.Single(point => point.Key == "documents");
        var download = catalog.Single(point => point.Key == "download");
        Assert.Equal(workspace.Position, document.ParentPosition);
        Assert.Equal("Document", document.ModuleId);
        Assert.StartsWith(workspace.Position + "/module:Document/", document.Position);
        Assert.Equal(new[] { workspace.Position, document.Position }, download.EntryDependencies);
        Assert.Equal("Document", download.ModuleId);
        Assert.Throws<FeatureSelectionException>(() => FeatureCatalogCompiler.ValidateSelection(catalog, [document.Position, download.Position]));
        FeatureCatalogCompiler.ValidateSelection(catalog, catalog.Select(point => point.Position).ToArray());
    }

    [Fact]
    public void WorkspaceObjects_ExposeNoIndependentRootAndMoveDetailAndDownloadWithTheirEntry()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "ICP-Platform.sln"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var declarations = new[] { "Workspace", "Part", "Document" }.SelectMany(module =>
        {
            var root = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, module);
            var capsule = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(File.ReadAllBytes(Path.Combine(root, "newp.plugin.json")));
            var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories)
                .Select(path => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(File.ReadAllBytes(path)));
            return StandardFeatureMetadataCompiler.Read(module, documents, FeatureCatalogCompiler.Read(capsule, module), true);
        }).ToArray();
        var points = FeatureCatalogCompiler.Compile(declarations);
        var workspace = points.Single(point => point.ModuleId == "Workspace" && point.Key == "workspace");
        foreach (var module in new[] { "Part", "Document" })
        {
            var entry = points.Single(point => point.ModuleId == module && point.ReferenceKey == "navigation:" + (module == "Part" ? "part.parts" : "document.documents"));
            Assert.Equal(workspace.Position, entry.ParentPosition);
            Assert.All(points.Where(point => point.ModuleId == module), point => Assert.Contains(workspace.Position, point.EntryDependencies));
        }
        var download = points.Single(point => point.BehaviorKey == "document.document.download");
        Assert.Contains(workspace.Position, download.EntryDependencies);
        Assert.DoesNotContain(points, point => point.ParentPosition is null && point.ModuleId is "Part" or "Document");
        Assert.Throws<InvalidOperationException>(() => FeatureCatalogCompiler.Compile(declarations.Where(point => point.ModuleId != "Workspace")));
    }

    [Fact]
    public void CrossModuleParents_RejectMissingEntriesCyclesAndActionParents()
    {
        FeatureDeclaration Child(string module, string parentModule) => new(module, "root", "navigation", module, "root", module + ".read") { ParentModuleId = parentModule };
        Assert.Throws<InvalidOperationException>(() => FeatureCatalogCompiler.Compile([Child("A", "B")]));
        Assert.Throws<InvalidOperationException>(() => FeatureCatalogCompiler.Compile([Child("A", "B"), Child("B", "A")]));
        Assert.Throws<InvalidOperationException>(() => FeatureCatalogCompiler.Compile([
            new("B", "entry", "navigation", "入口", null, "read"), new("B", "root", "action", "操作", "entry", "save"), Child("A", "B")]));
        using var json = System.Text.Json.JsonDocument.Parse("""{"features":[{"key":"root","kind":"navigation","title":"子入口","parentKey":"root","parentModuleId":"B","behaviorKey":"a.read"}]}""");
        var declared = Assert.Single(FeatureCatalogCompiler.Read(json.RootElement, "A"));
        Assert.Equal("B", declared.ParentModuleId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("")]
    [InlineData(null)]
    public void Selection_UnknownPositionPreservesStructuredFacts(string? position)
    {
        var error = Assert.Throws<FeatureSelectionException>(() => FeatureCatalogCompiler.ValidateSelection([], [position!]));
        Assert.Equal("授权集合包含未知或失效功能。", error.Message);
        Assert.Equal("FeatureSelection.UnknownPosition", error.Code);
        var args = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(error.Args);
        Assert.Equal(position, args["position"]);
    }

    [Fact]
    public void Selection_MissingEntryPreservesOriginalPositionAndFirstMissingDependency()
    {
        var catalog = FeatureCatalogCompiler.Compile([
            new("A", "root", "navigation", "入口", null, "read"),
            new("A", "tab", "tab", "子入口", "root", "read"),
            new("A", "save", "action", "保存", "tab", "save")]);
        var action = catalog.Single(point => point.Key == "save");
        foreach (var selected in new[] { new[] { action.Position }, new[] { action.Position, action.EntryDependencies[0] } })
        {
            var error = Assert.Throws<FeatureSelectionException>(() => FeatureCatalogCompiler.ValidateSelection(catalog, selected));
            Assert.Equal("功能 保存 缺少本分支入口授权。", error.Message);
            Assert.Equal("FeatureSelection.EntryDependencyMissing", error.Code);
            var args = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(error.Args);
            Assert.Equal(action.Position, args["position"]);
            Assert.Equal(action.EntryDependencies[selected.Length - 1], args["dependencyPosition"]);
        }
        FeatureCatalogCompiler.ValidateSelection(catalog, catalog.Select(point => point.Position).ToArray());
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"featureAuthorization\":false}")]
    [InlineData("{\"featureKey\":\"\"}")]
    [InlineData("{\"featureKey\":\"missing\"}")]
    public void Shortcut_RejectsTargetWithoutRealNavigationFeature(string targetProperties)
    {
        var target = System.Text.Json.Nodes.JsonNode.Parse(targetProperties)!;
        target["key"] = "target";
        var documents = new[]
        {
            "{\"navigation\":{\"items\":[{\"key\":\"roles\",\"featureAuthorization\":true,\"targetKey\":\"roles\"}," + target.ToJsonString() + "]}}",
            """{"view":{"key":"roles","mode":"list","resourceKey":"role","rowActions":[{"key":"open","handlerKey":"open","navigationTargetKey":"target"}]}}"""
        }.Select(value => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(value));
        Assert.Throws<InvalidOperationException>(() => StandardFeatureMetadataCompiler.Read("Authorization", documents));
    }
    [Theory]
    [InlineData("refresh", "rowActions")]
    [InlineData("view", "pageActions")]
    [InlineData("open", "rowActions")]
    public void PresentationNavigation_RejectsActionsOtherThanStandardRowView(string operation, string surface)
    {
        var documents = new[]
        {
            """{"navigation":{"items":[{"key":"records","featureAuthorization":true,"targetKey":"records"},{"key":"target","routeOnly":true,"featureAuthorization":false,"page":{"pageKey":"platform.detail","parameters":{"viewKey":"record"}}}]}}""",
            "{\"view\":{\"key\":\"records\",\"mode\":\"list\",\"resourceKey\":\"record\",\"" + surface + "\":[{\"key\":\"open\",\"standardAction\":\"" + operation + "\",\"navigationTargetKey\":\"target\"}]}}"
        }.Select(value => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(value));
        Assert.Throws<InvalidOperationException>(() => StandardFeatureMetadataCompiler.Read("Module", documents));
    }

    [Fact]
    public void AuthorizationAuthoring_SeparatesManagementActionsAndKeepsShortcutAtTarget()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ICP-Platform.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var module = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root.FullName, "Authorization");
        var documents = Directory.GetFiles(Path.Combine(module, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(File.ReadAllText(path)));
        var capsule = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(File.ReadAllText(Path.Combine(module, "newp.plugin.json")));
        var points = FeatureCatalogCompiler.Compile(FeatureCatalogCompiler.Read(capsule, "Authorization")
            .Concat(StandardFeatureMetadataCompiler.Read("Authorization", documents, FeatureCatalogCompiler.Read(capsule, "Authorization"))));
        var entry = Assert.Single(points, point => point.Key == "authorization.roles");
        Assert.Equal("authorization.role.read", entry.BehaviorKey);
        Assert.Single(points, point => point.BehaviorKey == "authorization.role.create");
        Assert.Equal(2, points.Count(point => point.BehaviorKey == "authorization.role.update"));
        var members = points.Where(point => point.BehaviorKey == "authorization.role.userRoles").ToArray();
        Assert.Equal(2, members.Length);
        Assert.All(members, point => Assert.Contains("authorization.userRoles.read", point.ReadBehaviors));
        Assert.DoesNotContain(points, point => point.Key is "permissions" or "export" or "delete");
        Assert.Single(points, point => point.BehaviorKey == "authorization.workbench.save");
    }
    [Fact]
    public void Compile_PreservesPlacementAndRequiresItsOwnEntry()
    {
        var catalog = FeatureCatalogCompiler.Compile([
            new("Authorization", "workbench", "navigation", "功能授权", null, "authorization.workbench.read"),
            new("Authorization", "save", "action", "保存", "workbench", "authorization.workbench.save")]);
        Assert.Equal("Authorization/navigation:workbench/action:save", catalog[1].Position);
        Assert.Equal(["Authorization/navigation:workbench"], catalog[1].EntryDependencies);
        Assert.ThrowsAny<InvalidOperationException>(() => FeatureCatalogCompiler.ValidateSelection(catalog, [catalog[1].Position]));
        FeatureCatalogCompiler.ValidateSelection(catalog, catalog.Select(p => p.Position).ToArray());
    }
    [Fact]
    public void CustomAuthoring_OptionalTitleKeyPreservesTheLegacyCatalogIdentity()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, "Authorization"), "newp.plugin.json"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var capsule = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, "Authorization"), "newp.plugin.json")))!;
        var legacyCapsule = capsule.DeepClone();
        foreach (var feature in legacyCapsule["features"]!.AsArray()) feature!.AsObject().Remove("titleKey");
        var legacy = FeatureCatalogCompiler.Compile(FeatureCatalogCompiler.Read(System.Text.Json.JsonSerializer.SerializeToElement(legacyCapsule), "Authorization"));
        var workbench = capsule["features"]!.AsArray().Single(feature => feature!["key"]!.GetValue<string>() == "authorization.workbench")!;
        workbench["titleKey"] = "Navigation.Workbench";
        var translated = FeatureCatalogCompiler.Compile(FeatureCatalogCompiler.Read(System.Text.Json.JsonSerializer.SerializeToElement(capsule), "Authorization"));
        Assert.Equal(legacy.Select(point => (point.Position, point.BehaviorKey, point.Title, point.Order, string.Join(",", point.EntryDependencies))),
            translated.Select(point => (point.Position, point.BehaviorKey, point.Title, point.Order, string.Join(",", point.EntryDependencies))));
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        var custom = System.Text.Json.JsonSerializer.SerializeToElement(translated.Single(point => point.Key == "authorization.workbench"), options);
        Assert.Equal("Navigation.Workbench", custom.GetProperty("titleKey").GetString());
        Assert.Null(System.Text.Json.JsonSerializer.SerializeToElement(legacy.Single(point => point.Key == "authorization.workbench"), options).GetProperty("titleKey").GetString());
    }

    [Fact]
    public void CanonicalPlan_ContainsFeaturesInItsHashAndRejectsBrokenCandidate()
    {
        var candidate = new ModuleCapsuleCandidate("Authorization", "1.0.0", "Authorization", null,
            "packages/Authorization", null, null, null, null, "legacy-module", [], [], [], [], [],
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
        Assert.ThrowsAny<InvalidOperationException>(() => FeatureCatalogCompiler.ValidateSelection([], [null!]));
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
        Assert.ThrowsAny<InvalidOperationException>(() => FeatureCatalogCompiler.ValidateSelection(catalog,
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
    public void StandardExport_PreservesItsExistingBehaviorAndPublicTitleKey()
    {
        var documents = new[]
        {
            """{"navigation":{"items":[{"key":"records","featureAuthorization":true,"targetKey":"records"}]}}""",
            """{"view":{"key":"records","mode":"list","resourceKey":"record","export":{"enabled":true}}}"""
        }.Select(value => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(value));
        var catalog = FeatureCatalogCompiler.Compile(StandardFeatureMetadataCompiler.Read("A", documents));
        var entry = catalog.Single(point => point.Key == "records");
        Assert.Null(entry.TitleKey);
        var export = catalog.Single(point => point.Key == "export");
        Assert.Equal("导出", export.Title);
        Assert.Equal("record.export", export.BehaviorKey);
        Assert.Equal("Platform.Export", export.TitleKey);
        Assert.Equal([entry.Position], export.EntryDependencies);
    }

    [Fact]
    public void RealOrganizationAuthoring_CompilesSeparateListTreeAndDetailActions()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, "Organization"), "newp.plugin.json"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var source = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, "Organization");
        var root = Path.Combine(repository.FullName, ".codex-temp", "feature-authoring-" + Guid.NewGuid().ToString("N"));
        try
        {
            var module = Path.Combine(root, "modules", "Organization");
            Directory.CreateDirectory(module);
            File.Copy(Path.Combine(source, "newp.plugin.json"), Path.Combine(module, "plugin.json"));
            foreach (var file in Directory.EnumerateFiles(Path.Combine(source, "metadata"), "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(module, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var candidate = Assert.Single(new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(root, "modules")));
            var points = FeatureCatalogCompiler.Compile(candidate.Features);
            var legacy = FeatureCatalogCompiler.Compile(candidate.Features.Select(node => node with { TitleKey = null, StaticScopeTitleKey = null }));
            Assert.Equal(legacy.Select(point => (point.Position, point.BehaviorKey, point.Title, point.StaticScopeTitle, point.Order, string.Join(",", point.EntryDependencies), string.Join(",", point.ReadBehaviors))),
                points.Select(point => (point.Position, point.BehaviorKey, point.Title, point.StaticScopeTitle, point.Order, string.Join(",", point.EntryDependencies), string.Join(",", point.ReadBehaviors))));
            Assert.Contains(points, point => point.Position == "Organization/navigation:organization/navigation:organization.structure/tab:persons/surface:list.rowActions/action:edit");
            Assert.Contains(points, point => point.Position == "Organization/navigation:organization/navigation:organization.structure/tab:structure/node-type:person/surface:contextMenu/action:edit");
            Assert.Contains(points, point => point.Position == "Organization/navigation:organization/navigation:organization.structure/tab:structure/node-type:person/surface:detail.pageActions/action:edit");
            Assert.DoesNotContain(points, point => point.Kind is "page" or "group");
            Assert.All(points.Where(point => point.Key == "edit"), point => Assert.Equal("编辑", point.Title));
            Assert.DoesNotContain(points, point => point.Title.StartsWith("Navigation.", StringComparison.Ordinal));
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
            var projected = System.Text.Json.JsonSerializer.SerializeToElement(points, options);
            var navigation = projected.EnumerateArray().Single(point => point.GetProperty("key").GetString() == "organization");
            Assert.Equal("Navigation.Organization", navigation.GetProperty("titleKey").GetString());
            var persons = projected.EnumerateArray().Single(point => point.GetProperty("key").GetString() == "persons");
            Assert.Equal("OrganizationComposition.Person", persons.GetProperty("titleKey").GetString());
            var treeEdit = projected.EnumerateArray().Single(point => point.GetProperty("position").GetString() == "Organization/navigation:organization/navigation:organization.structure/tab:structure/node-type:person/surface:contextMenu/action:edit");
            Assert.Equal("OrganizationComposition.Edit", treeEdit.GetProperty("titleKey").GetString());
            Assert.Equal("OrganizationComposition.Person", treeEdit.GetProperty("staticScopeTitleKey").GetString());
            Assert.Equal("人员", treeEdit.GetProperty("staticScopeTitle").GetString());
            var listEdit = projected.EnumerateArray().Single(point => point.GetProperty("position").GetString() == "Organization/navigation:organization/navigation:organization.structure/tab:persons/surface:list.rowActions/action:edit");
            Assert.Equal("PersonListView.Action_Edit", listEdit.GetProperty("titleKey").GetString());
            var linkUser = projected.EnumerateArray().First(point => point.GetProperty("key").GetString() == "linkUser");
            Assert.Equal("PersonDetailView.Action_LinkUser", linkUser.GetProperty("titleKey").GetString());
            using var runtime = System.Text.Json.JsonDocument.Parse(new ICP.Platform.Kernel.PluginPackages.MetadataRuntimeManifestBuilder().Build(root, ["Organization"], ["Organization"], points));
            var active = runtime.RootElement.GetProperty("modules")[0].GetProperty("features");
            Assert.Equal(projected.GetArrayLength(), active.GetArrayLength());
            Assert.Equal(treeEdit.GetProperty("titleKey").GetString(), active.EnumerateArray().Single(point => point.GetProperty("position").GetString() == treeEdit.GetProperty("position").GetString()).GetProperty("titleKey").GetString());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
