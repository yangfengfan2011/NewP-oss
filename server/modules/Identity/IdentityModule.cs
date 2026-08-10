using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Identity;

/// <summary>
/// Identity 必需内建模块的最小行为绑定。
/// </summary>
public sealed class IdentityModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "identity.backend";

    public override void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
    }
}
