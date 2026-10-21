using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class ObjectActionReadBoundaryTests
{
    [Fact]
    public async Task ActualRead_PrecedesCurrentActionsWithoutRepeatingOwnerReadOrCachingAllowance()
    {
        var fixture = new Fixture();
        var reads = 0;
        var value = new object();
        var result = await fixture.Access.WithReadAsync(async token =>
        {
            await Task.Yield();
            fixture.Trace.Add("owner-read");
            reads++;
            return new ObjectActionRead<object>("Part", "Part_actual", value);
        }, async (facts, scope, token) =>
        {
            Assert.Same(value, facts);
            fixture.Decisions.Allow = false;
            Assert.Equal(DataExecutionDecision.Denied, await scope.CheckAsync("Part", "Part_actual", ["partCopy"], token));
            fixture.Decisions.Allow = true;
            Assert.Equal(DataExecutionDecision.Allowed, await scope.CheckAsync("Part", "Part_actual", ["partObjectCopy"], token));
            Assert.Equal(DataExecutionDecision.Allowed, await scope.CheckAsync("Part", "Part_actual", ["partCopy"], token));
            fixture.Decisions.Allow = false;
            Assert.Equal(DataExecutionDecision.Denied, await scope.CheckAsync("Part", "Part_actual", ["partObjectCopy"], token));
            return 7;
        });
        Assert.Equal(7, result);
        Assert.Equal(1, reads);
        Assert.Equal(["owner-read", "Part_actual:partCopy", "Part_actual:partObjectCopy", "Part_actual:partCopy", "Part_actual:partObjectCopy"], fixture.Trace);
    }

    [Theory]
    [InlineData("fault")]
    [InlineData("cancel")]
    [InlineData("null")]
    [InlineData("unknown-object")]
    [InlineData("wrong-id")]
    [InlineData("changed-subject")]
    [InlineData("changed-request")]
    public async Task UnsuccessfulOrChangedRead_NeverEntersTheConsumer(string problem)
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException("owner read fault");
        var consumed = false;
        var caught = await Record.ExceptionAsync(() => fixture.Access.WithReadAsync<object, int>(_ =>
        {
            if (problem == "fault") throw failure;
            if (problem == "cancel") throw new OperationCanceledException();
            if (problem == "changed-subject") fixture.Http.HttpContext!.User = Principal("other");
            if (problem == "changed-request") fixture.Http.HttpContext = new DefaultHttpContext { User = Principal("actor") };
            return Task.FromResult(problem == "null" ? null! : new ObjectActionRead<object>(
                problem == "unknown-object" ? "Unknown" : "Part", problem == "wrong-id" ? "Document_wrong" : "Part_actual", new()));
        }, (_, _, _) => { consumed = true; return Task.FromResult(0); }));
        Assert.NotNull(caught);
        if (problem == "fault") Assert.Same(failure, caught);
        else if (problem == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(caught);
        else Assert.IsType<ActionDataAccessException>(caught);
        Assert.False(consumed);
        Assert.Empty(fixture.Trace);
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("fault")]
    [InlineData("cancel")]
    public async Task Scope_ExpiresAfterEveryConsumerExit(string exit)
    {
        var fixture = new Fixture();
        IObjectActionReadScope? retained = null;
        var failure = new InvalidOperationException("consumer fault");
        var caught = await Record.ExceptionAsync(() => fixture.Access.WithReadAsync<object, int>(Read,
            async (_, scope, token) =>
            {
                retained = scope;
                Assert.Equal(DataExecutionDecision.Allowed, await scope.CheckAsync("Part", "Part_actual", ["edit"], token));
                if (exit == "fault") throw failure;
                if (exit == "cancel") throw new OperationCanceledException();
                return 1;
            }));
        if (exit == "normal") Assert.Null(caught);
        else if (exit == "fault") Assert.Same(failure, caught);
        else Assert.IsAssignableFrom<OperationCanceledException>(caught);
        Assert.NotNull(retained);
        await Assert.ThrowsAsync<ActionDataAccessException>(() => retained.CheckAsync("Part", "Part_actual", ["edit"]));
        Assert.Single(fixture.Trace);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("model")]
    [InlineData("subject")]
    [InlineData("request")]
    [InlineData("anonymous")]
    public async Task Scope_RejectsIdentityRequestOrSubjectSubstitutionBeforeTheDecision(string problem)
    {
        var fixture = new Fixture();
        var request = fixture.Http.HttpContext!;
        var caught = await Record.ExceptionAsync(() => fixture.Access.WithReadAsync(Read, async (_, scope, token) =>
        {
            if (problem == "subject") fixture.Http.HttpContext!.User = Principal("other");
            if (problem == "anonymous") fixture.Http.HttpContext!.User = new ClaimsPrincipal();
            if (problem == "request") fixture.Http.HttpContext = new DefaultHttpContext { User = Principal("actor") };
            try
            {
                await Assert.ThrowsAsync<ActionDataAccessException>(() => scope.CheckAsync(problem == "model" ? "Document" : "Part",
                    problem == "target" ? "Part_other" : "Part_actual", ["edit"], token));
            }
            finally { request.User = Principal("actor"); }
            return 0;
        }));
        // HttpContextAccessor切换会撤销原请求holder；还原同名actor也不能恢复原调用能力。
        if (problem == "request") Assert.IsType<ActionDataAccessException>(caught);
        else Assert.Null(caught);
        Assert.Empty(fixture.Trace);
    }

    [Fact]
    public async Task NestedRead_RejectsTheSuspendedScopeAndRestoresItAfterFailure()
    {
        var fixture = new Fixture();
        await fixture.Access.WithReadAsync(Read, async (_, outer, token) =>
        {
            var failure = new InvalidOperationException("nested consumer");
            Assert.Same(failure, await Record.ExceptionAsync(() => fixture.Access.WithReadAsync<object, int>(Read, async (_, inner, nestedToken) =>
            {
                await Assert.ThrowsAsync<ActionDataAccessException>(() => outer.CheckAsync("Part", "Part_actual", ["edit"], nestedToken));
                Assert.Equal(DataExecutionDecision.Allowed, await inner.CheckAsync("Part", "Part_actual", ["edit"], nestedToken));
                throw failure;
            }, token)));
            Assert.Equal(DataExecutionDecision.Allowed, await outer.CheckAsync("Part", "Part_actual", ["edit"], token));
            return 0;
        });
        Assert.Equal(2, fixture.Trace.Count);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("not-applicable")]
    public async Task InvalidActions_AreRejectedByTheSameCatalogBeforeDecision(string problem)
    {
        var fixture = new Fixture();
        await fixture.Access.WithReadAsync(Read, async (_, scope, token) =>
        {
            string[] actions = problem switch { "empty" => [], "duplicate" => ["edit", "edit"], "unknown" => ["unknown"], _ => ["workspaceCopy"] };
            await Assert.ThrowsAsync<ActionDataAccessException>(() => scope.CheckAsync("Part", "Part_actual", actions, token));
            return 0;
        });
        Assert.Empty(fixture.Trace);
    }

    [Fact]
    public async Task CurrentDecisionFaultAndCancellation_PropagateWithoutTryingTheNextOrBranch()
    {
        var fixture = new Fixture();
        var failure = new InvalidOperationException("current decision fault");
        fixture.Decisions.Failure = failure;
        Assert.Same(failure, await Record.ExceptionAsync(() => fixture.Access.WithReadAsync(Read, async (_, scope, token) =>
        {
            foreach (var action in new[] { "partCopy", "partObjectCopy" })
                if (await scope.CheckAsync("Part", "Part_actual", [action], token) == DataExecutionDecision.Allowed) return 1;
            return 0;
        })));
        Assert.Equal(["Part_actual:partCopy"], fixture.Trace);
        using var cancellation = new CancellationTokenSource();
        await fixture.Access.WithReadAsync(Read, async (_, scope, _) =>
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.CheckAsync("Part", "Part_actual", ["edit"], cancellation.Token));
            return 0;
        });
        Assert.Single(fixture.Trace);
    }

    [Fact]
    public async Task CapturedExecutionContext_CannotKeepTheScopeAfterItsCallbackReturns()
    {
        var fixture = new Fixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? escaped = null;
        await fixture.Access.WithReadAsync(Read, (_, scope, _) =>
        {
            escaped = Task.Run(async () =>
            {
                await release.Task;
                await Assert.ThrowsAsync<ActionDataAccessException>(() => scope.CheckAsync("Part", "Part_actual", ["edit"]));
            });
            return Task.FromResult(0);
        });
        release.SetResult();
        await escaped!;
        Assert.Empty(fixture.Trace);
    }

    [Fact]
    public async Task SubjectChangedDuringDecision_CannotResumeTheBusinessAsTheOriginalActor()
    {
        var fixture = new Fixture();
        var resumed = false;
        fixture.Decisions.BeforeResult = async () =>
        {
            await Task.Yield();
            fixture.Http.HttpContext!.User = Principal("other");
        };
        await Assert.ThrowsAsync<ActionDataAccessException>(() => fixture.Access.WithReadAsync(Read, async (_, scope, token) =>
        {
            await scope.CheckAsync("Part", "Part_actual", ["edit"], token);
            resumed = true;
            return 0;
        }));
        Assert.False(resumed);
        Assert.Single(fixture.Trace);
    }

    private static Task<ObjectActionRead<object>> Read(CancellationToken token) => Task.FromResult(new ObjectActionRead<object>("Part", "Part_actual", new()));
    private static ClaimsPrincipal Principal(string id) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "read-boundary-test"));
    private sealed class Fixture
    {
        internal readonly List<string> Trace = [];
        internal readonly HttpContextAccessor Http = new() { HttpContext = new DefaultHttpContext { User = Principal("actor") } };
        internal readonly Decisions Decisions;
        internal readonly IObjectActionAccess Access;
        internal Fixture()
        {
            Decisions = new(Trace);
            var compiled = new CanonicalModulePlanCompiler().Compile("object-read-boundary",
                new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
            Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
            Access = new ObjectActionAccess(new ResourceCatalog(), compiled.Plan!, new NoRepeatedRead(), Decisions, Http);
        }
    }
    private sealed class ResourceCatalog : IDataScopeResourceCatalog
    {
        public IReadOnlyList<DataScopeResource> Resources { get; } = [new("part.part", "Part", "Part", "EID")];
        public IReadOnlyList<string>? ActionKeysFor(string resourceKey) => ["read", "edit", "partCopy", "partObjectCopy"];
    }
    private sealed class NoRepeatedRead : IDataScopeObjectAccess
    {
        public Task<bool> IsReadableAsync(string resourceKey, string objectId, CancellationToken token = default) =>
            throw new InvalidOperationException("The actual owner read must not run again.");
    }
    private sealed class Decisions(List<string> trace) : IDataScopeObjectDecisionProvider
    {
        internal bool Allow = true;
        internal Exception? Failure;
        internal Func<Task>? BeforeResult;
        public async Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken token = default)
        {
            trace.Add(objectId + ":" + actionKey);
            if (BeforeResult is not null) await BeforeResult();
            if (Failure is not null) throw Failure;
            return Allow;
        }
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey, CancellationToken token = default) =>
            throw new InvalidOperationException("An existing read is not a creation candidate.");
    }
}
