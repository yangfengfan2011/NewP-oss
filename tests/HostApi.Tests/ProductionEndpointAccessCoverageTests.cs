using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.HostApi.Authorization;
using ICP.Platform.HostApi.Controllers;
using ICP.Platform.HostApi.Endpoints;
using ICP.Platform.Kernel.ModuleLoader;
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
        var builder = WebApplication.CreateBuilder();
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
        builder.Services.AddStandardCrudResource(ExampleRecordCrudDescriptorFactory.Create());
        builder.Services.AddStandardCrudResource(ICP.Platform.Modules.Part.PartCrudDescriptor.Create());
        builder.Services.AddStandardCrudResource(ICP.Platform.Modules.Document.DocumentCrudDescriptor.Create());
        using var app = builder.Build();
        app.MapControllers();
        app.MapStandardCrudResources();
        app.MapPluginManifestEndpoints("test-plugin-root");

        EndpointAccessPlanCompiler.Compile(((IEndpointRouteBuilder)app).DataSources);
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
        var declared = ICP.Platform.Kernel.ModulePlanning.FeatureCatalogCompiler.Compile(
            ICP.Platform.Kernel.ModulePlanning.StandardFeatureMetadataCompiler.Read("Document", documents,
                ICP.Platform.Kernel.ModulePlanning.FeatureCatalogCompiler.Read(capsule.RootElement, "Document"), true));
        var expectedFeatures = declared.Select(point => point.BehaviorKey).Concat(declared.SelectMany(point => point.ReadBehaviors))
            .Where(key => key.StartsWith("document.document.", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
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
