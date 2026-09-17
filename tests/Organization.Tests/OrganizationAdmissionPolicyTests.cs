using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.Transactions;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class OrganizationAdmissionPolicyTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("unbound", false)]
    [InlineData("unassigned", false)]
    [InlineData("company-missing", false)]
    [InlineData("department-missing", false)]
    [InlineData("company-mismatch", false)]
    [InlineData("department-mismatch", false)]
    [InlineData("person-disabled", false)]
    [InlineData("company-disabled", false)]
    [InlineData("department-disabled", false)]
    [InlineData("ancestor-disabled", false)]
    [InlineData("root-disabled", false)]
    [InlineData("account-disabled", false)]
    [InlineData("personnel-hold", false)]
    public async Task Admission_RequiresPermanentPersonAndCompleteEnabledOrganization(string condition, bool allowed)
    {
        var person = Row("p"); person["CompanyId"] = "c"; person["DepartmentId"] = "d"; person["AccountEID"] = "account";
        var company = Row("c");
        var department = Row("d"); department["CompanyId"] = "c"; department["ParentDepartmentId"] = "parent";
        var parent = Row("parent"); parent["CompanyId"] = "c"; parent["ParentDepartmentId"] = "root";
        var root = Row("root"); root["CompanyId"] = "c";
        switch (condition)
        {
            case "company-mismatch": department["CompanyId"] = "other"; break;
            case "department-mismatch": person["DepartmentId"] = "other"; break;
            case "person-disabled": person["IsEnabled"] = false; break;
            case "company-disabled": company["IsEnabled"] = false; break;
            case "department-disabled": department["IsEnabled"] = false; break;
            case "ancestor-disabled": parent["IsEnabled"] = false; break;
            case "root-disabled": root["IsEnabled"] = false; break;
        }
        var query = new Query(condition == "unbound" ? null : new(person,
            condition is "unassigned" or "company-missing" ? null : company,
            condition is "unassigned" or "department-missing" ? null : department, [parent, root]));
        var account = Account() with { Status = condition == "account-disabled" ? IdentityAccountStatus.Disabled : IdentityAccountStatus.Enabled,
            PersonnelDisableHold = condition == "personnel-hold" };
        var policy = new OrganizationAdmissionPolicy(query);
        Assert.Equal(allowed, await policy.IsAllowedAsync(account));
        Assert.Equal(["Organization/Person"], policy.GetCoordinationModels(account));
    }

    [Fact]
    public async Task CacheFailure_IsNotMissingIdentityAndLocalAdministratorDoesNotReadOrganization()
    {
        var policy = new OrganizationAdmissionPolicy(new Query(null) { Failure = true });
        await Assert.ThrowsAsync<PlatformCacheUnavailableException>(() => policy.IsAllowedAsync(Account()).AsTask());
        var administrator = Account() with { IsLocalAdministrator = true, IsPersonnelBound = false };
        Assert.True(await policy.IsAllowedAsync(administrator));
        Assert.Empty(policy.GetCoordinationModels(administrator));
    }

    private static IdentityAccountSnapshot Account() => new() { AccountEid = "account", ECODE = "account", ENAME = "账号", Status = IdentityAccountStatus.Enabled, IsPersonnelBound = true };
    private static Dictionary<string, object?> Row(string id) => new() { ["EID"] = id, ["IsEnabled"] = true };
    private sealed class Query(CurrentOrganization? current) : IOrganizationQuery, IOrganizationReadView
    {
        public bool Failure;
        public ValueTask<IOrganizationReadView> ReadAsync(CancellationToken cancellationToken = default) => Failure
            ? throw new PlatformCacheUnavailableException("Organization") : ValueTask.FromResult<IOrganizationReadView>(this);
        public CurrentOrganization? FindByAccount(string accountId) => current;
        public IReadOnlyDictionary<string, object?>? Find(OrganizationRecordKind kind, string id) => throw new NotSupportedException();
        public IReadOnlyDictionary<string, object?>? FindCompanyByCode(string code) => throw new NotSupportedException();
        public IReadOnlyDictionary<string, OrganizationName> ResolveNames(OrganizationRecordKind kind, IReadOnlyCollection<string> ids) => throw new NotSupportedException();
    }
}
