using Microsoft.Extensions.Options;

namespace ICP.Platform.Modules.Identity.Application;

internal sealed class IdentityCredentialOptionsValidator : IValidateOptions<IdentityCredentialOptions>
{
    private readonly bool _isDevelopment;

    public IdentityCredentialOptionsValidator(string environmentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        _isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
    }

    public ValidateOptionsResult Validate(string? name, IdentityCredentialOptions options) =>
        options.UseSharedDevelopmentCredential && !_isDevelopment
            ? ValidateOptionsResult.Fail("IDENTITY_SHARED_TEMPORARY_CREDENTIAL_FORBIDDEN")
            : ValidateOptionsResult.Success;
}
