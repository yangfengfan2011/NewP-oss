using System.Security.Cryptography;
using System.Text;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Authorization;

namespace ICP.Platform.HostApi.Acceptance;

/// <summary>
/// 仅在显式 Acceptance 配置下映射的人员验收夹具。它只经应用服务准备组织前置数据，
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
            ICompanyService companyService,
            IDepartmentService departmentService,
            IIdentityLocalAdministratorReadinessProbe localAdministrator,
            CancellationToken cancellationToken) =>
        {
            if (!HasExpectedSecret(context, options.Secret))
            {
                return Results.NotFound();
            }

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

            var administrator = await localAdministrator.InspectAsync(cancellationToken);
            if (administrator is not { State: LocalAdministratorReadinessState.Ready, AccountEid: not null })
            {
                return Results.Problem(statusCode: 503, title: "IDENTITY_LOCAL_ADMIN_INVARIANT");
            }

            return Results.Ok(new PersonnelAcceptanceFixtureResponse(
                options.RunId,
                administrator.AccountEid,
                company.Id,
                company.DisplayName,
                department.Id,
                department.DisplayName));
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
    string CompanyId,
    string CompanyName,
    string DepartmentId,
    string DepartmentName);
