using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class ObjectCreationContinuationTests
{
    [Fact]
    public async Task NestedCreate_UsesSameCandidateAndExpiresWithItsRootCore()
    {
        var state = new State();
        var transactions = new Transaction();
        var executor = Executor(state, transactions);
        var result = await executor.ExecuteAsync("workspace.root", ObjectOperationAction.Create, null, new Dictionary<string, object?>());
        Assert.True(result.Succeeded);
        Assert.Equal(1, state.Writes);
        Assert.Equal("Part_official", result.Detail!.Fields["EID"]);
        Assert.Equal("服务器完整候选", result.Detail.Fields["ENAME"]);
        Assert.Throws<InvalidOperationException>(() => state.Root!.ContinueCreate("part.part", "Part", (_, _, _) => Task.FromResult(StandardCrudMutationResult.Success())));
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.Source!.InvokeAsync(new Dictionary<string, object?>(), (_, _) => Task.FromResult(StandardCrudMutationResult.Success()), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.Persist!(default));
        Assert.Equal(1, state.Writes);
    }

    [Theory]
    [InlineData("source", "OBJECT_CREATION_CONTINUATION_REUSED")]
    [InlineData("clone", "OBJECT_CREATION_CONTINUATION_REUSED")]
    [InlineData("persist", "OBJECT_CREATION_PERSIST_REUSED")]
    [InlineData("omit", "OBJECT_CREATION_PERSIST_REQUIRED")]
    public async Task OneBoundary_CannotRepeatOrOmitTheOfficialPersistence(string mode, string error)
    {
        var state = new State { Mode = mode };
        var transactions = new Transaction();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Executor(state, transactions)
            .ExecuteAsync("workspace.root", ObjectOperationAction.Create, null, new Dictionary<string, object?>()));
        Assert.Equal(error, failure.Message);
        Assert.True(transactions.Failed);
        Assert.Equal(mode == "omit" ? 0 : 1, state.Writes);
    }

    private static ObjectOperationExecutor Executor(State state, Transaction transactions)
    {
        ObjectOperationExecutor? executor = null;
        executor = new([new Root(state, () => executor!), new Child(state)], new EntityAuditValueProvider(new Stamp()), transactions);
        return executor;
    }

    private sealed class State
    {
        internal string Mode = "";
        internal int Writes;
        internal ObjectOperationContext? Root;
        internal ObjectOperationWriteSource.CreateContinuation? Source;
        internal Func<CancellationToken, Task<StandardCrudMutationResult>>? Persist;
        internal IReadOnlyDictionary<string, object?>? Candidate;
    }

    private sealed class Root(State state, Func<IObjectOperationExecutor> operations) : IObjectBehavior
    {
        public ObjectBehaviorDefinition Definition { get; } = new("workspace.root", "Workspace", "WorkspaceFolderContent",
            [ObjectOperationAction.Create], [], calls: [new(ObjectOperationAction.Create, new("part.part", ObjectOperationAction.Create))]);
        public async ValueTask<StandardCrudMutationResult> ExecuteAsync(ObjectOperationContext context, CancellationToken cancellationToken)
        {
            state.Root = context;
            state.Source = context.ContinueCreate("part.part", "Part", async (candidate, persist, token) =>
            {
                state.Candidate = candidate;
                state.Persist = persist;
                Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)candidate)["EID"] = "Part_replaced");
                if (state.Mode == "omit") return StandardCrudMutationResult.Success();
                var result = await persist(token);
                return state.Mode == "persist" ? await persist(token) : result;
            });
            var first = await operations().ExecuteAsync("part.part", ObjectOperationAction.Create, null, new Dictionary<string, object?>(), state.Source, cancellationToken);
            return state.Mode is "source" or "clone"
                ? await operations().ExecuteAsync("part.part", ObjectOperationAction.Create, null, new Dictionary<string, object?>(), state.Mode == "clone" ? state.Source with { } : state.Source, cancellationToken) : first;
        }
    }

    private sealed class Child(State state) : IObjectBehavior
    {
        public ObjectBehaviorDefinition Definition { get; } = new("part.part", "Part", "Part", [ObjectOperationAction.Create], []);
        public async ValueTask<StandardCrudMutationResult> ExecuteAsync(ObjectOperationContext context, CancellationToken cancellationToken)
        {
            var source = Assert.IsType<ObjectOperationWriteSource.CreateContinuation>(context.WriteSource);
            return await source.InvokeAsync(new Dictionary<string, object?> { ["EID"] = "Part_official", ["ENAME"] = "服务器完整候选" },
                (candidate, _) =>
                {
                    state.Writes++;
                    Assert.Same(state.Candidate, candidate);
                    return Task.FromResult(StandardCrudMutationResult.Success(new StandardCrudDetail { Fields = candidate }));
                }, cancellationToken);
        }
    }

    private sealed class Transaction : IPlatformTransactionExecutor
    {
        internal bool Failed;
        public async ValueTask<TResult> ExecuteAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> registerChanges, CancellationToken cancellationToken = default)
        {
            try { return await registerChanges(cancellationToken); }
            catch { Failed = true; throw; }
        }
        public ValueTask FlushAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class Stamp : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent() => new("actor", new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc));
    }
}
