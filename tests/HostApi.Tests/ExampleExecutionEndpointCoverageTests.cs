using ICP.Platform.Kernel.Extensions;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.Authorization.Extensions;
using System.Text.Json;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Example.Crud;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class ExampleExecutionEndpointCoverageTests : IDisposable
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "newp-endpoint-example-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot)) Directory.Delete(_storageRoot, recursive: true);
    }

    [Fact]
    public void ActualExampleEndpoints_BindEveryMigratedExecution()
    {
        using var app = Build(ExampleRecordCrudDescriptorFactory.Create());
        app.MapStandardCrudResources();
        var bindings = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Select(endpoint => endpoint.Metadata.GetMetadata<StandardCrudDataExecutionEndpoint>()).Where(item => item?.ResourceKey == "example.record").ToArray();
        Assert.Equal(new[] { "create", "delete", "export", "list", "read", "update" }, bindings.Select(item => item!.Operation).Order(StringComparer.Ordinal));
        Assert.Contains(bindings.Single(item => item!.Operation == "read")!.Bindings, binding => binding.Execution == "example.record.update" && binding.Stage == "prepare");
        foreach (var resource in new[] { "example.record", "organization.person" })
        {
            var export = Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints),
                endpoint => endpoint.Metadata.GetMetadata<StandardCrudDataExecutionEndpoint>() is { Operation: "export" } binding
                    && binding.ResourceKey == resource);
            Assert.Equal(typeof(ICP.Platform.Crud.Runtime.StandardCrudExportHandler),
                export.Metadata.GetMetadata<ICP.Platform.Authorization.Http.ButtonBusinessHandlerAttribute>()?.HandlerType);
            Assert.Equal(resource + ".export", Assert.Single(export.Metadata.GetMetadata<StandardCrudDataExecutionEndpoint>()!.Bindings).Execution);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("missing-list")]
    [InlineData("missing-read")]
    [InlineData("missing-export")]
    public void ActualExampleEndpoints_RejectUnregisteredAndUnboundExecutions(string problem)
    {
        var descriptor = ExampleRecordCrudDescriptorFactory.Create();
        var bindings = descriptor.DataExecutions.ToDictionary(item => item.Key, item => item.Value);
        if (problem == "unknown") bindings["update"] = "unregistered.execution";
        else bindings.Remove(problem.StartsWith("missing-", StringComparison.Ordinal) ? problem[8..] : "update");
        descriptor = descriptor with { DataExecutions = bindings };
        using var app = Build(descriptor);
        Assert.Throws<InvalidOperationException>(() => app.MapStandardCrudResources());
    }

    [Theory]
    [InlineData("api/identity/accounts", "list", "identity.account")]
    [InlineData("api/identity/accounts/{accountEid}", "read", "identity.account")]
    [InlineData("api/identity/accounts/{accountEid}/snapshot", "read", "identity.account")]
    [InlineData("api/authorization/roles/query", "list", "authorization.role")]
    [InlineData("api/authorization/roles/{roleId}", "read", "authorization.role")]
    public void ExistingMvcReads_HaveExactlyTheirOwnOperationAndRetainTheirOriginalQualification(string path, string operation, string resource)
    {
        using var app = Build(ExampleRecordCrudDescriptorFactory.Create());
        app.MapStandardCrudResources();
        var endpoint = Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText?.TrimStart('/') == path && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));
        var action = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()!;
        var declaration = Assert.Single(action.FilterDescriptors.Select(item => item.Filter).OfType<StandardCrudDataExecutionAttribute>());
        Assert.All(endpoint.Metadata.GetOrderedMetadata<StandardCrudDataExecutionAttribute>(), item =>
        {
            Assert.Equal(declaration.ResourceKey, item.ResourceKey); Assert.Equal(declaration.Operation, item.Operation);
            Assert.Equal(declaration.Bindings, item.Bindings);
        });
        Assert.Equal(operation, declaration.Operation); Assert.Equal(resource, declaration.ResourceKey);
        var binding = Assert.Single(declaration.Bindings);
        Assert.Equal(resource + "." + operation, binding.Execution); Assert.Equal("standardCrud." + operation, binding.Provider);
        Assert.Equal(resource + ".read", Assert.Single(endpoint.Metadata.GetOrderedMetadata<ICP.Platform.Authorization.Http.RequireFeatureAttribute>()).BehaviorKey);
        if (resource == "identity.account")
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>(), item => item.Policy == "AdminOnly");
    }

    private WebApplication Build(StandardCrudDescriptor descriptor)
    {
        var source = ICP.Platform.Testing.StandardDataReadFixtureCatalog.Source();
        var candidates = new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(source);
        var compiled = new CanonicalModulePlanCompiler().Compile("execution-coverage", candidates, source.PlatformCompositionModuleId);
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(compiled.Plan!);
        var persistence = new ICP.Platform.Persistence.Composition.PersistencePlanCompiler(
            Path.Combine(source.DistributionRoot, "server", "platform-modeling", "metadata", "storage-types.json"))
            .Compile(compiled.Plan!, ICP.Platform.Persistence.PersistenceProvider.SqlServer);
        Assert.True(persistence.IsSuccess, System.Text.Json.JsonSerializer.Serialize(persistence.Diagnostics));
        // 只编译端点，不启动Host或连接数据库；基础设施仍沿正式组合注册，不替换业务/判权实现。
        builder.Configuration["Authentication:Sessions:DefaultTimeZoneId"] = "Asia/Shanghai";
        builder.Configuration["Files:StorageRoot"] = _storageRoot;
        builder.Configuration["Persistence:Provider"] = "SqlServer";
        builder.Configuration["ConnectionStrings:SqlServer"] = "Server=invalid;Database=never-connect;Integrated Security=True";
        builder.Services.AddPlatformPersistence(builder.Configuration, persistence.Plan!);
        builder.Services.AddRelationTrees(compiled.Plan!);
        builder.Services.AddPlatformAuthorization();
        // 与真实Workspace Host同一基础模块集合；认证会话和文件契约随实际依赖装配，不逐处理器补许可。
        var moduleAssemblies = new[]
        {
            typeof(ICP.Platform.Modules.Identity.IdentityModule).Assembly,
            typeof(ICP.Platform.Modules.Authentication.AuthenticationModule).Assembly,
            typeof(ICP.Platform.Modules.Authorization.AuthorizationModule).Assembly,
            typeof(ICP.Platform.Modules.Audit.AuditModule).Assembly,
            typeof(ICP.Platform.Modules.Organization.OrganizationModule).Assembly,
            typeof(ICP.Platform.Modules.Workspace.WorkspaceModule).Assembly,
            typeof(ICP.Platform.Modules.Part.PartModule).Assembly,
            typeof(ICP.Platform.Modules.Document.DocumentModule).Assembly,
            typeof(ICP.Platform.Modules.Files.FilesModule).Assembly
        };
        builder.Services.AddModuleSystem(options => options.Assemblies.AddRange(moduleAssemblies));
        builder.Services.AddSingleton<ICP.Platform.Presentation.IRuntimeTextCatalog>(ICP.Platform.Presentation.RuntimeTextCatalog.Parse(
            new ICP.Platform.Kernel.PluginPackages.LocalizationRuntimeManifestBuilder().Build(source.DistributionRoot)));
        builder.Services.AddScoped<ICP.Platform.Presentation.IRequestPresentationContextAccessor,
            ICP.Platform.HostApi.Presentation.HttpRequestPresentationContextAccessor>();
        builder.Services.AddStandardCrudKernel();
        builder.Services.AddStandardCrudResource(descriptor);
        builder.Services.AddControllers().AddApplicationPart(typeof(ICP.Platform.Modules.Identity.Controllers.IdentityAccountsController).Assembly)
            .AddApplicationPart(typeof(ICP.Platform.Modules.Authorization.Controllers.RoleController).Assembly)
            .AddApplicationPart(typeof(ICP.Platform.Modules.Workspace.WorkspaceContentsController).Assembly)
            .AddApplicationPart(typeof(ICP.Platform.Modules.Part.PartHistoryController).Assembly)
            .AddApplicationPart(typeof(ICP.Platform.Modules.Document.DocumentHistoryController).Assembly);
        var app = builder.Build();
        app.MapControllers();
        return app;
    }
}
