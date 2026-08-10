using Microsoft.Extensions.Options;

namespace ICP.Platform.HostApi.Authentication;

public sealed class HostAuthenticationOptionsValidator : IValidateOptions<HostAuthenticationOptions>
{
    private readonly IWebHostEnvironment _environment;

    public HostAuthenticationOptionsValidator(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, HostAuthenticationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApplicationName)
            || options.LoginAttemptsPerMinute != 10
            || options.MaximumConcurrentPasswordVerifications != 20)
        {
            return ValidateOptionsResult.Fail("AUTH_KEY_RING_INVALID");
        }

        if (!_environment.IsProduction())
        {
            return ValidateOptionsResult.Success;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(options.KeyRingPath)
                || !Path.IsPathFullyQualified(options.KeyRingPath)
                || IsUnderInstallDirectory(options.KeyRingPath)
                || !options.ProtectKeysAtRest
                || !OperatingSystem.IsWindows()
                || !string.Equals(options.CookieName, "__Host-ICP.Platform.Auth", StringComparison.Ordinal))
            {
                return ValidateOptionsResult.Fail("AUTH_KEY_RING_INVALID");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return ValidateOptionsResult.Fail("AUTH_KEY_RING_INVALID");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsUnderInstallDirectory(string keyRingPath)
    {
        var install = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var keyRing = Path.GetFullPath(keyRingPath)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return keyRing.StartsWith(install, StringComparison.OrdinalIgnoreCase);
    }
}
