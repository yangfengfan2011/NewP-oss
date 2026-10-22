using System.Collections.Frozen;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.Ownership;

namespace ICP.Platform.Modules.Organization.Services;

/// <summary>把可信账号和 Organization 当前事实投影为对象归属需要的最小目录。</summary>
public sealed class OrganizationAccountOwnershipDirectory(
    IIdentityAccountDirectory accounts, IOrganizationQuery organization, IDataScopeReadableIds readableIds)
    : IAccountOwnershipDirectory
{
    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<AccountOwnershipCandidate>> SearchAsync(
        string? query, CancellationToken cancellationToken = default)
    {
        var candidates = await accounts.SearchAsync(new IdentityAccountSearch { Query = query, Limit = 100 }, cancellationToken);
        var ownership = await FindManyAsync(candidates.Select(account => account.AccountEid).ToArray(), cancellationToken);
        return candidates.Where(account => ownership.ContainsKey(account.AccountEid))
            .Select(account => new AccountOwnershipCandidate(account.AccountEid, account.ECODE, account.ENAME,
                ownership[account.AccountEid].DepartmentId)).ToArray();
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyDictionary<string, AccountOwnership>> FindManyAsync(
        IReadOnlyCollection<string> accountIds, CancellationToken cancellationToken = default)
    {
        var distinct = accountIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 0) return FrozenDictionary<string, AccountOwnership>.Empty;
        var currentAccounts = await accounts.FindManyAsync(distinct, cancellationToken);
        var view = await organization.ReadAsync(cancellationToken);
        var result = new Dictionary<string, AccountOwnership>(StringComparer.Ordinal);
        foreach (var account in currentAccounts.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (account.Status == IdentityAccountStatus.Deleted) continue;
            var departmentId = account.IsLocalAdministrator
                ? FindAdministratorDepartment(view)
                : view.FindByAccount(account.AccountEid)?.Department?.GetValueOrDefault("EID") as string;
            result.Add(account.AccountEid, new(account.AccountEid, departmentId));
        }
        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyDictionary<string, string>> FindDepartmentNamesAsync(
        IReadOnlyCollection<string> departmentIds, CancellationToken cancellationToken = default)
    {
        var distinct = departmentIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 0) return FrozenDictionary<string, string>.Empty;
        var names = (await organization.ReadAsync(cancellationToken)).ResolveNames(OrganizationRecordKind.Department, distinct);
        var readable = await readableIds.GetReadableIdsAsync("organization.department", distinct, cancellationToken);
        return names.ToFrozenDictionary(pair => pair.Key, pair => readable.Contains(pair.Key) ? pair.Value.Name : "***", StringComparer.Ordinal);
    }

    // 保留 EID 只由显式初始化写入；同名或同编码的普通业务部门不能被推断为管理员默认归属。
    private static string? FindAdministratorDepartment(IOrganizationReadView view)
    {
        var company = view.FindCompanyByCode(OrganizationInitializationService.CompanyCode);
        var department = view.Find(OrganizationRecordKind.Department, OrganizationInitializationService.DepartmentId);
        return company is not null && department is not null
            && Equals(company.GetValueOrDefault("EID"), department.GetValueOrDefault("CompanyId"))
            ? department.GetValueOrDefault("EID") as string : null;
    }
}
