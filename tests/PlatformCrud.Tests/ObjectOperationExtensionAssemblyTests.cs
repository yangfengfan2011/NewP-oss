using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Crud.Tests.Support;
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.Transactions;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class ObjectOperationExtensionAssemblyTests
{
    [Theory]
    [InlineData("missing-target", "OBJECT_OPERATION_EXTENSION_TARGET_NOT_FOUND")]
    [InlineData("closed-action", "OBJECT_OPERATION_EXTENSION_ACTION_NOT_OPEN")]
    [InlineData("missing-module-dependency", "OBJECT_OPERATION_EXTENSION_MODULE_DEPENDENCY_REQUIRED")]
    [InlineData("missing-order-dependency", "OBJECT_OPERATION_EXTENSION_ORDER_DEPENDENCY_NOT_FOUND")]
    [InlineData("order-cycle", "OBJECT_OPERATION_EXTENSION_ORDER_CYCLE")]
    public async Task StartAsync_WhenExtensionAssemblyIsInvalid_FailsBeforeFirstRequest(
        string scenario,
        string expectedCode)
    {
        using var host = BuildHost(services => ConfigureInvalidScenario(services, scenario));

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());

        Assert.Contains(expectedCode, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_WhenDeclaredCallTargetDoesNotExist_FailsBeforeFirstRequest()
    {
        using var host = BuildHost(services =>
        {
            var owner = new TestModule("Organization", []);
            var contributor = new TestModule("ExtensionProvider", ["Organization"]);
            services.AddObjectBehavior<RecordingBehavior>(owner, PersonDefinition());
            services.AddObjectOperationExtension<ExtensionA>(
                contributor,
                new ObjectOperationExtensionDefinition(
                    "missing-call-target",
                    "Organization",
                    "organization.person",
                    ObjectOperationAction.Create,
                    calls: [new ObjectOperationTarget("organization.missing", ObjectOperationAction.Create)]));
        });

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());

        Assert.Contains("OBJECT_OPERATION_CALL_TARGET_NOT_FOUND", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_WhenBehaviorFactoryReturnsMismatchedDefinition_FailsBeforeFirstRequest()
    {
        using var host = BuildHost(services =>
        {
            var owner = new TestModule("Organization", []);
            var declared = ReusableDefinition("organization.person", "Organization/Person");
            services.AddObjectBehavior<ConfigurableBehavior>(
                owner,
                declared,
                static (_, _) => new ConfigurableBehavior(
                    ReusableDefinition("organization.person", "Organization/Other")));
        });

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());

        Assert.Contains("OBJECT_OPERATION_BEHAVIOR_DEFINITION_MISMATCH", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_WhenBehaviorFactoryConstructionFails_FailsBeforeFirstRequest()
    {
        using var host = BuildHost(services =>
        {
            var owner = new TestModule("Organization", []);
            services.AddObjectBehavior<ConfigurableBehavior>(
                owner,
                ReusableDefinition("organization.person", "Organization/Person"),
                static (_, _) => throw new InvalidOperationException("injected behavior construction failure"));
        });

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());

        Assert.Contains("injected behavior construction failure", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_RunsStableTopologyAroundCoreAndSnapshotsAfterResult()
    {
        var calls = new InvocationLog();
        var services = CreateServices();
        services.AddSingleton(calls);
        var owner = new TestModule("Organization", []);
        var contributor = new TestModule("ExtensionProvider", ["Organization"]);
        services.AddObjectBehavior<RecordingBehavior>(owner, PersonDefinition());
        services.AddObjectOperationExtension<ExtensionB>(contributor, Extension("b", runsBefore: ["c"]));
        services.AddObjectOperationExtension<ExtensionC>(contributor, Extension("c", runsAfter: ["a"]));
        services.AddObjectOperationExtension<ExtensionA>(contributor, Extension("a"));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>().ExecuteAsync(
            "organization.person",
            ObjectOperationAction.Create,
            null,
            new Dictionary<string, object?> { ["PersonNo"] = "P-001" });

        Assert.True(result.Succeeded);
        Assert.Equal(["before:a", "before:b", "before:c", "core", "after:a", "after:b", "after:c"], calls.Calls);
        Assert.Equal("由扩展补充", result.Detail!.Fields["Name"]);
        Assert.Equal("server-id", result.Detail.Fields["EID"]);
        Assert.True(calls.AfterMutationWasBlocked);
    }

    [Theory]
    [InlineData("EID")]
    [InlineData("CreatedBy")]
    public async Task ExecuteAsync_WhenBeforeTouchesProtectedInput_RejectsBeforeCore(string field)
    {
        var calls = new InvocationLog();
        var services = CreateServices();
        services.AddSingleton(calls);
        services.AddSingleton(new ProtectedField(field));
        var owner = new TestModule("Organization", []);
        var contributor = new TestModule("ExtensionProvider", ["Organization"]);
        services.AddObjectBehavior<RecordingBehavior>(owner, PersonDefinition());
        services.AddObjectOperationExtension<ProtectedInputExtension>(contributor, Extension("protected"));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>().ExecuteAsync(
                "organization.person",
                ObjectOperationAction.Create,
                null,
                new Dictionary<string, object?>()));

        Assert.Equal($"OBJECT_OPERATION_INPUT_PROTECTED: organization.person/create/{field}", exception.Message);
        Assert.DoesNotContain("core", calls.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBeforeRejects_RollsBackWithoutCallingCore()
    {
        var calls = new InvocationLog();
        var transaction = new RecordingTransactionExecutor();
        var services = CreateServices(transaction);
        services.AddSingleton(calls);
        var owner = new TestModule("Organization", []);
        var contributor = new TestModule("ExtensionProvider", ["Organization"]);
        services.AddObjectBehavior<RecordingBehavior>(owner, PersonDefinition());
        services.AddObjectOperationExtension<RejectingExtension>(contributor, Extension("reject"));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>().ExecuteAsync(
            "organization.person",
            ObjectOperationAction.Create,
            null,
            new Dictionary<string, object?>());

        Assert.False(result.Succeeded);
        Assert.Equal("Organization.Person.ExtensionRejected", result.Error?.Code);
        Assert.True(transaction.DelegateFailed);
        Assert.DoesNotContain("core", calls.Calls);
    }

    [Fact]
    public async Task Assembly_AllowsOneBehaviorAndExtensionTypeForDistinctDeclaredIdentities()
    {
        using var host = BuildHost(services =>
        {
            var owner = new TestModule("Organization", []);
            var contributor = new TestModule("ExtensionProvider", ["Organization"]);
            var person = ReusableDefinition("organization.person", "Organization/Person");
            var company = ReusableDefinition("organization.company", "Organization/Company");
            services.AddObjectBehavior<ConfigurableBehavior>(owner, person, static (_, definition) => new(definition));
            services.AddObjectBehavior<ConfigurableBehavior>(owner, company, static (_, definition) => new(definition));
            services.AddObjectOperationExtension<ReusableExtension>(contributor, new(
                "shared-extension",
                "Organization",
                person.ObjectKey,
                ObjectOperationAction.Create));
            services.AddObjectOperationExtension<ReusableExtension>(contributor, new(
                "shared-extension",
                "Organization",
                company.ObjectKey,
                ObjectOperationAction.Create));
        });
        await host.StartAsync();
        try
        {
            using var scope = host.Services.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>();
            Assert.True(executor.CanExecute("organization.person", ObjectOperationAction.Create));
            Assert.True(executor.CanExecute("organization.company", ObjectOperationAction.Create));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task AddStandardCrudObjectBehavior_WritesEachDeclaredResourceWithoutResolvingReadService()
    {
        var services = CreateServices();
        var owner = new TestModule("Organization", []);
        var person = ReusableDefinition("organization.person", "Organization/Person");
        var company = ReusableDefinition("organization.company", "Organization/Company");
        services.AddStandardCrudResource(Resource("organization.person", "Person"));
        services.AddStandardCrudResource(Resource("organization.company", "Company"));
        services.AddStandardCrudObjectBehavior(owner, person);
        services.AddStandardCrudObjectBehavior(owner, company);
        var repository = new InMemoryStandardCrudRepository([]);
        services.AddSingleton<IStandardCrudRepository>(repository);
        services.AddScoped<StandardCrudService>(_ =>
            throw new InvalidOperationException("Object writes must not resolve the read and presentation service."));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var operations = scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>();
        foreach (var (key, model, name) in new[]
                 {
                     ("organization.person", "Person", "人员"),
                     ("organization.company", "Company", "公司")
                 })
        {
            var result = await operations.ExecuteAsync(key, ObjectOperationAction.Create, null,
                new Dictionary<string, object?> { ["ENAME"] = name });

            Assert.True(result.Succeeded, result.Error?.Message);
            var id = Assert.IsType<string>(result.Detail!.Fields["EID"]);
            var persisted = await repository.GetByIdAsync(Resource(key, model), id);
            Assert.Equal(name, persisted!["ENAME"]);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DeclaredNestedOperationPrecoordinatesClosureAndFlushesEachCore()
    {
        var transaction = new RecordingTransactionExecutor();
        var services = CreateServices(transaction);
        var owner = new TestModule("Organization", []);
        var contributor = new TestModule("ExtensionProvider", ["Organization"]);
        services.AddObjectBehavior<ConfigurableBehavior>(owner, ReusableDefinition("organization.company", "Organization/Company"), static (_, definition) => new(definition));
        services.AddObjectBehavior<ConfigurableBehavior>(owner, ReusableDefinition("organization.person", "Organization/Person"), static (_, definition) => new(definition));
        services.AddObjectOperationExtension<NestedCallExtension>(
            contributor,
            new ObjectOperationExtensionDefinition(
                "nested-person",
                "Organization",
                "organization.company",
                ObjectOperationAction.Create,
                calls: [new ObjectOperationTarget("organization.person", ObjectOperationAction.Create)]));
        services.AddSingleton(new NestedCallControl("organization.person", IgnoreFailure: false));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>().ExecuteAsync(
            "organization.company",
            ObjectOperationAction.Create,
            null,
            new Dictionary<string, object?>());

        Assert.True(result.Succeeded);
        Assert.Equal(["Organization/Company", "Organization/Person"], transaction.RootModelKeys.Order(StringComparer.Ordinal));
        Assert.Equal(2, transaction.Executions);
        Assert.Equal(2, transaction.Flushes);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNestedFailureIsIgnored_RollsBackOuterWithNestedFailure()
    {
        var transaction = new RecordingTransactionExecutor();
        var services = CreateServices(transaction);
        var owner = new TestModule("Organization", []);
        var contributor = new TestModule("ExtensionProvider", ["Organization"]);
        var nestedError = new StandardCrudError
        {
            StatusCode = 409,
            Code = "Organization.Person.NestedRejected",
            Message = "嵌套人员写入失败。"
        };
        services.AddObjectBehavior<ConfigurableBehavior>(owner, ReusableDefinition("organization.company", "Organization/Company"), static (_, definition) => new(definition));
        services.AddObjectBehavior<RejectingBehavior>(owner, ReusableDefinition("organization.person", "Organization/Person"), (_, definition) => new(definition, nestedError));
        services.AddObjectOperationExtension<NestedCallExtension>(
            contributor,
            new ObjectOperationExtensionDefinition(
                "nested-person",
                "Organization",
                "organization.company",
                ObjectOperationAction.Create,
                calls: [new ObjectOperationTarget("organization.person", ObjectOperationAction.Create)]));
        services.AddSingleton(new NestedCallControl("organization.person", IgnoreFailure: true));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>().ExecuteAsync(
            "organization.company",
            ObjectOperationAction.Create,
            null,
            new Dictionary<string, object?>());

        Assert.False(result.Succeeded);
        Assert.Equal(nestedError.Code, result.Error?.Code);
        Assert.True(transaction.DelegateFailed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenUndeclaredNestedOperationIsCaught_StillPoisonsOuterOperation()
    {
        var transaction = new RecordingTransactionExecutor();
        var services = CreateServices(transaction);
        var owner = new TestModule("Organization", []);
        var contributor = new TestModule("ExtensionProvider", ["Organization"]);
        services.AddObjectBehavior<ConfigurableBehavior>(owner, ReusableDefinition("organization.company", "Organization/Company"), static (_, definition) => new(definition));
        services.AddObjectBehavior<ConfigurableBehavior>(owner, ReusableDefinition("organization.person", "Organization/Person"), static (_, definition) => new(definition));
        services.AddObjectOperationExtension<NestedCallExtension>(
            contributor,
            new ObjectOperationExtensionDefinition(
                "nested-person",
                "Organization",
                "organization.company",
                ObjectOperationAction.Create));
        services.AddSingleton(new NestedCallControl("organization.company", IgnoreFailure: true));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>().ExecuteAsync(
                "organization.company",
                ObjectOperationAction.Create,
                null,
                new Dictionary<string, object?>()));

        Assert.Equal("OBJECT_OPERATION_TARGET_NOT_DECLARED: organization.company/create", exception.Message);
        Assert.True(transaction.DelegateFailed);
    }

    [Fact]
    public async Task ExecuteAsync_InnerAfterDoesNotExposeInnerOrRestoredOuterCoreMarker()
    {
        var services = CreateServices();
        var owner = new TestModule("Organization", []);
        var contributor = new TestModule("ExtensionProvider", ["Organization"]);
        services.AddSingleton<CoreMarkerLog>();
        services.AddObjectBehavior<OuterCallingBehavior>(owner, OuterDefinition());
        services.AddObjectBehavior<ConfigurableBehavior>(
            owner,
            ReusableDefinition("organization.inner", "Organization/Inner"),
            static (_, definition) => new(definition));
        services.AddObjectOperationExtension<CoreMarkerExtension>(
            contributor,
            new ObjectOperationExtensionDefinition(
                "core-marker",
                "Organization",
                "organization.inner",
                ObjectOperationAction.Create));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>().ExecuteAsync(
            "organization.outer",
            ObjectOperationAction.Create,
            null,
            new Dictionary<string, object?>());

        var log = scope.ServiceProvider.GetRequiredService<CoreMarkerLog>();
        Assert.True(result.Succeeded);
        Assert.True(log.OuterCoreSeen);
        Assert.False(log.InnerBeforeSawInnerCore);
        Assert.False(log.InnerBeforeSawOuterCore);
        Assert.False(log.InnerAfterSawInnerCore);
        Assert.False(log.InnerAfterSawOuterCore);
    }

    private static IServiceCollection CreateServices(RecordingTransactionExecutor? transaction = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStandardCrudKernel();
        services.AddSingleton<IEntityAuditStampProvider, FixedStampProvider>();
        services.AddSingleton(transaction ?? new RecordingTransactionExecutor());
        services.AddSingleton<IPlatformTransactionExecutor>(provider =>
            provider.GetRequiredService<RecordingTransactionExecutor>());
        return services;
    }

    private static IHost BuildHost(Action<IServiceCollection> configure)
    {
        var builder = Host.CreateApplicationBuilder();
        var services = CreateServices();
        foreach (var descriptor in services)
        {
            builder.Services.Add(descriptor);
        }
        configure(builder.Services);
        return builder.Build();
    }

    private static void ConfigureInvalidScenario(IServiceCollection services, string scenario)
    {
        var owner = new TestModule("Organization", []);
        var provider = new TestModule(
            "ExtensionProvider",
            scenario == "missing-module-dependency" ? [] : ["Organization"]);
        if (scenario != "missing-target")
        {
            services.AddObjectBehavior<RecordingBehavior>(owner, PersonDefinition(
                open: scenario != "closed-action"));
        }

        var first = scenario switch
        {
            "missing-order-dependency" => Extension("first", runsAfter: ["absent"]),
            "order-cycle" => Extension("first", runsAfter: ["second"]),
            _ => Extension("first")
        };
        services.AddObjectOperationExtension<ExtensionA>(provider, first);
        if (scenario == "order-cycle")
        {
            services.AddObjectOperationExtension<ExtensionB>(provider, Extension("second", runsAfter: ["first"]));
        }
    }

    private static ObjectBehaviorDefinition PersonDefinition(bool open = true) => new(
        "organization.person",
        "Organization",
        "Organization/Person",
        [ObjectOperationAction.Create],
        open ? [ObjectOperationAction.Create] : [],
        ["EID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"]);

    private static ObjectBehaviorDefinition ReusableDefinition(string objectKey, string modelKey) => new(
        objectKey,
        "Organization",
        modelKey,
        [ObjectOperationAction.Create],
        [ObjectOperationAction.Create],
        ["EID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"]);

    private static ObjectBehaviorDefinition OuterDefinition() => new(
        "organization.outer",
        "Organization",
        "Organization/Outer",
        [ObjectOperationAction.Create],
        [ObjectOperationAction.Create],
        ["EID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"],
        [new ObjectOperationCall(
            ObjectOperationAction.Create,
            new ObjectOperationTarget("organization.inner", ObjectOperationAction.Create))]);

    private static StandardCrudDescriptor Resource(string resourceKey, string modelName) => new()
    {
        ResourceKey = resourceKey,
        ModuleId = "Organization",
        ModelName = modelName,
        Route = $"api/organization/{modelName.ToLowerInvariant()}",
        IdField = "EID",
        DisplayField = "ENAME",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "ENAME"],
            DetailFields: ["EID", "ENAME"],
            CreateFields: ["ENAME"],
            UpdateFields: ["ENAME"])
    };

    private static ObjectOperationExtensionDefinition Extension(
        string key,
        IReadOnlyList<string>? runsAfter = null,
        IReadOnlyList<string>? runsBefore = null) => new(
            key,
            "Organization",
            "organization.person",
            ObjectOperationAction.Create,
            runsAfter ?? [],
            runsBefore ?? []);

    private sealed class TestModule(string moduleId, IReadOnlyList<string> dependencies) : ModuleBase
    {
        public override string ModuleId => moduleId;
        public override string DisplayName => moduleId;
        public override IReadOnlyList<string> Dependencies => dependencies;
    }

    private sealed class InvocationLog
    {
        public List<string> Calls { get; } = [];
        public bool AfterMutationWasBlocked { get; set; }
    }

    private sealed record ProtectedField(string Value);

    private sealed class RecordingBehavior(InvocationLog calls)
        : ObjectBehaviorBase(PersonDefinition())
    {
        protected override ValueTask<StandardCrudMutationResult> CreateAsync(
            ObjectOperationContext context,
            CancellationToken cancellationToken)
        {
            calls.Calls.Add("core");
            return ValueTask.FromResult(StandardCrudMutationResult.Success(new StandardCrudDetail
            {
                Fields = new Dictionary<string, object?>
                {
                    ["EID"] = "server-id",
                    ["Name"] = context.Values["Name"]
                }
            }));
        }
    }

    private sealed class ConfigurableBehavior(ObjectBehaviorDefinition definition)
        : ObjectBehaviorBase(definition)
    {
        protected override ValueTask<StandardCrudMutationResult> CreateAsync(
            ObjectOperationContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(StandardCrudMutationResult.Success());
    }

    private sealed class RejectingBehavior(
        ObjectBehaviorDefinition definition,
        StandardCrudError error) : ObjectBehaviorBase(definition)
    {
        protected override ValueTask<StandardCrudMutationResult> CreateAsync(
            ObjectOperationContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(StandardCrudMutationResult.Failure(error));
    }

    private sealed class CoreMarkerLog
    {
        internal bool OuterCoreSeen { get; set; }
        internal bool InnerBeforeSawInnerCore { get; set; }
        internal bool InnerBeforeSawOuterCore { get; set; }
        internal bool InnerAfterSawInnerCore { get; set; }
        internal bool InnerAfterSawOuterCore { get; set; }
    }

    private sealed class OuterCallingBehavior(
        Func<IObjectOperationExecutor> executor,
        IObjectOperationExecutionContext executionContext,
        CoreMarkerLog log) : ObjectBehaviorBase(OuterDefinition())
    {
        protected override async ValueTask<StandardCrudMutationResult> CreateAsync(
            ObjectOperationContext context,
            CancellationToken cancellationToken)
        {
            log.OuterCoreSeen = executionContext.IsExecuting(
                "organization.outer",
                ObjectOperationAction.Create,
                null);
            return await executor().ExecuteAsync(
                "organization.inner",
                ObjectOperationAction.Create,
                null,
                new Dictionary<string, object?>(),
                cancellationToken);
        }
    }

    private sealed class CoreMarkerExtension(
        IObjectOperationExecutionContext executionContext,
        CoreMarkerLog log) : IObjectOperationExtension
    {
        public ValueTask<StandardCrudError?> BeforeAsync(
            ObjectOperationBeforeContext context,
            CancellationToken cancellationToken)
        {
            log.InnerBeforeSawInnerCore = executionContext.IsExecuting(
                "organization.inner",
                ObjectOperationAction.Create,
                null);
            log.InnerBeforeSawOuterCore = executionContext.IsExecuting(
                "organization.outer",
                ObjectOperationAction.Create,
                null);
            return ValueTask.FromResult<StandardCrudError?>(null);
        }

        public ValueTask AfterAsync(
            ObjectOperationAfterContext context,
            CancellationToken cancellationToken)
        {
            log.InnerAfterSawInnerCore = executionContext.IsExecuting(
                "organization.inner",
                ObjectOperationAction.Create,
                null);
            log.InnerAfterSawOuterCore = executionContext.IsExecuting(
                "organization.outer",
                ObjectOperationAction.Create,
                null);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record NestedCallControl(string TargetObjectKey, bool IgnoreFailure);

    private sealed class NestedCallExtension(
        Func<IObjectOperationExecutor> executor,
        NestedCallControl control) : IObjectOperationExtension
    {
        public ValueTask<StandardCrudError?> BeforeAsync(
            ObjectOperationBeforeContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<StandardCrudError?>(null);

        public async ValueTask AfterAsync(
            ObjectOperationAfterContext context,
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await executor().ExecuteAsync(
                    control.TargetObjectKey,
                    ObjectOperationAction.Create,
                    null,
                    new Dictionary<string, object?>(),
                    cancellationToken);
                if (!control.IgnoreFailure && !result.Succeeded)
                {
                    throw new InvalidOperationException(result.Error?.Code);
                }
            }
            catch when (control.IgnoreFailure)
            {
            }
        }
    }

    private abstract class RecordingExtension(InvocationLog calls, string key) : IObjectOperationExtension
    {
        protected InvocationLog Calls { get; } = calls;

        public virtual ValueTask<StandardCrudError?> BeforeAsync(
            ObjectOperationBeforeContext context,
            CancellationToken cancellationToken)
        {
            Calls.Calls.Add("before:" + key);
            return ValueTask.FromResult<StandardCrudError?>(null);
        }

        public virtual ValueTask AfterAsync(
            ObjectOperationAfterContext context,
            CancellationToken cancellationToken)
        {
            Calls.Calls.Add("after:" + key);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ExtensionA(InvocationLog calls) : RecordingExtension(calls, "a")
    {
        public override ValueTask<StandardCrudError?> BeforeAsync(
            ObjectOperationBeforeContext context,
            CancellationToken cancellationToken)
        {
            context.SetValue("Name", "由扩展补充");
            return base.BeforeAsync(context, cancellationToken);
        }
    }

    private sealed class ExtensionB(InvocationLog calls) : RecordingExtension(calls, "b");

    private sealed class ExtensionC(InvocationLog calls) : RecordingExtension(calls, "c")
    {
        public override ValueTask AfterAsync(
            ObjectOperationAfterContext context,
            CancellationToken cancellationToken)
        {
            Calls.AfterMutationWasBlocked = context.ResultFields is not IDictionary<string, object?> mutable
                || Assert.Throws<NotSupportedException>(() => mutable["Name"] = "被 after 偷改").Message.Length > 0;
            return base.AfterAsync(context, cancellationToken);
        }
    }

    private sealed class ProtectedInputExtension(ProtectedField field) : IObjectOperationExtension
    {
        public ValueTask<StandardCrudError?> BeforeAsync(
            ObjectOperationBeforeContext context,
            CancellationToken cancellationToken)
        {
            context.SetValue(field.Value, "extension-value");
            return ValueTask.FromResult<StandardCrudError?>(null);
        }

        public ValueTask AfterAsync(
            ObjectOperationAfterContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class RejectingExtension : IObjectOperationExtension
    {
        public ValueTask<StandardCrudError?> BeforeAsync(
            ObjectOperationBeforeContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<StandardCrudError?>(new StandardCrudError
            {
                StatusCode = 409,
                Code = "Organization.Person.ExtensionRejected",
                Message = "扩展拒绝创建人员。"
            });

        public ValueTask AfterAsync(
            ObjectOperationAfterContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class ReusableExtension : IObjectOperationExtension
    {
        public ValueTask<StandardCrudError?> BeforeAsync(
            ObjectOperationBeforeContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<StandardCrudError?>(null);

        public ValueTask AfterAsync(
            ObjectOperationAfterContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class RecordingTransactionExecutor : IPlatformTransactionExecutor
    {
        public bool DelegateFailed { get; private set; }
        public bool IsActive => _depth > 0;
        public int Executions { get; private set; }
        public int Flushes { get; private set; }
        public IReadOnlyCollection<string> RootModelKeys { get; private set; } = [];
        private int _depth;

        public ValueTask<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, ValueTask<TResult>> registerChanges,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync([], registerChanges, cancellationToken);

        public async ValueTask<TResult> ExecuteAsync<TResult>(
            IReadOnlyCollection<string> modelKeys,
            Func<CancellationToken, ValueTask<TResult>> registerChanges,
            CancellationToken cancellationToken = default)
        {
            if (_depth == 0)
            {
                RootModelKeys = modelKeys.ToArray();
            }
            Executions++;
            _depth++;
            try
            {
                return await registerChanges(cancellationToken);
            }
            catch
            {
                DelegateFailed = true;
                throw;
            }
            finally
            {
                _depth--;
            }
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            Assert.True(IsActive);
            Flushes++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixedStampProvider : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent() =>
            new("actor", new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc));
    }
}
