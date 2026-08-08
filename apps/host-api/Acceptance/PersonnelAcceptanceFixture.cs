using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace ICP.Platform.HostApi.Acceptance;

/// <summary>
/// 仅在显式 Acceptance 配置下映射的人员验收夹具。它只经应用服务准备 actor 与组织前置数据，
/// 不参与被测人员写入，也不向常规 Development/Production 暴露固定入口。
/// </summary>
public static class PersonnelAcceptanceFixture
{
    private const string SecretHeader = "X-NewP-Acceptance-Secret";

    public static void MapPersonnelAcceptanceFixture(this WebApplication app, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!app.Environment.IsEnvironment("Acceptance")
            || !configuration.GetValue<bool>("Acceptance:Enabled"))
        {
            return;
        }

        var options = PersonnelAcceptanceFixtureOptions.FromConfiguration(configuration);
        var group = app.MapGroup("/api/acceptance/personnel").AllowAnonymous();

        group.MapPost("/prepare", async (
            HttpContext context,
            IRoleService roleService,
            ICompanyService companyService,
            IDepartmentService departmentService,
            IOptions<LocalAuthOptions> localAuth,
            CancellationToken cancellationToken) =>
        {
            if (!HasExpectedSecret(context, options.Secret))
            {
                return Results.NotFound();
            }

            var role = await roleService.GetRoleByCodeAsync(options.ReadOnlyRoleCode, cancellationToken)
                ?? await roleService.CreateRoleAsync(new RoleDto
                {
                    Code = options.ReadOnlyRoleCode,
                    DisplayName = "Acceptance Read Only",
                    Description = "Ephemeral role for the Personnel acceptance gate"
                }, "acceptance", cancellationToken);
            await roleService.AssignRolePermissionsAsync(role.Id, new AssignRolePermissionsRequest
            {
                PermissionCodes = []
            }, "acceptance", cancellationToken);
            await roleService.AssignUserRolesAsync(options.ReadOnlyUserId, new AssignUserRolesRequest
            {
                RoleCodes = [role.Code]
            }, "acceptance", cancellationToken);

            var company = (await companyService.GetAllAsync(cancellationToken))
                .SingleOrDefault(item => string.Equals(item.Code, options.CompanyCode, StringComparison.Ordinal))
                ?? await companyService.CreateAsync(new CompanyDto
                {
                    Code = options.CompanyCode,
                    DisplayName = options.CompanyName,
                    IsEnabled = true,
                    SortOrder = "10"
                }, cancellationToken);
            var department = (await departmentService.GetByCompanyIdAsync(company.Id, cancellationToken))
                .SingleOrDefault(item => string.Equals(item.Code, options.DepartmentCode, StringComparison.Ordinal))
                ?? await departmentService.CreateAsync(new DepartmentDto
                {
                    Code = options.DepartmentCode,
                    DisplayName = options.DepartmentName,
                    CompanyId = company.Id,
                    ParentDepartmentId = null,
                    IsEnabled = true,
                    SortOrder = "10"
                }, cancellationToken);

            return Results.Ok(new PersonnelAcceptanceFixtureResponse(
                options.RunId,
                localAuth.Value.AdminUserId,
                options.ReadOnlyUserId,
                company.Id,
                company.DisplayName,
                department.Id,
                department.DisplayName));
        });

        group.MapPost("/read-only-login", async (HttpContext context) =>
        {
            if (!HasExpectedSecret(context, options.Secret))
            {
                return Results.NotFound();
            }

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, options.ReadOnlyUserId),
                    new Claim(ClaimTypes.Name, options.ReadOnlyUsername),
                    new Claim("display_name", "Acceptance Read Only")
                ],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = false });
            return Results.Ok(new { userId = options.ReadOnlyUserId });
        });
    }

    private static bool HasExpectedSecret(HttpContext context, string expected)
    {
        if (!context.Request.Headers.TryGetValue(SecretHeader, out var supplied))
        {
            return false;
        }

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied.ToString()));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }
}

internal sealed record PersonnelAcceptanceFixtureOptions(
    string RunId,
    string Secret,
    string ReadOnlyUserId,
    string ReadOnlyUsername,
    string ReadOnlyRoleCode,
    string CompanyCode,
    string CompanyName,
    string DepartmentCode,
    string DepartmentName)
{
    public static PersonnelAcceptanceFixtureOptions FromConfiguration(IConfiguration configuration)
    {
        var runId = Require(configuration["Acceptance:RunId"], "Acceptance:RunId");
        var secret = Require(configuration["Acceptance:Secret"], "Acceptance:Secret");
        var suffix = runId.Length > 12 ? runId[..12] : runId;
        return new PersonnelAcceptanceFixtureOptions(
            runId,
            secret,
            $"acceptance-readonly-{runId}",
            $"acceptance-readonly-{suffix}",
            $"AcceptanceReadOnly{suffix}",
            $"ACC-C-{suffix}",
            $"Personnel Gate Company {suffix}",
            $"ACC-D-{suffix}",
            $"Personnel Gate Department {suffix}");
    }

    private static string Require(string? value, string key) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"ACC_FIXTURE_CONFIG_MISSING: {key} is required.");
}

public sealed record PersonnelAcceptanceFixtureResponse(
    string RunId,
    string AdminUserId,
    string ReadOnlyUserId,
    string CompanyId,
    string CompanyName,
    string DepartmentId,
    string DepartmentName);
