using Microsoft.AspNetCore.DataProtection;

namespace ICP.Platform.HostApi.Authentication;

public sealed class AuthenticationKeyRingStartupValidator : IHostedService
{
    private readonly IDataProtectionProvider _provider;

    public AuthenticationKeyRingStartupValidator(IDataProtectionProvider provider)
    {
        _provider = provider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var protector = _provider.CreateProtector("ICP.Platform.Authentication.KeyRingStartupProbe.v1");
            var protectedProbe = protector.Protect("key-ring-ready");
            if (!string.Equals(protector.Unprotect(protectedProbe), "key-ring-ready", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("AUTH_KEY_RING_INVALID");
            }
        }
        catch (Exception exception) when (exception is not InvalidOperationException { Message: "AUTH_KEY_RING_INVALID" })
        {
            throw new InvalidOperationException("AUTH_KEY_RING_INVALID", exception);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
