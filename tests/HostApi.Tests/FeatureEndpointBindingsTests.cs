using ICP.Platform.Authorization.Http;
using ICP.Platform.HostApi.Authorization;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Reflection;
using System.Text.Json;
using ICP.Platform.Modules.Workspace;
using Xunit;
namespace ICP.Platform.HostApi.Tests;

public sealed class FeatureEndpointBindingsTests
{
    [Fact]
    public void WorkspaceTreeActions_CompileReferencedHandlersAndBindToTheirRealControllerEndpoints()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var root = ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository.FullName, "Workspace");
        var documents = Directory.GetFiles(Path.Combine(root, "metadata"), "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path))).ToArray();
        var capsule = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(root, "newp.plugin.json")));
        var custom = FeatureCatalogCompiler.Read(capsule, "Workspace");
        var points = FeatureCatalogCompiler.Compile(StandardFeatureMetadataCompiler.Read("Workspace", documents, custom, true));
        // 使用真实控制器的模块、功能及 HTTP 方法元数据，复用正式绑定校验而不启动数据库或另造 Host。
        var endpoints = typeof(WorkspaceContentsController).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(ControllerBase)))
            .SelectMany(type => type.GetMethods().Where(method => method.GetCustomAttribute<RequireFeatureAttribute>() is not null)
                .Select(method => new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(
                    type.GetCustomAttribute<EndpointModuleAttribute>()!, method.GetCustomAttribute<RequireFeatureAttribute>()!,
                    new HttpMethodMetadata(method.GetCustomAttributes<HttpMethodAttribute>().SelectMany(attribute => attribute.HttpMethods))),
                    type.Name + "." + method.Name))).ToArray();
        FeatureEndpointBindings.Validate([new DefaultEndpointDataSource(endpoints)], points);
        Assert.Equal("workspace.folder.editObject", Assert.Single(points, point => point.ActionScope == "contextMenu" && point.Key == "edit").BehaviorKey);
        Assert.Equal("workspace.folder.remove", Assert.Single(points, point => point.ActionScope == "contextMenu" && point.Key == "remove").BehaviorKey);
    }

    [Fact]
    public void Validate_AcceptsSameBehaviorOwnedByDifferentModules()
    {
        var points = FeatureCatalogCompiler.Compile([new("A", "entry", "navigation", "A", null, "shared.read"),
            new("B", "entry", "navigation", "B", null, "shared.read")]);
        var endpoints = new[] { "A", "B" }.Select(module => new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new EndpointModuleAttribute(module), new RequireFeatureAttribute("shared.read")), module));
        FeatureEndpointBindings.Validate([new DefaultEndpointDataSource(endpoints)], points);
    }
    [Fact]
    public void Validate_RejectsMissingDeclarationMissingConsumerAndWrongOwner()
    {
        var catalog = FeatureCatalogCompiler.Compile([new("Authorization", "entry", "navigation", "授权", null, "authorization.read")]);
        var endpoint = new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new EndpointModuleAttribute("Authorization"), new RequireFeatureAttribute("authorization.read")), "read");
        FeatureEndpointBindings.Validate([new DefaultEndpointDataSource(endpoint)], catalog);
        Assert.Throws<InvalidOperationException>(() => FeatureEndpointBindings.Validate([], catalog));
        Assert.Throws<InvalidOperationException>(() => FeatureEndpointBindings.Validate([new DefaultEndpointDataSource(endpoint)], []));
        var wrong = new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new EndpointModuleAttribute("Other"), new RequireFeatureAttribute("authorization.read")), "wrong");
        Assert.Throws<InvalidOperationException>(() => FeatureEndpointBindings.Validate([new DefaultEndpointDataSource(wrong)], catalog));
    }
    [Fact]
    public void Validate_RequiresEveryAdditionalReadBindingAndRejectsWriteEndpointsAsReads()
    {
        var catalog = FeatureCatalogCompiler.Compile([new("Organization", "persons", "navigation", "人员", null, "person.read")
            { ReadBehaviors = ["person.lookup"] }]);
        Endpoint Read(string behavior, string method) => new(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new EndpointModuleAttribute("Organization"), new RequireFeatureAttribute(behavior), new HttpMethodMetadata([method])), behavior);
        var main = Read("person.read", "GET");
        Assert.Throws<InvalidOperationException>(() => FeatureEndpointBindings.Validate([new DefaultEndpointDataSource(main)], catalog));
        Assert.Throws<InvalidOperationException>(() => FeatureEndpointBindings.Validate([new DefaultEndpointDataSource(main, Read("person.lookup", "POST"))], catalog));
        FeatureEndpointBindings.Validate([new DefaultEndpointDataSource(main, Read("person.lookup", "GET"))], catalog);
    }
}
