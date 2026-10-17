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
using ICP.Platform.Crud.Extensions;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.HostApi.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class LinkQueryAuthoringTests
{
    [Fact]
    public void NonPlatformEndpoint_StillRequiresExplicitBusinessDependency()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Compile(
            new WorkspaceModelMetadataProvider().GetDefinitions(), omitBusinessDependency: true));
        Assert.Contains("端点不是可依赖的规范 Entity", error.Message);
    }

    [Fact]
    public void PlatformEndpoint_WithoutExplicitDependencyStillRequiresCorrectResourceOwner()
    {
        var models = PlatformEndpointModels();
        var resource = new DataScopeResource("identity.accounts", "Identity", "IdentityAccount", "EID");
        var definition = Assert.Single(Compile(models, platformEndpoint: resource).Definitions);
        Assert.Equal("Identity", definition.Endpoints["IdentityAccount"].Model.OwnerModuleId);
        var wrong = resource with { ModuleId = "Workspace" };
        var error = Assert.Throws<InvalidOperationException>(() => Compile(models, platformEndpoint: wrong));
        Assert.Contains("端点资源模型或主键不一致", error.Message);
    }

    private static IReadOnlyList<ModelDefinition> PlatformEndpointModels()
    {
        var models = new WorkspaceModelMetadataProvider().GetDefinitions();
        var link = Assert.Single(models, model => model.LinkQuery is not null);
        var query = link.LinkQuery!;
        var identity = new ICP.Platform.Modules.Identity.Persistence.Descriptors.IdentityModelMetadataProvider()
            .GetDefinitions().Single(model => model.Name == "IdentityAccount");
        var types = new[] { "WorkspaceFolder", "IdentityAccount" };
        var adapted = link with
        {
            LinkedTypes = [new ModelMetadataLinkedType { Parent = types[0], Child = types[1] }],
            LinkQuery = query with
            {
                Endpoints = new Dictionary<string, LinkQueryEndpointMetadata>
                {
                    [types[0]] = query.Endpoints[types[0]],
                    [types[1]] = new() { ResourceKey = "identity.accounts", TitleKey = "Identity.Account" }
                },
                Columns = query.Columns.Select(column => column with
                {
                    Mappings = new Dictionary<string, string?>
                    {
                        [types[0]] = column.Mappings[types[0]],
                        [types[1]] = column.Field is "OwnerId" or "OwnerDepartmentId" ? null : column.Mappings["Part"]
                    }
                }).ToArray()
            }
        };
        return [.. models.Select(model => model == link ? adapted : model), identity];
    }

    [Fact]
    public void EndpointDisplay_InvalidPlanIsRejectedBeforeAnyQuery()
    {
        var models = new WorkspaceModelMetadataProvider().GetDefinitions()
            .Select(model => model.Name == "WorkspaceFolder" ? model with
            {
                Fields = model.Fields.Select(field => field.Name == "ENAME" ? field with
                {
                    DisplayRule = new FieldDisplayRuleDefinition("unsupported", "", "", "")
                } : field).ToArray()
            } : model).ToArray();

        var error = Assert.Throws<InvalidOperationException>(() => Compile(models));

        Assert.Contains("MODEL_DISPLAY_TYPE_UNSUPPORTED", error.Message);
        Assert.Contains("ENAME", error.Message);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("wrong-owner")]
    public async Task EndpointConstraints_RejectMissingDuplicateOrWrongModelBeforeMapping(string invalid)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(Compile(new WorkspaceModelMetadataProvider().GetDefinitions()));
        builder.Services.AddSingleton<ICP.Platform.Crud.Abstractions.IStandardCrudResourceRegistry>(new StandardCrudResourceRegistry([]));
        builder.Services.AddSingleton<ILinkQuerySourcePolicy>(new SourcePolicy());
        if (invalid != "missing") builder.Services.AddSingleton<ILinkQueryEndpointConstraint>(new ModelConstraint(invalid == "wrong-owner" ? "Other/Folder" : "WorkspaceFolder"));
        if (invalid == "duplicate") builder.Services.AddSingleton<ILinkQueryEndpointConstraint>(new ModelConstraint("WorkspaceFolder"));
        await using var app = builder.Build();
        Assert.Equal("LINK_QUERY_REQUIRED_POLICY_MISSING_OR_AMBIGUOUS", Assert.Throws<InvalidOperationException>(() => app.MapLinkQueries()).Message);
        Assert.Empty(((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources);
    }

    [Fact]
    public void WorkspaceAuthoring_ClosesTheTypedDirectoryAndRejectsInvalidCandidateAsAWhole()
    {
        var models = new WorkspaceModelMetadataProvider().GetDefinitions(); var link = Assert.Single(models, m => m.LinkQuery is not null);
        Assert.Equal("WorkspaceFolder", link.LinkQuery!.Parent.FixedModelKey); Assert.Equal("ChildModelKey", link.LinkQuery.Child.TypeField);
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
            link with { Fields = link.Fields.Select(f => f.Name == "ChildModelKey" ? f with { IsAuditField = true } : f).ToArray() },
            link with { Fields = link.Fields.Select(f => f.Name == "ChildModelKey" ? f with { IsPrimaryKey = true } : f).ToArray() },
            link with { Fields = link.Fields.Select(f => f.Name == "ChildModelKey" ? f with { Kind = FieldKind.Reference } : f).ToArray() },
            link with { LinkQuery = query with { Endpoints = new Dictionary<string, LinkQueryEndpointMetadata> { ["Unknown/Folder"] = new() { ResourceKey = "workspace.folder" } } } },
            link with { LinkQuery = query with { Endpoints = new Dictionary<string, LinkQueryEndpointMetadata> { ["WorkspaceFolder"] = new() { ResourceKey = "missing.resource" } } } },
            link with { LinkQuery = query with { Columns = query.Columns.Select(c => c.Field == "Name" ? c with { Mappings = new Dictionary<string, string?> { ["WorkspaceFolder"] = "MissingField" } } : c).ToArray() } },
            link with { LinkQuery = query with { Columns = query.Columns.Select(c => c.Field == "Name" ? c with { ValueType = "Instant" } : c).ToArray() } },
            link with { LinkQuery = query with { Columns = query.Columns.Select(c => c.Field == "Name" ? c with { Mappings = new Dictionary<string, string?> { ["WorkspaceFolder"] = null } } : c).ToArray() } }
        };
        foreach (var invalid in variants) Assert.Throws<InvalidOperationException>(() => Compile(models.Select(m => m == link ? invalid : m).ToArray()));
        var missingDisplay = models.Select(m => m.Name == "WorkspaceFolder" ? m with { Fields = m.Fields.Select(f => f.Name == "UpdatedAt" ? f with { DisplayRule = null } : f).ToArray() } : m).ToArray();
        Assert.Throws<InvalidOperationException>(() => Compile(missingDisplay));
        Assert.Throws<InvalidOperationException>(() => Compile(models, new("workspace.folder", "WrongOwner", "WorkspaceFolder", "EID")));
        Assert.Throws<InvalidOperationException>(() => Compile(models.Select(model => model == link ? link with { LinkQuery = query with
            { Columns = query.Columns.Select(column => column.Field == "Name" ? column with { Field = "AssociatedId" } : column).ToArray() } } : model).ToArray()));
    }

    [Fact]
    public void GenericQuery_AcceptsTwoDiscriminatedEndsAndDoesNotForbidParallelRelations()
    {
        var models = new WorkspaceModelMetadataProvider().GetDefinitions();
        var link = Assert.Single(models, model => model.LinkQuery is not null);
        var discriminator = Assert.Single(link.Fields, field => field.Name == "ChildModelKey");
        var generic = link with { Fields = [.. link.Fields, discriminator with { Name = "ParentType", StoreName = "ParentType" }],
            LinkQuery = link.LinkQuery! with { Parent = link.LinkQuery.Parent with { FixedModelKey = null, TypeField = "ParentType" } },
            Indexes = link.Indexes.Where(index => !index.IsUnique).ToArray() };
        var definition = Assert.Single(Compile(models.Select(model => model == link ? generic : model).ToArray()).Definitions);
        Assert.Null(definition.Metadata.Parent.FixedModelKey);
        Assert.Equal(3, definition.Model.LinkedTypes.Count);
        var fixedChild = generic with { LinkedTypes = [link.LinkedTypes[0]], LinkQuery = generic.LinkQuery with {
            Child = generic.LinkQuery.Child with { TypeField = null, FixedModelKey = "WorkspaceFolder" },
            Endpoints = generic.LinkQuery.Endpoints.Where(pair => pair.Key == "WorkspaceFolder").ToDictionary(),
            Columns = generic.LinkQuery.Columns.Select(column => column with { Mappings = column.Mappings.Where(pair => pair.Key == "WorkspaceFolder").ToDictionary() }).ToArray() } };
        Assert.Single(Compile(models.Select(model => model == link ? fixedChild : model).ToArray()).Definitions);
    }

    [Fact]
    public void RuntimeModelProjection_PreservesTheFormalLinkQueryFieldCatalog()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root.FullName, "Workspace"), "newp.plugin.json"))) root = root.Parent;
        var path = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root!.FullName, "Workspace"), "metadata/models/WorkspaceFolderContent.model.json");
        var bytes = File.ReadAllBytes(path);
        var projected = new ICP.Platform.Modeling.Projection.ModelRuntimeMetadataProjector().Project(bytes, path);
        using var source = JsonDocument.Parse(bytes); using var runtime = JsonDocument.Parse(projected);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(source.RootElement.GetProperty("WorkspaceFolderContent").GetProperty("linkQuery").GetRawText()),
            System.Text.Json.Nodes.JsonNode.Parse(runtime.RootElement.GetProperty("WorkspaceFolderContent").GetProperty("linkQuery").GetRawText())));
        Assert.Contains(runtime.RootElement.GetProperty("WorkspaceFolderContent").GetProperty("fields").EnumerateArray(), field => field.GetProperty("name").GetString() == "LID");
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    private static LinkQueryCatalog Compile(IReadOnlyList<ModelDefinition> models, DataScopeResource? endpoint = null,
        DataScopeResource? platformEndpoint = null, bool omitBusinessDependency = false)
    {
        models = models.Concat(new ICP.Platform.Modules.Part.PartModelMetadataProvider().GetDefinitions())
            .Concat(new ICP.Platform.Modules.Document.DocumentModelMetadataProvider().GetDefinitions())
            .Concat(new ICP.Platform.Modules.Organization.Persistence.Descriptors.OrganizationModelMetadataProvider().GetDefinitions()).ToArray();
        var plan = new CompiledPersistencePlan("query-test", "snapshot", "hash", default, "digest", [], models, []);
        var source = TrustedBuiltInModuleSource.CreateForHostDistribution(AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "modules"));
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(source.ModulesRoot).Select(candidate =>
            omitBusinessDependency && candidate.ModuleId == "Workspace" ? candidate with { Dependencies = [] } : candidate);
        var compilation = new CanonicalModulePlanCompiler().Compile("link-test", candidates, source.PlatformCompositionModuleId);
        Assert.True(compilation.IsSuccess, string.Join(Environment.NewLine, compilation.Diagnostics));
        var resources = new List<DataScopeResource> { endpoint ?? new("workspace.folder", "Workspace", "WorkspaceFolder", "EID"),
            new("part.part", "Part", "Part", "EID"), new("document.document", "Document", "Document", "EID") };
        if (platformEndpoint is not null) resources.Add(platformEndpoint);
        return new(plan, new StandardCrudResourceRegistry([], plan), resources, compilation.Plan);
    }

    private sealed class SourcePolicy : ILinkQuerySourcePolicy
    {
        public string Key => "workspace.content";
        public Task ValidateAsync(LinkQueryDefinition definition, LinkQueryRequest request, CancellationToken token) => Task.CompletedTask;
        public Task<Expression<Func<Dictionary<string, object?>, bool>>?> ReadPredicateAsync(CancellationToken token) => Task.FromResult<Expression<Func<Dictionary<string, object?>, bool>>?>(null);
    }
    private sealed class ModelConstraint(string model) : ILinkQueryEndpointConstraint
    {
        public string Key => "workspace.folder.scope";
        public string ModelKey => model;
        public Task<Expression<Func<Dictionary<string, object?>, bool>>> ReadPredicateAsync(LinkQueryEndpoint endpoint, CancellationToken token) => Task.FromResult<Expression<Func<Dictionary<string, object?>, bool>>>(row => true);
    }
}
