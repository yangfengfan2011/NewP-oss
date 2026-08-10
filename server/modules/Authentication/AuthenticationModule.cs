using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Authentication;

/// <summary>
/// Authentication 必需内建模块的最小行为绑定。
/// </summary>
public sealed class AuthenticationModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "authentication.backend";

    public override void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
    }
}
