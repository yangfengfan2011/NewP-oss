using Microsoft.Extensions.Options;

namespace ICP.Platform.HostApi.Authentication;

public sealed class HostAuthenticationOptionsValidator : IValidateOptions<HostAuthenticationOptions>
{
    public const string OptionsInvalid = "AUTH_OPTIONS_INVALID";
    public const string KeyRingInvalid = "AUTH_KEY_RING_INVALID";
    private readonly IWebHostEnvironment _environment;

    public HostAuthenticationOptionsValidator(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, HostAuthenticationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApplicationName)
            || options.LoginAttemptsPerMinute is < 1 or > 10_000
            || options.MaximumConcurrentPasswordVerifications is < 1 or > 1_000)
        {
            return ValidateOptionsResult.Fail(OptionsInvalid);
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
                return ValidateOptionsResult.Fail(KeyRingInvalid);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return ValidateOptionsResult.Fail(KeyRingInvalid);
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
