namespace ICP.Platform.HostApi.Authentication;

public sealed class LocalAuthOptions
{
    public string AdminUserId { get; set; } = "local-admin";
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "admin123";
    public string DisplayName { get; set; } = "Local Administrator";
    public string AdminRoleCode { get; set; } = "LocalAdmin";
    public string AdminRoleName { get; set; } = "Local Administrator";
    public string CookieName { get; set; } = "ICP.Platform.Auth";
}
