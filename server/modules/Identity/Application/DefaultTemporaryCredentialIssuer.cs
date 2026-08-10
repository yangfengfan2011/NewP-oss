using System.Security.Cryptography;

namespace ICP.Platform.Modules.Identity.Application;

public sealed class DefaultTemporaryCredentialIssuer : ITemporaryCredentialIssuer
{
    private const string SharedDevelopmentCredential = "123456";
    private readonly bool _isDevelopment;
    private readonly bool _useSharedDevelopmentCredential;

    public DefaultTemporaryCredentialIssuer(
        string environmentName,
        bool useSharedDevelopmentCredential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        _isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
        _useSharedDevelopmentCredential = useSharedDevelopmentCredential;
        if (_useSharedDevelopmentCredential && !_isDevelopment)
        {
            throw new InvalidOperationException("IDENTITY_SHARED_TEMPORARY_CREDENTIAL_FORBIDDEN");
        }
    }

    public string Issue()
    {
        if (_useSharedDevelopmentCredential)
        {
            return SharedDevelopmentCredential;
        }

        Span<byte> bytes = stackalloc byte[18];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
