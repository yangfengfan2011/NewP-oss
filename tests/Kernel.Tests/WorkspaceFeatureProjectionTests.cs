using ICP.Platform.Testing.Authorization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class WorkspaceFeatureProjectionTests
{
    [Fact]
    [Trait("Category", "ModuleMetadataConsumer")]
    public void PermissionReadNavigation_UsesExistingBrowseIdentityWithoutNewGrants()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var moduleRoot = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, "Workspace");
        using var capsule = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(moduleRoot, "newp.plugin.json")));
        var documents = Directory.GetFiles(Path.Combine(moduleRoot, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path))).ToArray();
        var catalog = RuntimeFeatureCatalogTestSupport.CompileWithParents(StandardFeatureMetadataCompiler.Read("Workspace", documents,
            FeatureCatalogCompiler.Read(capsule.RootElement, "Workspace"), includeCustomFeatures: true));
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repository.FullName,
            "tests", "Kernel.Tests", "Fixtures", "WorkspacePermissionFeatures.contract.json")));
        var read = Assert.Single(catalog, point => point.Kind == "navigation" && point.Key == "workspace");
        var manage = Assert.Single(catalog, point => point.Key == "authorizeContent" && point.ActionScope == "list.rowActions");
        Assert.Equal(fixture.RootElement.GetProperty("readPosition").GetString(), read.Position);
        Assert.Equal(fixture.RootElement.GetProperty("managePosition").GetString(), manage.Position);
        Assert.Contains("workspace.folder.read", read.ReadBehaviors);
        Assert.Equal("workspace.folder.authorize", manage.BehaviorKey);
        Assert.DoesNotContain(catalog, point => point.Key == "workspace.permissions" || point.Key == "viewAuthorization"
            || point.Position.Contains("navigation:workspace.permissions", StringComparison.Ordinal));
        FeatureCatalogCompiler.ValidateSelection(catalog, RuntimeFeatureCatalogTestSupport.ExpandGrantSet([read]));
    }

    [Theory, Trait("Category", "ModuleMetadataConsumer")]
    [InlineData("Part", "part.part", "part.parts")]
    [InlineData("Document", "document.document", "document.documents")]
    public void IndependentPermissionReadNavigation_UsesOnlyExistingReadAndActualManagePositions(string moduleId, string resourceKey, string entryKey)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var moduleRoot = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, moduleId);
        using var capsule = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(moduleRoot, "newp.plugin.json")));
        var documents = Directory.GetFiles(Path.Combine(moduleRoot, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path))).ToArray();
        var catalog = RuntimeFeatureCatalogTestSupport.CompileWithParents(StandardFeatureMetadataCompiler.Read(moduleId, documents,
            FeatureCatalogCompiler.Read(capsule.RootElement, moduleId), includeCustomFeatures: true));
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repository.FullName,
            "tests", "Kernel.Tests", "Fixtures", moduleId + "PermissionFeatures.contract.json")));
        var read = Assert.Single(catalog, point => point.Kind == "navigation" && point.Key == entryKey);
        var manage = Assert.Single(catalog, point => point.Key == "authorize" && point.ActionScope == "list.rowActions");
        Assert.Equal(fixture.RootElement.GetProperty("readPosition").GetString(), read.Position);
        Assert.Equal(fixture.RootElement.GetProperty("managePosition").GetString(), manage.Position);
        Assert.Contains(resourceKey + ".read", read.ReadBehaviors);
        Assert.Equal(resourceKey + ".authorize", manage.BehaviorKey);
        Assert.Contains(resourceKey + ".read", manage.ReadBehaviors);
        Assert.DoesNotContain(catalog, point => point.Key == resourceKey + ".permissions" || point.Key == "viewAuthorization"
            || point.Position.Contains("navigation:" + resourceKey + ".permissions", StringComparison.Ordinal));
        FeatureCatalogCompiler.ValidateSelection(catalog, RuntimeFeatureCatalogTestSupport.ExpandGrantSet([read]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TreeReference_ReusesTheTargetActionOperationAndAdditionalReads(bool standard)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var root = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, "Workspace");
        var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!).ToArray();
        var list = documents.Single(document => document["view"]?["key"]?.GetValue<string>() == "workspace.folder.list")["view"]!;
        var target = list["rowActions"]!.AsArray().Single(action => action!["key"]!.GetValue<string>() == "edit")!;
        target["readBehaviors"] = new JsonArray("tree:workspace.structure");
        if (standard) target["standardAction"] = "edit";
        var tree = documents.Single(document => document["view"]?["key"]?.GetValue<string>() == "workspace.structure")["view"]!;
        var reference = tree["nodeTypes"]![0]!["contextMenu"]!.AsArray().Single(item => item!["key"]!.GetValue<string>() == "edit")!.AsObject();
        if (standard) { reference.Remove("actionKey"); reference["standardAction"] = "edit"; }
        var capsule = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(root, "newp.plugin.json")));
        var points = RuntimeFeatureCatalogTestSupport.CompileWithParents(StandardFeatureMetadataCompiler.Read("Workspace",
            documents.Select(document => JsonSerializer.SerializeToElement(document)), FeatureCatalogCompiler.Read(capsule, "Workspace"), true));
        var row = Assert.Single(points, point => point.Key == "edit" && point.ActionScope == "list.rowActions");
        var node = Assert.Single(points, point => point.Key == "edit" && point.ActionScope == "contextMenu");
        Assert.Equal(standard ? "workspace.folder.update" : "workspace.folder.editObject", node.BehaviorKey);
        Assert.Equal(row.BehaviorKey, node.BehaviorKey);
        Assert.Equal(row.ReadBehaviors, node.ReadBehaviors);
        Assert.Contains("tree:workspace.structure", node.ReadBehaviors);
    }

    [Fact]
    public void CustomNavigation_ComposesStandardReadAndCreateWithoutReplacingInitializationBehavior()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root.FullName, "Workspace"), "newp.plugin.json"))) root = root.Parent;
        var moduleRoot = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root!.FullName, "Workspace");
        using var capsule = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(moduleRoot, "newp.plugin.json")));
        var custom = FeatureCatalogCompiler.Read(capsule.RootElement, "Workspace");
        var documents = Directory.GetFiles(Path.Combine(moduleRoot, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path))).ToArray();
        var catalog = RuntimeFeatureCatalogTestSupport.CompileWithParents(StandardFeatureMetadataCompiler.Read("Workspace", documents, custom, includeCustomFeatures: true));
        var entry = Assert.Single(catalog, point => point.Kind == "navigation" && point.Key == "workspace");
        Assert.Equal("workspace.spaces.access", entry.BehaviorKey);
        Assert.DoesNotContain(catalog, point => point.Key == "new");
        Assert.Contains("tree:workspace.structure", entry.ReadBehaviors); Assert.Contains("workspace.folder.read", entry.ReadBehaviors);
        var create = Assert.Single(catalog, point => point.BehaviorKey == "workspace.folder.create");
        Assert.Equal("Workspace/navigation:workspace/node-type:folder/surface:list.pageActions/action:create", create.Position);
        Assert.Equal("workspace.folder.create", create.BehaviorKey); Assert.Equal(new[] { entry.Position }, create.EntryDependencies);
        Assert.Throws<FeatureSelectionException>(() => FeatureCatalogCompiler.ValidateSelection(catalog, [create.Position]));
        FeatureCatalogCompiler.ValidateSelection(catalog, [entry.Position, create.Position]);
        foreach (var (key, behavior, surface) in new[] { ("edit", "editObject", "rowActions"), ("delete", "deleteObject", "rowActions"), ("deleteObjects", "deleteObjects", "pageActions") })
        {
            var action = Assert.Single(catalog, point => point.BehaviorKey == "workspace.folder." + behavior && point.ActionScope == "list." + surface);
            Assert.Equal("Workspace/navigation:workspace/node-type:folder/surface:list." + surface + "/action:" + key, action.Position);
            Assert.Contains("workspace.folder.read", action.ReadBehaviors);
        }
        foreach (var (behavior, surface) in new[] { ("add", "custom"), ("remove", "rowActions"), ("createPart", "pageActions"), ("createDocument", "pageActions") })
        {
            var action = Assert.Single(catalog, point => point.BehaviorKey == "workspace.folder." + behavior && point.ActionScope == "list." + surface);
            Assert.Equal("Workspace/navigation:workspace/node-type:folder/surface:list." + surface + "/action:" + behavior, action.Position);
            Assert.Equal(new[] { entry.Position }, action.EntryDependencies);
            Assert.Contains("workspace.folder.read", action.ReadBehaviors);
        }
        foreach (var key in new[] { "edit", "remove" })
        {
            var row = Assert.Single(catalog, point => point.Key == key && point.ActionScope == "list.rowActions");
            var node = Assert.Single(catalog, point => point.Key == key && point.ActionScope == "contextMenu");
            Assert.Equal(row.BehaviorKey, node.BehaviorKey); Assert.Equal(row.ReadBehaviors, node.ReadBehaviors);
            Assert.Equal("Workspace/navigation:workspace/node-type:folder/surface:contextMenu/action:" + key, node.Position);
        }
        foreach (var key in new[] { "moveUp", "moveDown" })
        {
            var node = Assert.Single(catalog, point => point.Key == key && point.ActionScope == "contextMenu");
            Assert.Equal("folder", node.StaticScope); Assert.Equal("workspace.folder.reorder", node.BehaviorKey);
            Assert.Contains("workspace.folder.read", node.ReadBehaviors);
            Assert.DoesNotContain(catalog, point => point.Key == key && point.ActionScope is "list.rowActions" or "list.pageActions");
        }
        foreach (var (key, surface, behavior) in new[] { ("copySelected", "list.pageActions", "copy"), ("copy", "list.rowActions", "copy"),
            ("cutSelected", "list.pageActions", "cut"), ("cut", "list.rowActions", "cut"), ("cut", "contextMenu", "cut"), ("removeSelected", "list.pageActions", "removeSelected"),
            ("copy", "contextMenu", "copy"), ("paste", "list.pageActions", "paste"), ("paste", "contextMenu", "paste"),
            ("clearClipboard", "list.pageActions", "copy"), ("clearClipboard", "contextMenu", "copy"),
            ("pasteFolderCopy", "list.pageActions", "pasteFolderCopy"), ("pasteFolderCopy", "contextMenu", "pasteFolderCopy"), ("pasteFolderStructure", "list.pageActions", "pasteFolderStructure"), ("pasteFolderStructure", "contextMenu", "pasteFolderStructure") })
        {
            var action = Assert.Single(catalog, point => point.ModuleId == "Workspace" && point.StaticScope == "folder" && point.Key == key && point.ActionScope == surface);
            Assert.Equal("workspace.folder." + behavior, action.BehaviorKey);
            Assert.Contains("workspace.folder.read", action.ReadBehaviors);
        }
    }
}
