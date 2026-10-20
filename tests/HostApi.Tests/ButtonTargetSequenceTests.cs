using System.Runtime.CompilerServices;
using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class ButtonMultipleTargetTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("first-denied-later-read")]
    [InlineData("first-denied-later-missing")]
    [InlineData("later-read")]
    [InlineData("later-missing")]
    [InlineData("later-denied")]
    [InlineData("check-failure")]
    [InlineData("iterator-failure")]
    [InlineData("cancel")]
    [InlineData("unknown-type")]
    [InlineData("empty")]
    [InlineData("null-factory")]
    public async Task TargetSequence_ChecksEachNormalizedTargetBeforeAdvancingAndExecutesOnlyAfterAll(string problem)
    {
        var plan = Plan();
        var button = plan.ActionDataAccessCatalog.Single(item => item.BusinessBinding == "workspace.folder.reorder" && item.ActionKey == "moveUp");
        using var cancellation = new CancellationTokenSource();
        var handler = new SequenceHandler(problem);
        var context = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actor")], "sequence-test")) };
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new EndpointModuleAttribute("Workspace"),
            new RequireFeatureAttribute(button.BusinessBinding!), new ButtonBusinessHandlerAttribute(typeof(SequenceHandler))), "formal-sequence-handler"));
        context.Request.Headers["X-NewP-Button"] = button.Id;
        context.Request.Headers["X-NewP-Feature"] = button.FeaturePositions[0];
        var executor = new ButtonBusinessExecutor(new ButtonExecutionAccess(plan, new HttpContextAccessor { HttpContext = context },
            new ResourceCatalog(), new SequenceChecks(handler, cancellation)));
        var failure = await Record.ExceptionAsync(async () => Assert.Equal(7, await executor.ExecuteAsync(handler, 7, cancellation.Token)));
        Assert.False(handler.BoundaryActive);
        Assert.Equal(1, handler.TargetCalls);
        if (problem == "valid")
        {
            Assert.Null(failure);
            Assert.Equal(1, handler.Calls);
            Assert.Equal(new[] { "first:business-read", "first:fact", "first:edit", "later:business-read", "later:fact", "later:edit", "dispose", "execute", "boundary-end" }, handler.Trace);
        }
        else
        {
            Assert.Equal(0, handler.Calls);
            if (problem is "check-failure" or "iterator-failure") Assert.Same(handler.Failure, failure);
            else if (problem == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(failure);
            else Assert.Equal(problem.StartsWith("first-denied", StringComparison.Ordinal) || problem == "later-denied" ? 403
                : problem is "later-read" or "later-missing" ? 404 : 400, Assert.IsType<ActionDataAccessException>(failure).Status);
            if (problem.StartsWith("first-denied", StringComparison.Ordinal) || problem == "cancel")
                Assert.Equal(new[] { "first:business-read", "first:fact", "first:edit", "dispose", "boundary-end" }, handler.Trace);
        }
        if (problem != "null-factory") Assert.True(handler.Disposed);
    }

    private sealed record SequenceState(int Value, IReadOnlyList<string> Ids);
    private sealed record UnknownSequenceTarget() : ButtonObjectTarget("workspace.folder", "WorkspaceFolder");

    private sealed class SequenceHandler(string problem) : IButtonBusinessHandler<int, SequenceState, int>
    {
        internal string Problem => problem;
        internal readonly List<string> Trace = [];
        internal readonly Exception Failure = new InvalidOperationException("Injected ordered target preparation failure.");
        internal bool BoundaryActive;
        internal bool Disposed;
        internal int Calls;
        internal int TargetCalls;
        private SequenceState? expected;

        public async Task<int> WithinBoundaryAsync(int input, string actionKey, Func<SequenceState, CancellationToken, Task<int>> execute, CancellationToken token)
        {
            BoundaryActive = true;
            expected = new(input, Array.AsReadOnly(new[] { "first", "later" }));
            try { return await execute(expected, token); }
            finally { BoundaryActive = false; Trace.Add("boundary-end"); }
        }

        public ButtonBusinessTarget Target(SequenceState state)
        {
            Assert.True(BoundaryActive); Assert.Same(expected, state); ++TargetCalls;
            return new ButtonTargetSequence(problem == "null-factory" ? null! : token => Targets(state, token));
        }

        private async IAsyncEnumerable<ButtonObjectTarget> Targets(SequenceState state, [EnumeratorCancellation] CancellationToken token)
        {
            try
            {
                if (problem == "empty") yield break;
                foreach (var id in state.Ids)
                {
                    Assert.True(BoundaryActive);
                    Trace.Add(id + ":business-read");
                    if (id == "later" && problem is "later-read" or "first-denied-later-read")
                        throw new ActionDataAccessException(404, "Business.NotReadable");
                    if (id == "later" && problem == "iterator-failure") throw Failure;
                    Trace.Add(id + ":fact");
                    if (id == "later" && problem is "later-missing" or "first-denied-later-missing")
                        throw new ActionDataAccessException(404, "Business.FactMissing");
                    await Task.CompletedTask;
                    yield return problem == "unknown-type" ? new UnknownSequenceTarget() : Existing("workspace.folder", "WorkspaceFolder", id);
                }
            }
            finally { Assert.True(BoundaryActive); Disposed = true; Trace.Add("dispose"); }
        }

        public Task<int> ExecuteAsync(SequenceState state, CancellationToken token)
        {
            Assert.True(BoundaryActive); Assert.True(Disposed); Assert.Same(expected, state);
            ++Calls; Trace.Add("execute"); return Task.FromResult(state.Value);
        }
    }

    private sealed class SequenceChecks(SequenceHandler handler, CancellationTokenSource cancellation) : IObjectActionAccess
    {
        public Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions, CancellationToken token = default)
        {
            Assert.True(handler.BoundaryActive);
            var id = objectId[(objectName.Length + 1)..];
            handler.Trace.Add(id + ":edit");
            if (id == "first" && handler.Problem.StartsWith("first-denied", StringComparison.Ordinal) || id == "later" && handler.Problem == "later-denied")
                return Task.FromResult(DataExecutionDecision.Denied);
            if (id == "later" && handler.Problem == "check-failure") return Task.FromException<DataExecutionDecision>(handler.Failure);
            if (handler.Problem == "cancel") cancellation.Cancel();
            return Task.FromResult(DataExecutionDecision.Allowed);
        }
    }
}
