using System.Security.Claims;
using ICP.Platform.Authorization.Abstractions;
using ICP.Platform.Authorization.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Authorization.Tests;

public sealed class PermissionGateTests
{
    [Theory]
    [InlineData(PermissionDecision.Granted, null)]
    [InlineData(PermissionDecision.Denied, PermissionGateFailureCodes.PermissionDenied)]
    [InlineData(PermissionDecision.Indeterminate, PermissionGateFailureCodes.DecisionUnavailable)]
    public async Task EvaluateAsync_ReturnsStableDecisionAndFailureCode(
        PermissionDecision sourceDecision,
        string? expectedFailureCode)
    {
        var source = new RecordingPermissionDecisionSource(sourceDecision);
        var gate = new PermissionGate(source);
        var subject = AuthenticatedSubject.FromPrincipal(CreatePrincipal("trusted-subject"));

        var result = await gate.EvaluateAsync(subject, new PermissionRequirement("Organization.Person.View"));

        Assert.Equal(sourceDecision, result.Decision);
        Assert.Equal(expectedFailureCode, result.FailureCode);
        Assert.Equal("trusted-subject", source.LastSubjectId);
        Assert.Equal("Organization.Person.View", source.LastPermissionCode);
    }

    [Fact]
    public async Task EvaluateAsync_WhenSourceThrows_ReturnsIndeterminateWithoutLeakingFailure()
    {
        var gate = new PermissionGate(new ThrowingPermissionDecisionSource());
        var subject = AuthenticatedSubject.FromPrincipal(CreatePrincipal("trusted-subject"));

        var result = await gate.EvaluateAsync(subject, new PermissionRequirement("Organization.Person.View"));

        Assert.Equal(PermissionDecision.Indeterminate, result.Decision);
        Assert.Equal(PermissionGateFailureCodes.DecisionUnavailable, result.FailureCode);
    }

    [Fact]
    public async Task AddPlatformAuthorization_WhenDecisionSourceIsNotRegistered_FailsClosed()
    {
        var services = new ServiceCollection();
        services.AddPlatformAuthorization();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var gate = scope.ServiceProvider.GetRequiredService<PermissionGate>();
        var subject = AuthenticatedSubject.FromPrincipal(CreatePrincipal("trusted-subject"));

        var result = await gate.EvaluateAsync(
            subject,
            new PermissionRequirement("Organization.Person.View"));

        Assert.Equal(PermissionDecision.Indeterminate, result.Decision);
        Assert.Equal(PermissionGateFailureCodes.DecisionUnavailable, result.FailureCode);
    }

    [Fact]
    public void FromPrincipal_RejectsAuthenticatedPrincipalWithoutTrustedSubjectId()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], "test"));

        var exception = Assert.Throws<PermissionSubjectException>(() =>
            AuthenticatedSubject.FromPrincipal(principal));

        Assert.Equal(PermissionGateFailureCodes.SubjectMissing, exception.FailureCode);
    }

    private static ClaimsPrincipal CreatePrincipal(string subjectId) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, subjectId)],
            "test"));

    private sealed class RecordingPermissionDecisionSource : IPermissionDecisionSource
    {
        private readonly PermissionDecision _decision;

        public RecordingPermissionDecisionSource(PermissionDecision decision)
        {
            _decision = decision;
        }

        public string? LastSubjectId { get; private set; }

        public string? LastPermissionCode { get; private set; }

        public ValueTask<PermissionDecision> EvaluateAsync(
            string subjectId,
            string permissionCode,
            CancellationToken cancellationToken = default)
        {
            LastSubjectId = subjectId;
            LastPermissionCode = permissionCode;
            return ValueTask.FromResult(_decision);
        }
    }

    private sealed class ThrowingPermissionDecisionSource : IPermissionDecisionSource
    {
        public ValueTask<PermissionDecision> EvaluateAsync(
            string subjectId,
            string permissionCode,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PermissionDecision>(new InvalidOperationException("storage detail"));
    }
}
