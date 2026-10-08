using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.Planning;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class LinkQueryAuthoringTests
{
    [Fact]
    public void WorkspaceAuthoring_ClosesTheTypedDirectoryAndRejectsInvalidCandidateAsAWhole()
    {
        var models = new WorkspaceModelMetadataProvider().GetDefinitions(); var link = Assert.Single(models, m => m.LinkQuery is not null);
        Assert.Equal("Workspace/WorkspaceFolder", link.LinkQuery!.Parent.FixedModelKey); Assert.Equal("ChildModelKey", link.LinkQuery.Child.TypeField);
        Assert.Single(Compile(models).Definitions);
        var query = link.LinkQuery;
        var variants = new[]
        {
            link with { LinkedTypes = [.. link.LinkedTypes, link.LinkedTypes[0]] },
            link with { LinkedTypes = [] },
            link with { Kind = ModelKind.Entity },
            link with { LinkQuery = query with { Parent = query.Parent with { TypeField = "ChildModelKey" } } },
            link with { LinkQuery = query with { Child = query.Child with { TypeField = "Unknown" } } },
            link with { LinkQuery = query with { Child = query.Child with { ReferenceField = "PEID" } } },
            link with { Fields = link.Fields.Select(f => f.Name == "ChildModelKey" ? f with { Required = false } : f).ToArray() },
            link with { Fields = link.Fields.Select(f => f.Name == "ChildModelKey" ? f with { OrdinalKey = false } : f).ToArray() },
            link with { LinkQuery = query with { Endpoints = new Dictionary<string, LinkQueryEndpointMetadata> { ["Unknown/Folder"] = new() { ResourceKey = "workspace.folder" } } } },
            link with { LinkQuery = query with { Endpoints = new Dictionary<string, LinkQueryEndpointMetadata> { ["Workspace/WorkspaceFolder"] = new() { ResourceKey = "missing.resource" } } } },
            link with { LinkQuery = query with { Columns = query.Columns.Select(c => c.Field == "Name" ? c with { Mappings = new Dictionary<string, string?> { ["Workspace/WorkspaceFolder"] = "MissingField" } } : c).ToArray() } },
            link with { LinkQuery = query with { Columns = query.Columns.Select(c => c.Field == "Name" ? c with { ValueType = "Instant" } : c).ToArray() } },
            link with { LinkQuery = query with { Columns = query.Columns.Select(c => c.Field == "Name" ? c with { Mappings = new Dictionary<string, string?> { ["Workspace/WorkspaceFolder"] = null } } : c).ToArray() } }
        };
        foreach (var invalid in variants) Assert.Throws<InvalidOperationException>(() => Compile(models.Select(m => m == link ? invalid : m).ToArray()));
        var missingDisplay = models.Select(m => m.Name == "WorkspaceFolder" ? m with { Fields = m.Fields.Select(f => f.Name == "UpdatedAt" ? f with { DisplayRule = null } : f).ToArray() } : m).ToArray();
        Assert.Throws<InvalidOperationException>(() => Compile(missingDisplay));
        Assert.Throws<InvalidOperationException>(() => Compile(models, new("workspace.folder", "WrongOwner", "Workspace/WorkspaceFolder", "EID")));
        Assert.Throws<InvalidOperationException>(() => Compile(models.Select(model => model == link ? link with { LinkQuery = query with
            { Columns = query.Columns.Select(column => column.Field == "Name" ? column with { Field = "AssociatedId" } : column).ToArray() } } : model).ToArray()));
    }

    [Fact]
    public void RuntimeModelProjection_PreservesTheFormalLinkQueryFieldCatalog()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "modules/Workspace/newp.plugin.json"))) root = root.Parent;
        var path = Path.Combine(root!.FullName, "modules/Workspace/metadata/models/WorkspaceFolderContent.model.json");
        var bytes = File.ReadAllBytes(path);
        var projected = new ICP.Platform.Modeling.Projection.ModelRuntimeMetadataProjector().Project(bytes, path);
        using var source = JsonDocument.Parse(bytes); using var runtime = JsonDocument.Parse(projected);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(source.RootElement.GetProperty("WorkspaceFolderContent").GetProperty("linkQuery").GetRawText()),
            System.Text.Json.Nodes.JsonNode.Parse(runtime.RootElement.GetProperty("WorkspaceFolderContent").GetProperty("linkQuery").GetRawText())));
        Assert.Contains(runtime.RootElement.GetProperty("WorkspaceFolderContent").GetProperty("fields").EnumerateArray(), field => field.GetProperty("name").GetString() == "LID");
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    private static LinkQueryCatalog Compile(IReadOnlyList<ModelDefinition> models, DataScopeResource? endpoint = null)
    {
        var plan = new CompiledPersistencePlan("query-test", "snapshot", "hash", default, "digest", [], models, []);
        var module = new CanonicalModulePlanModule("Workspace", "1", "工作空间", null, ".", null, null, null, null, "behavior-bindings",
            ["Identity", "Authentication", "Authorization"], [], [], [], models.Select(m => m.LogicalKey).ToArray(), new("test", "Workspace", "/"))
        { Features = [new("Workspace", "workspace", "navigation", "工作空间", null, "workspace.spaces.access")] };
        var modules = new CanonicalModulePlan("test", "1", "hash", "snapshot", [], [module]);
        return new(plan, new StandardCrudResourceRegistry([], plan), [endpoint ?? new("workspace.folder", "Workspace", "WorkspaceFolder", "EID")], modules);
    }
}
