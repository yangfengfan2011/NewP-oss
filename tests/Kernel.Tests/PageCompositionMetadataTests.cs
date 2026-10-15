using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Kernel.PluginDiscovery;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class PageCompositionMetadataTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("empty-column")]
    [InlineData("unknown-model")]
    [InlineData("missing-model")]
    [InlineData("missing-action")]
    [InlineData("duplicate-action")]
    [InlineData("missing-surface")]
    [InlineData("conflict")]
    [InlineData("presentation")]
    public void WorkspaceActionSets_ValidateModelsAndIndependentSurfaces(string kind)
    {
        var root = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(RepositoryRoot(), "Workspace");
        var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!).ToArray();
        var list = documents.Single(document => document["view"]?["key"]?.GetValue<string>() == "workspace.folder.list")["view"]!;
        var sets = list["rowActionSets"]!.AsObject();
        switch (kind)
        {
            case "empty-column": sets["Part"]!["column"] = new JsonArray(); break;
            case "unknown-model": sets["Unknown"] = sets["Part"]!.DeepClone(); break;
            case "missing-model": sets.Remove("Part"); break;
            case "missing-action": sets["Part"]!["column"] = JsonNode.Parse("[\"missing\"]"); break;
            case "duplicate-action": sets["Part"]!["contextMenu"] = JsonNode.Parse("[\"view\",\"view\"]"); break;
            case "missing-surface": sets["Part"]!.AsObject().Remove("column"); break;
            case "conflict": list["rowContextMenu"] = new JsonArray(); break;
            case "presentation": list["rowActionColumn"] = "automatic"; break;
        }
        IReadOnlyList<string> Check() => PageCompositionMetadataValidator.Validate(documents.Select(document => JsonSerializer.SerializeToElement(document)), null);
        if (kind is "valid" or "empty-column") Check();
        else Assert.Throws<JsonException>(() => Check());
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("page-action")]
    [InlineData("inline-definition")]
    [InlineData("not-array")]
    public void WorkspaceRowContextMenu_ValidatesRealAuthoringReferences(string kind)
    {
        var root = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(RepositoryRoot(), "Workspace");
        var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!).ToArray();
        var list = documents.Single(document => document["view"]?["key"]?.GetValue<string>() == "workspace.folder.list")["view"]!;
        list.AsObject().Remove("rowActionSets");
        list["rowContextMenu"] = kind switch
        {
            "missing" => JsonNode.Parse("""["missing"]"""),
            "duplicate" => JsonNode.Parse("""["edit","edit"]"""),
            "page-action" => JsonNode.Parse("""["create"]"""),
            "inline-definition" => JsonNode.Parse("""[{"key":"edit","handlerKey":"other"}]"""),
            "not-array" => JsonValue.Create("edit"),
            _ => JsonNode.Parse("""["edit","delete","remove"]""")
        };
        IReadOnlyList<string> Check() => PageCompositionMetadataValidator.Validate(documents.Select(document => JsonSerializer.SerializeToElement(document)), null);
        if (kind == "valid") Check();
        else Assert.Throws<JsonException>(() => Check());
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("duplicate-key")]
    [InlineData("duplicate-target")]
    [InlineData("navigation")]
    [InlineData("page")]
    [InlineData("record-id")]
    [InlineData("binding")]
    public void WorkspaceDetailContributions_ValidateOwnedAuthoringBeforeStaging(string kind)
    {
        var root = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(RepositoryRoot(), "Workspace");
        var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories).Select(path => JsonNode.Parse(File.ReadAllText(path))!).ToArray();
        var bindings = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "newp.plugin.json")))!["frontend"]!["bindings"]!.AsArray().Select(value => value!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var entries = documents.Single(document => document["view"]?["key"]?.GetValue<string>() == "workspace.references.list")["view"]!["detailContributions"]!.AsArray();
        if (kind == "duplicate-key") entries.Add(entries[0]!.DeepClone());
        if (kind == "duplicate-target") { var copy = entries[0]!.DeepClone(); copy["key"] = "another-key"; entries.Add(copy); }
        if (kind == "navigation") entries[0]!["navigationKey"] = "part.parts";
        if (kind == "page") entries[0]!["pageRef"]!["parameters"]!["viewKey"] = "workspace.folder.list";
        if (kind == "record-id") entries[0]!["pageRef"]!["parameters"]!["recordId"] = "spoofed";
        if (kind == "binding") bindings.Remove("workspace.references.object");
        IReadOnlyList<string> ValidateWorkspace() => PageCompositionMetadataValidator.Validate(documents.Select(document => JsonSerializer.SerializeToElement(document)), bindings);
        if (kind == "valid") { Assert.Contains("workspace.references.object", ValidateWorkspace()); Assert.Contains("workspace.references.list", ValidateWorkspace()); }
        else Assert.Throws<JsonException>(() => ValidateWorkspace());
    }
    [Fact]
    public void Document_RuntimeModelMatchesTheSharedCreateAndDetailConsumerFixture()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root, "Document"), "metadata/models/Document.model.json");
        var authoring = File.ReadAllBytes(path);
        var projected = new ICP.Platform.Modeling.Projection.ModelRuntimeMetadataProjector().Project(authoring, path);
        var fixture = File.ReadAllBytes(Path.Combine(root, "tests/Kernel.Tests/Fixtures/DocumentRuntime.contract.json"));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(projected), JsonNode.Parse(fixture)));
        Assert.Equal(authoring, File.ReadAllBytes(path));
    }

    [Fact]
    public void Part_RuntimeModelMatchesTheSharedCreateAndDetailConsumerFixture()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root, "Part"), "metadata/models/Part.model.json");
        var authoring = File.ReadAllBytes(path);
        var projected = new ICP.Platform.Modeling.Projection.ModelRuntimeMetadataProjector().Project(authoring, path);
        var fixture = File.ReadAllBytes(Path.Combine(root, "tests/Kernel.Tests/Fixtures/PartRuntime.contract.json"));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(projected), JsonNode.Parse(fixture)));
        Assert.Equal(authoring, File.ReadAllBytes(path));
    }

    [Fact]
    public void WorkspaceFolder_RuntimeModelMatchesTheSharedDetailConsumerFixture()
    {
        var root = RepositoryRoot();
        var path = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root, "Workspace"), "metadata/models/WorkspaceFolder.model.json");
        var authoring = File.ReadAllBytes(path);
        var projected = new ICP.Platform.Modeling.Projection.ModelRuntimeMetadataProjector().Project(authoring, path);
        var fixture = File.ReadAllBytes(Path.Combine(root, "tests/Kernel.Tests/Fixtures/WorkspaceFolderRuntime.contract.json"));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(projected), JsonNode.Parse(fixture)));
        Assert.Equal(authoring, File.ReadAllBytes(path));
    }

    [Fact]
    public void TreeSearch_CanBeExplicitlyDisabledButMustRemainBoolean()
    {
        var documents = ReadDocuments();
        var tree = documents.Single(document => document["view"]?["key"]?.GetValue<string>() == "organization.structure")["view"]!;
        Validate(documents);
        tree["searchEnabled"] = false;
        Validate(documents);
        tree["searchEnabled"] = "false";
        Assert.Throws<JsonException>(() => Validate(documents));
    }

    [Fact]
    public void OrganizationTreeActions_CompileAndStageTheSharedRuntimeContract()
    {
        var repository = RepositoryRoot();
        var temporary = Directory.CreateTempSubdirectory("NewPTreeActions_");
        try
        {
            var modulesRoot = Directory.CreateDirectory(Path.Combine(temporary.FullName, "modules")).FullName;
            var moduleRoot = Directory.CreateDirectory(Path.Combine(modulesRoot, "Organization")).FullName;
            var sourceRoot = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository, "Organization");
            foreach (var file in Directory.GetFiles(Path.Combine(sourceRoot, "metadata"), "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(moduleRoot, Path.GetRelativePath(sourceRoot, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(sourceRoot, "newp.plugin.json")))!;
            manifest["dependencies"] = new JsonArray();
            File.WriteAllText(Path.Combine(moduleRoot, "plugin.json"), manifest.ToJsonString());
            // 可信候选签发会读取 PE 引用；使用当前真实模块，不以文本冒充程序集。
            File.Copy(typeof(ICP.Platform.Modules.Organization.OrganizationModule).Assembly.Location,
                Path.Combine(temporary.FullName, "OrganizationModule.dll"));
            var treePath = Path.Combine(moduleRoot, "metadata/views/OrganizationStructure.view.json");
            var tree = JsonNode.Parse(File.ReadAllText(treePath))!;
            tree["view"]!["treeActions"]!.AsArray().Add(JsonNode.Parse("""{"key":"secondCreate","labelKey":"Department","viewKey":"organization.department.list","actionKey":"create"}"""));
            foreach (var node in tree["view"]!["nodeTypes"]!.AsArray())
            {
                var action = node!["contextMenu"]!.AsArray().Single(item => item!["standardAction"]!.GetValue<string>() == "delete")!.AsObject();
                action.Remove("standardAction"); action["actionKey"] = "delete";
            }
            File.WriteAllText(treePath, tree.ToJsonString());
            var source = TrustedBuiltInModuleSource.CreateForHost(temporary.FullName, modulesRoot);
            var candidate = Assert.Single(new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(source));
            var compiler = new CanonicalModulePlanCompiler();
            var plan = Assert.IsType<CanonicalModulePlan>(compiler.Compile("tree-actions", [candidate]).Plan);
            Assert.Null(compiler.Compile("missing-resource-binding", [candidate with { FrontendBindings = candidate.FrontendBindings.Where(key => key != "organization.department.list").ToArray() }]).Plan);
            var install = Path.Combine(temporary.FullName, "install");
            var materializer = new RuntimeViewMaterializer(new InstalledPluginStateStore(install), new FrontendRuntimeManifestBuilder(), source);
            using var stage = materializer.Stage(install, plan);
            var runtime = JsonNode.Parse(File.ReadAllText(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(stage.StagedRuntimeRoot, "Organization"), "metadata/views/OrganizationStructure.view.json")));
            var contract = JsonNode.Parse(File.ReadAllText(Path.Combine(repository, "tests/Kernel.Tests/Fixtures/OrganizationTreeActions.contract.json")));
            Assert.True(JsonNode.DeepEquals(contract, runtime));
        }
        finally { temporary.Delete(true); }
    }

    [Theory]
    [InlineData("missing-action")]
    [InlineData("duplicate")]
    [InlineData("both")]
    public void TreeActions_RejectInvalidStableReferences(string invalid)
    {
        var documents = ReadDocuments();
        var tree = documents.Single(document => document["view"]?["key"]?.GetValue<string>() == "organization.structure")["view"]!;
        tree["treeActions"] = JsonNode.Parse("""[{"key":"new","labelKey":"CreateCompany","viewKey":"organization.company.list","actionKey":"create"}]""");
        var actions = tree["treeActions"]!.AsArray();
        if (invalid == "missing-action") actions[0]!["actionKey"] = "missing";
        if (invalid == "duplicate") actions.Add(actions[0]!.DeepClone());
        if (invalid == "both") actions[0]!["standardAction"] = "create";
        Assert.Throws<JsonException>(() => Validate(documents));
    }

    [Fact]
    public void RealOrganizationAuthoring_UsesCanonicalReaderWithoutViewPageIdentities()
    {
        var root = RepositoryRoot();
        var source = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root, "Organization");
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
            Assert.DoesNotContain("organization.tree", candidate.FrontendBindingReferences);
            Assert.DoesNotContain(RelationTreeMetadata.ProviderKey, candidate.FrontendBindingReferences);
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
    [Theory]
    [InlineData("createView")]
    [InlineData("action")]
    [InlineData("label")]
    public void TreeMaintenance_RejectsInvalidActionReferences(string invalid)
    {
        var documents = ReadDocuments();
        var tree = documents.Single(document => document["view"]?["key"]?.GetValue<string>() == "organization.structure")["view"]!;
        if (invalid == "createView") tree["treeActions"]![0]!["viewKey"] = "organization.company.detail";
        else if (invalid == "action") tree["nodeTypes"]![0]!["contextMenu"]![0]!["standardAction"] = "script";
        else tree["treeActions"]![0]!["labelKey"] = "Missing";
        Assert.Throws<JsonException>(() => Validate(documents));
    }
    private static void Validate(JsonNode[] documents) => PageCompositionMetadataValidator.Validate(
        documents.Select(document => JsonSerializer.Deserialize<JsonElement>(document.ToJsonString())), null);
    private static JsonNode[] ReadDocuments() => Directory.GetFiles(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(RepositoryRoot(), "Organization"), "metadata"), "*.json", SearchOption.AllDirectories)
        .Select(path => JsonNode.Parse(File.ReadAllText(path))!).ToArray();
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(directory.FullName, "Organization"), "newp.plugin.json"))) directory = directory.Parent;
        return directory!.FullName;
    }
}
