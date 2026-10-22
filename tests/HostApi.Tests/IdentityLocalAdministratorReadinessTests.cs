using ICP.Platform.HostApi.Readiness;
using ICP.Platform.Modules.Identity.Application;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class IdentityLocalAdministratorReadinessTests
{
    [Theory]
    [InlineData(LocalAdministratorReadinessState.BootstrapRequired, "identity-bootstrap-required", IdentityLocalAdministratorReadinessFailureCodes.BootstrapRequired)]
    [InlineData(LocalAdministratorReadinessState.InvariantInvalid, "local-administrator-count-invalid", IdentityLocalAdministratorReadinessFailureCodes.InvariantInvalid)]
    public async Task CheckAsync_WhenAdministratorIsNotReady_ReturnsStableFailure(
        LocalAdministratorReadinessState state,
        string diagnostic,
        string expectedFailureCode)
    {
        var contributor = new IdentityLocalAdministratorReadinessContributor(
            new FakeProbe(new LocalAdministratorReadiness(state, diagnostic)));

        var result = await contributor.CheckAsync(CancellationToken.None);

        Assert.Equal(ReadinessState.Failed, result.State);
        Assert.Equal(expectedFailureCode, result.FailureCode);
        Assert.Equal(diagnostic, result.Diagnostic);
        Assert.True(contributor.IsRequired);
    }

    [Fact]
    public async Task CheckAsync_WhenAdministratorInvariantIsComplete_ReturnsReady()
    {
        var contributor = new IdentityLocalAdministratorReadinessContributor(
            new FakeProbe(new LocalAdministratorReadiness(LocalAdministratorReadinessState.Ready, "local-administrator-ready", "User_1")));

        var result = await contributor.CheckAsync(CancellationToken.None);

        Assert.Equal(ReadinessState.Ready, result.State);
        Assert.Null(result.FailureCode);
    }

    private sealed class FakeProbe(LocalAdministratorReadiness result) : IIdentityLocalAdministratorReadinessProbe
    {
        public ValueTask<LocalAdministratorReadiness> InspectAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(result);
    }
}
