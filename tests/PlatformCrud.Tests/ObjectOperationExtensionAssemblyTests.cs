using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
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
    public void AddStandardCrudObjectBehavior_ReusesOneDefaultImplementationWithoutEmptyObjectClasses()
    {
        var services = CreateServices();
        var owner = new TestModule("Organization", []);
        var person = ReusableDefinition("organization.person", "Organization/Person");
        var company = ReusableDefinition("organization.company", "Organization/Company");
        services.AddStandardCrudResource(Resource("organization.person", "Person"));
        services.AddStandardCrudResource(Resource("organization.company", "Company"));
        services.AddStandardCrudObjectBehavior(owner, person);
        services.AddStandardCrudObjectBehavior(owner, company);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var behaviors = scope.ServiceProvider.GetServices<IObjectBehaviorContribution>()
            .Select(contribution => contribution.Behavior)
            .ToArray();

        Assert.Equal(2, behaviors.Length);
        Assert.Single(behaviors.Select(behavior => behavior.GetType()).Distinct());
        Assert.Equal(
            ["organization.company", "organization.person"],
            behaviors.Select(behavior => behavior.Definition.ObjectKey).Order(StringComparer.Ordinal));
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

        public ValueTask<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, ValueTask<TResult>> registerChanges,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync([], registerChanges, cancellationToken);

        public async ValueTask<TResult> ExecuteAsync<TResult>(
            IReadOnlyCollection<string> modelKeys,
            Func<CancellationToken, ValueTask<TResult>> registerChanges,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await registerChanges(cancellationToken);
            }
            catch
            {
                DelegateFailed = true;
                throw;
            }
        }
    }

    private sealed class FixedStampProvider : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent() =>
            new("actor", new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc));
    }
}
