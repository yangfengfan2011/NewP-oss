using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceMutationInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BeforeArrayMutation_IsRejectedBeforeResolvingTargets(bool mutatePosition)
    {
        // 使用真实Before快照及对象执行器；只为反例开放测试root，不改变生产root的扩展约束。
        var transactions = new GuardTransaction();
        var mutation = new ArrayMutation(mutatePosition);
        var contents = GuardOnlyContents();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStandardCrudKernel();
        services.AddSingleton<IEntityAuditStampProvider>(new Stamp());
        services.AddSingleton<IPlatformTransactionExecutor>(transactions);
        services.AddObjectBehavior<OpenMutation>(new TestModule("Workspace", []), OpenMutation.DefinitionValue,
            (provider, _) => new(contents, () => provider.GetRequiredService<IObjectOperationExecutor>()));
        services.AddObjectOperationExtension<ArrayMutation>(new TestModule("Probe", ["Workspace"]),
            new("mutate-array", "Workspace", OpenMutation.DefinitionValue.ObjectKey, ObjectOperationAction.Delete), (_, _) => mutation);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>();
        var handler = new WorkspaceMutationHandler(contents, () => operations);
        var callbacks = 0;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.WithinBoundaryAsync(
            new("actor", new("space", "root", new[] { "parent-link" }), ["selected-link"], null, ObjectOperationAction.Delete),
            "deleteObjects", (_, _) => { callbacks++; return Task.FromResult(StandardCrudMutationResult.Success()); }, default));

        Assert.Equal("WORKSPACE_BUTTON_INPUT_INVALID", error.Message);
        Assert.True(mutation.Ran);
        Assert.Equal(0, callbacks);
        Assert.Equal(0, transactions.Flushes);
    }

    // 反例必须在任何图锁/位置读取前失败；真实事务SQL与判权由独立API/SQL切片验证。
    private static WorkspaceContents GuardOnlyContents() => new(db: null!, positions: null!, transactions: null!,
        ids: null!, audit: null!, catalog: null!, folderScope: null!, reader: null!, crud: null!, resources: null!,
        documentFiles: null!, documentCopies: null!, treeOrder: null!, referenceAudit: null!,
        logger: NullLogger<WorkspaceContents>.Instance, ownership: null!, ownershipAudit: null!, folderDescriptor: null!,
        parentAuthorizations: null!, spaceInitialization: null!, executionAccess: null!, objectActions: null!, mutationRepository: null!,
        copyButtons: null!, copyPreparationHandler: null!, copyHandler: null!, folderCopyPreparationHandler: null!, folderCopyHandler: null!);

    private sealed class TestModule(string id, IReadOnlyList<string> dependencies) : ModuleBase
    {
        public override string ModuleId => id;
        public override string DisplayName => id;
        public override IReadOnlyList<string> Dependencies => dependencies;
    }

    private sealed class OpenMutation(WorkspaceContents contents, Func<IObjectOperationExecutor> operations) : IObjectBehavior
    {
        internal static ObjectBehaviorDefinition DefinitionValue { get; } = new(
            WorkspaceObjectMutation.BehaviorDefinition.ObjectKey, "Workspace", "WorkspaceFolderContent",
            [ObjectOperationAction.Delete], [ObjectOperationAction.Delete], WorkspaceObjectMutation.BehaviorDefinition.ProtectedInputFields);
        public ObjectBehaviorDefinition Definition => DefinitionValue;
        public ValueTask<StandardCrudMutationResult> ExecuteAsync(ObjectOperationContext context, CancellationToken cancellationToken) =>
            new WorkspaceObjectMutation(() => contents, operations).ExecuteAsync(context, cancellationToken);
    }

    private sealed class ArrayMutation(bool position) : IObjectOperationExtension
    {
        public bool Ran { get; private set; }
        public ValueTask<StandardCrudError?> BeforeAsync(ObjectOperationBeforeContext context, CancellationToken cancellationToken)
        {
            var array = position ? (string[])((WorkspacePosition)context.Values["Position"]!).LinkIds : (string[])context.Values["LinkIds"]!;
            array[0] = "replaced-link";
            Ran = true;
            return ValueTask.FromResult<StandardCrudError?>(null);
        }
        public ValueTask AfterAsync(ObjectOperationAfterContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class Stamp : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent() => new("actor", new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc));
    }

    private sealed class GuardTransaction : IPlatformTransactionExecutor
    {
        public int Flushes { get; private set; }
        public ValueTask<TResult> ExecuteAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> registerChanges,
            CancellationToken cancellationToken = default) => registerChanges(cancellationToken);
        public ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            Flushes++;
            return ValueTask.CompletedTask;
        }
    }
}
