using System.Security.Cryptography;

namespace ICP.Platform.Modules.Authentication.Application;

public interface IAuthenticationSessionCredentialGenerator
{
    string Issue();
}

public sealed class DefaultAuthenticationSessionCredentialGenerator : IAuthenticationSessionCredentialGenerator
{
    public string Issue()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
