using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace ICP.Platform.HostApi.Authentication;

public interface IHostAuthenticationCookie
{
    string? Read(HttpContext context);

    bool TryUnprotect(string protectedValue, out string credential);

    void Append(HttpContext context, string credential, DateTime expiresAtUtc);

    void Delete(HttpContext context);
}

public sealed class HostAuthenticationCookie : IHostAuthenticationCookie
{
    private const string Purpose = "ICP.Platform.Authentication.SessionCredential.v1";
    private readonly IDataProtector _protector;
    private readonly HostAuthenticationOptions _options;
    private readonly IWebHostEnvironment _environment;

    public HostAuthenticationCookie(
        IDataProtectionProvider provider,
        IOptions<HostAuthenticationOptions> options,
        IWebHostEnvironment environment)
    {
        _protector = provider.CreateProtector(Purpose);
        _options = options.Value;
        _environment = environment;
    }

    public string? Read(HttpContext context) =>
        context.Request.Cookies.TryGetValue(CookieName, out var value) ? value : null;

    public bool TryUnprotect(string protectedValue, out string credential)
    {
        try
        {
            credential = _protector.Unprotect(protectedValue);
            return !string.IsNullOrWhiteSpace(credential);
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or FormatException)
        {
            credential = string.Empty;
            return false;
        }
    }

    public void Append(HttpContext context, string credential, DateTime expiresAtUtc) =>
        context.Response.Cookies.Append(
            CookieName,
            _protector.Protect(credential),
            CookieOptions(expiresAtUtc));

    public void Delete(HttpContext context) =>
        context.Response.Cookies.Delete(CookieName, CookieOptions(DateTime.UnixEpoch));

    private CookieOptions CookieOptions(DateTime expiresAtUtc) => new()
    {
        HttpOnly = true,
        Secure = !_environment.IsDevelopment() || !_options.AllowInsecureDevelopmentCookie,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc)),
        IsEssential = true
    };

    private string CookieName => _options.CookieName
        ?? (_environment.IsProduction() ? "__Host-ICP.Platform.Auth" : "ICP.Platform.Auth");
}
