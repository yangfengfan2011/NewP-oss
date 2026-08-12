using ICP.Platform.HostApi.Readiness;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class PersistenceReadinessTests
{
    [Fact]
    public async Task Contributor_IsRequiredAndTracksBootstrapState()
    {
        var state = new PersistenceReadinessState();
        var contributor = new PersistenceReadinessContributor(state);

        var starting = await contributor.CheckAsync(CancellationToken.None);
        Assert.True(contributor.IsRequired);
        Assert.Equal("database", contributor.Name);
        Assert.Equal(ReadinessState.Starting, starting.State);
        Assert.Equal(PersistenceReadinessFailureCodes.NotInitialized, starting.FailureCode);

        state.MarkReady();
        var ready = await contributor.CheckAsync(CancellationToken.None);
        Assert.Equal(ReadinessState.Ready, ready.State);
        Assert.Equal("Persistence ready.", ready.Diagnostic);
    }

    [Fact]
    public async Task Contributor_UsesStableFailureCodeWithoutExceptionDetails()
    {
        var state = new PersistenceReadinessState();
        var contributor = new PersistenceReadinessContributor(state);

        state.MarkFailed("PERSISTENCE_DATABASE_UNOWNED");

        var failed = await contributor.CheckAsync(CancellationToken.None);
        Assert.Equal(ReadinessState.Failed, failed.State);
        Assert.Equal("PERSISTENCE_DATABASE_UNOWNED", failed.FailureCode);
        Assert.Equal("Persistence bootstrap failed.", failed.Diagnostic);
    }
}
