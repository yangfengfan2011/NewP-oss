using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Http;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

// 测试屏障只暂停真实功能判定完成后的当前请求，不持业务事务或替代判定结果。
internal sealed class GrantedFeatureAdmissionBarrier
{
    internal (string Actor, string Position, string Path)? Target { get; set; }
    internal TaskCompletionSource Admitted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class AdmittedFeatureDecisionSource(IFeatureDecisionSource inner, IHttpContextAccessor contexts,
    GrantedFeatureAdmissionBarrier barrier) : IFeatureDecisionSource
{
    public async ValueTask<PermissionDecision> EvaluateAsync(string subjectId, string position, string moduleId,
        string behaviorKey, CancellationToken cancellationToken)
    {
        var decision = await inner.EvaluateAsync(subjectId, position, moduleId, behaviorKey, cancellationToken);
        if (decision == PermissionDecision.Granted && barrier.Target is { } target
            && subjectId == target.Actor && position == target.Position
            && moduleId == "Authorization" && behaviorKey == "authorization.workbench.save"
            && contexts.HttpContext?.Request.Path.Value == target.Path)
        {
            barrier.Admitted.TrySetResult();
            await barrier.Resume.Task.WaitAsync(cancellationToken);
        }
        return decision;
    }
}

public sealed class GrantedFeatureAdmissionBarrierTests
{
    [Fact]
    public async Task ExactRequest_DelegatesBeforePausingAndFinallyReleasesTheOriginalDecision()
    {
        var (source, inner, barrier) = Create("/target", PermissionDecision.Granted);
        var pending = source.EvaluateAsync("actor", "save", "Authorization", "authorization.workbench.save", default).AsTask();
        try
        {
            Assert.Equal(1, inner.Calls);
            Assert.True(barrier.Admitted.Task.IsCompletedSuccessfully);
            Assert.False(pending.IsCompleted);
        }
        finally { barrier.Resume.TrySetResult(); }
        Assert.Equal(PermissionDecision.Granted, await pending);
    }

    [Theory]
    [InlineData("other", "save", "/target", PermissionDecision.Granted)]
    [InlineData("actor", "other", "/target", PermissionDecision.Granted)]
    [InlineData("actor", "save", "/other", PermissionDecision.Granted)]
    [InlineData("actor", "save", "/target", PermissionDecision.Denied)]
    [InlineData("actor", "save", "/target", PermissionDecision.Indeterminate)]
    public async Task OtherRequestsAndNonGrantedDecisions_AreNeverPaused(string actor, string position, string path, PermissionDecision decision)
    {
        var (source, inner, barrier) = Create(path, decision);
        Assert.Equal(decision, await source.EvaluateAsync(actor, position, "Authorization", "authorization.workbench.save", default));
        Assert.Equal(1, inner.Calls);
        Assert.False(barrier.Admitted.Task.IsCompleted);
    }

    [Fact]
    public async Task Cancellation_DoesNotTurnTheRealDecisionIntoASuccessfulRequest()
    {
        var (source, _, barrier) = Create("/target", PermissionDecision.Granted);
        using var cancelled = new CancellationTokenSource();
        var pending = source.EvaluateAsync("actor", "save", "Authorization", "authorization.workbench.save", cancelled.Token).AsTask();
        try
        {
            Assert.True(barrier.Admitted.Task.IsCompletedSuccessfully);
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        finally { barrier.Resume.TrySetResult(); }
    }

    [Theory]
    [InlineData("Other", "authorization.workbench.save")]
    [InlineData("Authorization", "authorization.workbench.read")]
    public async Task OtherModuleOrBehavior_IsNotTheAdmittedSave(string module, string behavior)
    {
        var (source, inner, barrier) = Create("/target", PermissionDecision.Granted);
        Assert.Equal(PermissionDecision.Granted, await source.EvaluateAsync("actor", "save", module, behavior, default));
        Assert.Equal(1, inner.Calls);
        Assert.False(barrier.Admitted.Task.IsCompleted);
    }

    private static (AdmittedFeatureDecisionSource Source, DecisionSource Inner, GrantedFeatureAdmissionBarrier Barrier)
        Create(string path, PermissionDecision decision)
    {
        var inner = new DecisionSource(decision);
        var contexts = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        contexts.HttpContext.Request.Path = path;
        var barrier = new GrantedFeatureAdmissionBarrier { Target = ("actor", "save", "/target") };
        return (new(inner, contexts, barrier), inner, barrier);
    }

    private sealed class DecisionSource(PermissionDecision decision) : IFeatureDecisionSource
    {
        internal int Calls;
        public ValueTask<PermissionDecision> EvaluateAsync(string subjectId, string position, string moduleId,
            string behaviorKey, CancellationToken cancellationToken)
        { Calls++; return ValueTask.FromResult(decision); }
    }
}
