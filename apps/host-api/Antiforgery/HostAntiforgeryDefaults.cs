namespace ICP.Platform.HostApi.Antiforgery;

public static class HostAntiforgeryDefaults
{
    public const string HeaderName = "X-CSRF-TOKEN";
    public const string TokenPath = "/api/auth/antiforgery-token";
}

public static class HostAntiforgeryFailureCodes
{
    public const string InvalidToken = "ANTIFORGERY_TOKEN_INVALID";
}
