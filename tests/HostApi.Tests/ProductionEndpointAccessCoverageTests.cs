using ICP.Platform.Testing.Authorization;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.HostApi.Authorization;
using ICP.Platform.HostApi.Controllers;
using ICP.Platform.HostApi.Endpoints;
using ICP.Platform.Kernel.ModuleLoader;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Audit;
using ICP.Platform.Modules.Audit.Controllers;
using ICP.Platform.Modules.Authorization;
using ICP.Platform.Modules.Authorization.Controllers;
using ICP.Platform.Modules.ConfigCenter;
using ICP.Platform.Modules.ConfigCenter.Controllers;
using ICP.Platform.Modules.Demo;
using ICP.Platform.Modules.Example;
using ICP.Platform.Modules.Example.Crud;
using ICP.Platform.Modules.Files;
using ICP.Platform.Modules.Files.Controllers;
using ICP.Platform.Modules.MetadataConfigCenter;
using ICP.Platform.Modules.MetadataConfigCenter.Controllers;
using ICP.Platform.Modules.Identity;
using ICP.Platform.Modules.Identity.Controllers;
using ICP.Platform.Modules.ModuleTemplateSample;
using ICP.Platform.Modules.ModuleTemplateSample.Controllers;
using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Sdk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class ProductionEndpointAccessCoverageTests
{
    [Fact]
    public void WorkspaceSourceProperties_RequiresTheWorkspaceReadFeatureOnItsExactReadOnlyRoute()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(ICP.Platform.Modules.Workspace.WorkspaceContentsController).Assembly);
        using var app = builder.Build();
        app.MapControllers();
        foreach (var path in new[] { "properties", "permissions/catalog", "permissions/subjects/user" })
        {
            var endpoint = Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(),
                endpoint => endpoint.RoutePattern.RawText?.TrimStart('/') == "api/workspace/content/{linkId}/" + path);
            Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
            Assert.Equal("workspace.folder.read", Assert.Single(endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>()).BehaviorKey);
        }
        EndpointAccessPlanCompiler.Compile(((IEndpointRouteBuilder)app).DataSources);
    }

    [Fact, Trait("Category", "ModuleMetadataConsumer")]
    public void WorkspaceHistoryTerminals_ConsumeTheOneCompiledHistoryPositionWithoutExtraGrants()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var root = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, "Workspace");
        using var capsule = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "newp.plugin.json")));
        var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(File.ReadAllBytes(path))).ToArray();
        var points = RuntimeFeatureCatalogTestSupport.CompileWithParents(StandardFeatureMetadataCompiler.Read("Workspace", documents,
            FeatureCatalogCompiler.Read(capsule.RootElement, "Workspace"), includeCustomFeatures: true));
        var history = Assert.Single(points, point => point.BehaviorKey == "workspace.folder.history");
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(typeof(ICP.Platform.Modules.Workspace.WorkspaceContentsController).Assembly);
        using var app = builder.Build();
        app.MapControllers();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/').StartsWith("api/workspace/content/{linkId}/history", StringComparison.Ordinal) == true).ToArray();
        var list = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText!.TrimStart('/') == "api/workspace/content/{linkId}/history");
        var detail = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText!.TrimStart('/') == "api/workspace/content/{linkId}/history/{recordId}");
        Assert.Equal("workspace.history.read", Assert.Single(list.Metadata.GetOrderedMetadata<RequireFeatureAttribute>()).BehaviorKey);
        Assert.Equal("workspace.folder.history", Assert.Single(detail.Metadata.GetOrderedMetadata<RequireFeatureAttribute>()).BehaviorKey);
        foreach (var suffix in new[] { "export", "print" })
        {
            var terminal = Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText!.TrimStart('/') == "api/workspace/content/{linkId}/history/" + suffix
                && endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods.Contains("POST"));
            Assert.Equal("workspace.folder.history", Assert.Single(terminal.Metadata.GetOrderedMetadata<RequireFeatureAttribute>()).BehaviorKey);
        }
        FeatureEndpointBindings.Validate([new DefaultEndpointDataSource(endpoints)], [history]);
    }

    [Theory, Trait("Category", "ModuleMetadataConsumer")]
    [InlineData("Part", "part", "parts")]
    [InlineData("Document", "document", "documents")]
    public void IndependentHistoryTerminals_UseDeclaredOwnerHistoryAndOneReadAlias(string module, string slug, string collection)
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var root = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, module);
        using var capsule = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "newp.plugin.json")));
        var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(File.ReadAllBytes(path))).ToArray();
        var declarations = StandardFeatureMetadataCompiler.Read(module, documents, FeatureCatalogCompiler.Read(capsule.RootElement, module), includeCustomFeatures: true);
        var history = Assert.Single(RuntimeFeatureCatalogTestSupport.CompileWithParents(declarations), point => point.BehaviorKey == slug + "." + slug + ".history");
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers().AddApplicationPart(module == "Part" ? typeof(ICP.Platform.Modules.Part.PartModule).Assembly : typeof(ICP.Platform.Modules.Document.DocumentModule).Assembly);
        using var app = builder.Build();
        app.MapControllers();
        var prefix = "api/" + slug + "/" + collection + "/{objectId}/history";
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/').StartsWith(prefix, StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(5, endpoints.Length);
        var check = Assert.Single(endpoints, item => item.RoutePattern.RawText?.TrimStart('/') == prefix + "/check/{operation}"
            && item.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods.Contains("GET"));
        Assert.Equal(slug + "." + slug + ".history", Assert.Single(check.Metadata.GetOrderedMetadata<RequireFeatureAttribute>()).BehaviorKey);
        foreach (var (suffix, method) in new[] { ("", "GET"), ("/{recordId}", "GET"), ("/export", "POST"), ("/print", "POST") })
        {
            var endpoint = Assert.Single(endpoints, item => item.RoutePattern.RawText?.TrimStart('/') == prefix + suffix
                && item.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods.Contains(method));
            Assert.Equal(slug + (suffix == "" ? ".history.read" : "." + slug + ".history"), Assert.Single(endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>()).BehaviorKey);
        }
        FeatureEndpointBindings.Validate([new DefaultEndpointDataSource(endpoints)], [history]);
        EndpointAccessPlanCompiler.Compile(((IEndpointRouteBuilder)app).DataSources);
    }

    [Theory]
    [InlineData("Part", "part", "parts", "part.part")]
    [InlineData("Document", "document", "documents", "document.document")]
    public void IndependentPermissionOwners_SeparateBrowseAndManagementOnEveryActualRoute(string moduleId, string modulePath, string collection, string resourceKey)
    {
        var builder = WebApplication.CreateBuilder();
        var assembly = moduleId == "Part" ? typeof(ICP.Platform.Modules.Part.PartModule).Assembly : typeof(ICP.Platform.Modules.Document.DocumentModule).Assembly;
        builder.Services.AddControllers().AddApplicationPart(assembly);
        using var app = builder.Build();
        app.MapControllers();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        var prefix = "api/" + modulePath + "/" + collection + "/{objectId}/permissions";
        foreach (var (suffix, method, action) in new[] { ("/catalog", "GET", "read"), ("/explanation", "GET", "read"),
            ("/subjects/user", "GET", "read"), ("/subjects/{type}", "GET", "authorize"), ("/eligibility", "GET", "authorize"),
            ("", "GET", "authorize"), ("", "PUT", "authorize") })
        {
            var endpoint = Assert.Single(endpoints, item => item.RoutePattern.RawText?.TrimStart('/') == prefix + suffix
                && item.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods.Contains(method));
            Assert.Equal(resourceKey + "." + action, Assert.Single(endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>()).BehaviorKey);
            Assert.Equal(moduleId, Assert.Single(endpoint.Metadata.GetOrderedMetadata<EndpointModuleAttribute>()).ModuleId);
        }
        EndpointAccessPlanCompiler.Compile(((IEndpointRouteBuilder)app).DataSources);
    }

    [Fact, Trait("Category", "ModuleMetadataConsumer")]
    public void Compile_AllCurrentProductionControllersPersonCrudAndPluginManifests_HaveTerminalRules()
    {
        var compiled = new CanonicalModulePlanCompiler().Compile("production-endpoint-coverage",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compiled.IsSuccess, System.Text.Json.JsonSerializer.Serialize(compiled.Diagnostics));
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(compiled.Plan!);
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly)
            .AddApplicationPart(typeof(AccessLogsController).Assembly)
            .AddApplicationPart(typeof(RoleController).Assembly)
            .AddApplicationPart(typeof(ConfigController).Assembly)
            .AddApplicationPart(typeof(FilesController).Assembly)
            .AddApplicationPart(typeof(MetadataModulesController).Assembly)
            .AddApplicationPart(typeof(SampleStatusController).Assembly)
            .AddApplicationPart(typeof(IdentityAccountsController).Assembly)
            .AddApplicationPart(typeof(PersonOrganizationLookupsController).Assembly)
            .AddApplicationPart(typeof(ICP.Platform.Modules.Workspace.WorkspaceSpacesController).Assembly)
            .AddApplicationPart(typeof(ICP.Platform.Modules.Document.DocumentFilesController).Assembly)
            .AddApplicationPart(typeof(ExampleModule).Assembly);
        builder.Services.AddStandardCrudKernel();
        builder.Services.AddStandardCrudResource(PersonCrudDescriptorFactory.Create());
        builder.Services.AddStandardCrudResource(CompanyCrudDescriptorFactory.Create());
        builder.Services.AddStandardCrudResource(DepartmentCrudDescriptorFactory.Create());
        builder.Services.AddStandardCrudResource(PositionCrudDescriptorFactory.Create());
        builder.Services.AddStandardCrudResource(ICP.Platform.Modules.Identity.Application.IdentityAccountCrudResource.Create());
        builder.Services.AddStandardCrudResource(ICP.Platform.Modules.Authorization.Services.RoleReadResource.Create());
        builder.Services.AddStandardCrudResource(ExampleRecordCrudDescriptorFactory.Create());
        builder.Services.AddStandardCrudResource(ICP.Platform.Modules.Part.PartCrudDescriptor.Create());
        builder.Services.AddStandardCrudResource(ICP.Platform.Modules.Document.DocumentCrudDescriptor.Create());
        using var app = builder.Build();
        app.MapControllers();
        app.MapStandardCrudResources();
        app.MapPluginManifestEndpoints("test-plugin-root");

        EndpointAccessPlanCompiler.Compile(((IEndpointRouteBuilder)app).DataSources);
        var actualEndpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules"));
        var assemblies = candidates.Where(candidate => candidate.BackendEntry is not null).Select(candidate =>
            System.Reflection.Assembly.LoadFrom(Path.GetFullPath(Path.Combine(candidate.PackageRoot, candidate.BackendEntry!))))
            .Append(typeof(ICP.Platform.Crud.Runtime.StandardCrudService).Assembly).Distinct().ToArray();
        foreach (var module in compiled.Plan!.Modules)
        foreach (var execution in module.DataExecutions)
        {
            if (execution.Boundary == "entry")
                Assert.Contains(compiled.Plan.ActionDataAccessCatalog, declaration => declaration.Execution == execution.Key);
            foreach (var binding in execution.FixedBindings)
            {
                Assert.Equal(execution.Boundary, binding.Kind);
                if (binding.Kind == "api")
                {
                    var endpoint = Assert.Single(actualEndpoints, endpoint =>
                        endpoint.RoutePattern.RawText?.TrimStart('/') == binding.Route?.TrimStart('/')
                        && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(binding.HttpMethod!));
                    Assert.Equal(module.ModuleId, endpoint.Metadata.GetMetadata<EndpointModuleAttribute>()!.ModuleId);
                }
                else if (binding.Kind == "internal")
                {
                    var identity = binding.Caller!;
                    var types = assemblies.Select(assembly => assembly.GetType(identity)).OfType<Type>().Distinct().ToArray();
                    if (types.Length > 0) { Assert.Single(types); continue; }
                    var split = identity.LastIndexOf('.');
                    var type = Assert.Single(assemblies.Select(assembly => assembly.GetType(identity[..split])).OfType<Type>().Distinct());
                    Assert.Contains(type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance), method => method.Name == identity[(split + 1)..]);
                }
                else Assert.Fail("Unknown fixed source: " + binding.Kind);
            }
        }

        var partEndpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/part/parts", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(new[] { "part.part.create", "part.part.read" }, partEndpoints.SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>())
            .Select(feature => feature.BehaviorKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.All(partEndpoints.Where(endpoint => endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods.Any(method => method is "PUT" or "DELETE")),
            endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<DenyAllAttribute>()));
        var documentEndpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/').StartsWith("api/document/documents", StringComparison.Ordinal) == true).ToArray();
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var documentRoot = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, "Document");
        using var capsule = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(documentRoot, "newp.plugin.json")));
        var documents = Directory.GetFiles(Path.Combine(documentRoot, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(File.ReadAllBytes(path))).ToArray();
        var declared = RuntimeFeatureCatalogTestSupport.CompileWithParents(
            ICP.Platform.Kernel.ModulePlanning.StandardFeatureMetadataCompiler.Read("Document", documents,
                ICP.Platform.Kernel.ModulePlanning.FeatureCatalogCompiler.Read(capsule.RootElement, "Document"), true));
        var expectedFeatures = declared.Select(point => point.BehaviorKey).Concat(declared.SelectMany(point => point.ReadBehaviors))
            .Where(key => key.StartsWith("document.", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        Assert.Equal(expectedFeatures, documentEndpoints.SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>())
            .Select(feature => feature.BehaviorKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        var documentFileEndpoints = documentEndpoints.Where(endpoint => endpoint.RoutePattern.RawText!.Contains("/files", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { "api/document/documents/{documentId}/files", "api/document/documents/{documentId}/files/{fileLinkId}/content" },
            documentFileEndpoints.Select(endpoint => endpoint.RoutePattern.RawText!.TrimStart('/')).Order(StringComparer.Ordinal));
        Assert.All(documentFileEndpoints, endpoint =>
        {
            Assert.Equal(new[] { "GET" }, endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
            Assert.Equal("Document", Assert.Single(endpoint.Metadata.GetOrderedMetadata<EndpointModuleAttribute>()).ModuleId);
            Assert.Equal(endpoint.RoutePattern.RawText!.EndsWith("/content", StringComparison.Ordinal) ? "document.document.download" : "document.document.read",
                Assert.Single(endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>()).BehaviorKey);
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        });
        Assert.Contains(documentEndpoints, endpoint => endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods.Contains("PUT"));
        Assert.Contains(documentEndpoints, endpoint => endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods.Contains("DELETE"));
        // 标准业务CRUD仍关闭；owner的受控归属/授权写入由各自明确功能规则覆盖。
        Assert.All(documentEndpoints.Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith("/", StringComparison.Ordinal)
            && endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods.Any(method => method is "PUT" or "DELETE")),
            endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<DenyAllAttribute>()));
        var manifestEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/plugins/",
                StringComparison.Ordinal) == true)
            .ToArray();
        var exampleRecordEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/api/example/records",
                StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(9, exampleRecordEndpoints.Length);
        Assert.All(
            exampleRecordEndpoints,
            static endpoint =>
            {
                Assert.Equal(
                    "Example",
                    Assert.Single(endpoint.Metadata.GetOrderedMetadata<EndpointModuleAttribute>()).ModuleId);
                Assert.Equal(1, endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>().Count
                    + endpoint.Metadata.GetOrderedMetadata<DenyAllAttribute>().Count);
            });
        Assert.Equal(
            ["example.record.create", "example.record.delete", "example.record.export", "example.record.read", "example.record.update"],
            exampleRecordEndpoints.SelectMany(static endpoint => endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>())
                .Select(static feature => feature.BehaviorKey).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Equal(4, manifestEndpoints.Length);
        Assert.Contains(
            manifestEndpoints,
            static endpoint => string.Equals(
                endpoint.RoutePattern.RawText,
                "/plugins/active-module-runtime.json",
                StringComparison.Ordinal));
        Assert.All(
            manifestEndpoints,
            static endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>()));
    }
}
