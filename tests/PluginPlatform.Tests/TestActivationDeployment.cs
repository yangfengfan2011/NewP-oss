using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.PluginPlatform.Activation;
using ICP.Platform.PluginPlatform.Lifecycle;

namespace ICP.Platform.PluginPlatform.Tests;

internal sealed class TestActivationDeployment(bool requiresConfirmation) : IPluginActivationDeployment
{
    internal CompiledPersistencePlan? Plan { get; set; }
    internal List<string>? Events { get; set; }
    internal HashSet<string> ConfirmedSnapshotIds { get; } = new(StringComparer.Ordinal);
    internal Exception? PreparationFailure { get; set; }
    internal Exception? ConfirmationFailure { get; set; }
    internal CompiledPersistencePlan? Pending { get; private set; }

    public CompiledPersistencePlan Compile(CanonicalModulePlan modulePlan)
    {
        Events?.Add("persistence-plan");
        return Plan ?? throw new InvalidOperationException("This fixture only reads lifecycle state.");
    }

    public void PrepareCandidate(CanonicalModulePlan modulePlan, CompiledPersistencePlan persistencePlan)
    {
        Events?.Add("deployment-prepare");
        if (PreparationFailure is not null) throw PreparationFailure;
        Pending = persistencePlan;
    }

    public void CompleteCandidate(CompiledPersistencePlan persistencePlan)
    {
        Events?.Add("deployment-complete");
        if (ConfirmationFailure is not null) throw ConfirmationFailure;
        ConfirmedSnapshotIds.Add(persistencePlan.SnapshotId);
        Pending = null;
    }

    public bool IsSnapshotCommitted(PluginActiveSnapshot snapshot) =>
        !requiresConfirmation || ConfirmedSnapshotIds.Contains(snapshot.SnapshotId);
}
