using ICP.Platform.Modules.Identity.Application;

namespace ICP.Platform.HostApi.Readiness;

public static class IdentityLocalAdministratorReadinessFailureCodes
{
    public const string BootstrapRequired = "IDENTITY_BOOTSTRAP_REQUIRED";
    public const string InvariantInvalid = "IDENTITY_LOCAL_ADMIN_INVARIANT";
}

public sealed class IdentityLocalAdministratorReadinessContributor(
    IIdentityLocalAdministratorReadinessProbe probe) : IReadinessContributor
{
    public string Name => "local-administrator";

    public int Order => 100;

    public bool IsRequired => true;

    public async ValueTask<ReadinessContribution> CheckAsync(CancellationToken cancellationToken)
    {
        var result = await probe.InspectAsync(cancellationToken);
        return result.State switch
        {
            LocalAdministratorReadinessState.Ready => ReadinessContribution.Ready(result.Diagnostic),
            LocalAdministratorReadinessState.BootstrapRequired => new(
                ReadinessState.Failed,
                IdentityLocalAdministratorReadinessFailureCodes.BootstrapRequired,
                result.Diagnostic),
            _ => new(
                ReadinessState.Failed,
                IdentityLocalAdministratorReadinessFailureCodes.InvariantInvalid,
                result.Diagnostic)
        };
    }
}
