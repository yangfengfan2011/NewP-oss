namespace ICP.Platform.HostApi.Authentication;

public sealed class HostAuthenticationOptions
{
    public const string SectionName = "Authentication:Host";

    public string? CookieName { get; set; }

    public string? KeyRingPath { get; set; }

    public string ApplicationName { get; set; } = "ICP.Platform.HostApi.Authentication.v1";

    public bool AllowInsecureDevelopmentCookie { get; set; }

    public bool ProtectKeysAtRest { get; set; } = true;

    public string[] TrustedProxies { get; set; } = [];

    public string[] TrustedNetworks { get; set; } = [];

    public int LoginAttemptsPerMinute { get; set; } = 10;

    public int MaximumConcurrentPasswordVerifications { get; set; } = 20;
}
