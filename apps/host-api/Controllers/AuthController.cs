using System.Security.Claims;
using ICP.Platform.Authorization.Http;
using ICP.Platform.HostApi.Authentication;
using PlatformAuthorizationService = ICP.Platform.Modules.Authorization.Services.IAuthorizationService;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ICP.Platform.HostApi.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly LocalAuthOptions _options;
    private readonly PlatformAuthorizationService _authorizationService;

    public AuthController(IOptions<LocalAuthOptions> options, PlatformAuthorizationService authorizationService)
    {
        _options = options.Value;
        _authorizationService = authorizationService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthUserResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Username, _options.Username, StringComparison.Ordinal) ||
            !string.Equals(request.Password, _options.Password, StringComparison.Ordinal))
        {
            return Unauthorized(new { message = "Invalid username or password." });
        }

        var permissions = await _authorizationService.GetUserPermissionsAsync(_options.AdminUserId, cancellationToken);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, _options.AdminUserId),
            new(ClaimTypes.Name, _options.Username),
            new("display_name", _options.DisplayName)
        };

        var roles = await _authorizationService.GetUserRolesAsync(_options.AdminUserId, cancellationToken);
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            });

        return Ok(new AuthUserResponse(
            _options.AdminUserId,
            _options.Username,
            _options.DisplayName,
            permissions));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [HttpGet("current-user")]
    [AuthenticatedOnly]
    public async Task<ActionResult<AuthUserResponse>> GetCurrentUser(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var username = User.Identity?.Name ?? _options.Username;
        var displayName = User.FindFirstValue("display_name") ?? _options.DisplayName;
        var permissions = await _authorizationService.GetUserPermissionsAsync(userId, cancellationToken);

        return Ok(new AuthUserResponse(userId, username, displayName, permissions));
    }
}

public sealed record LoginRequest(string Username, string Password);

public sealed record AuthUserResponse(string Id, string Username, string DisplayName, IReadOnlyList<string> Permissions);
