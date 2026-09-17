using ICP.Platform.Identity.Contracts;
using ICP.Platform.Organization.Contracts;

namespace ICP.Platform.Modules.Organization.Services;

/// <summary>Organization 拥有业务身份和完整启用链；账号状态由 Identity 提供。</summary>
public sealed class OrganizationAdmissionPolicy(IOrganizationQuery query) : IIdentityAdmissionPolicy
{
    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetCoordinationModels(IdentityAccountSnapshot account) =>
        account.IsLocalAdministrator ? [] : ["Organization/Person"];

    /// <inheritdoc/>
    public async ValueTask<bool> IsAllowedAsync(IdentityAccountSnapshot account, CancellationToken cancellationToken = default)
    {
        if (account.Status != IdentityAccountStatus.Enabled || account.PersonnelDisableHold) return false;
        if (account.IsLocalAdministrator) return true;
        var current = (await query.ReadAsync(cancellationToken)).FindByAccount(account.AccountEid);
        return IsEligible(current);
    }

    // 同一规则用于请求准入与提交前后比较；准备档案可以存储，但不能进入系统。
    internal static bool IsEligible(CurrentOrganization? current)
    {
        if (current is null || !Enabled(current.Person) || !Enabled(current.Company) || !Enabled(current.Department)) return false;
        var companyId = current.Company!.GetValueOrDefault("EID") as string;
        return companyId is not null
            && Equals(current.Person.GetValueOrDefault("CompanyId"), companyId)
            && Equals(current.Person.GetValueOrDefault("DepartmentId"), current.Department!.GetValueOrDefault("EID"))
            && Equals(current.Department.GetValueOrDefault("CompanyId"), companyId)
            && current.Ancestors.All(ancestor => Enabled(ancestor) && Equals(ancestor.GetValueOrDefault("CompanyId"), companyId));
    }

    // 缺失或非启用事实均不能放行；缓存故障由查询入口单独传播。
    private static bool Enabled(IReadOnlyDictionary<string, object?>? row) => row?.GetValueOrDefault("IsEnabled") is true;
}
