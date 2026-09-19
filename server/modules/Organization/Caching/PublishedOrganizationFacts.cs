using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Organization.Contracts;

namespace ICP.Platform.Modules.Organization.Caching;

/// <summary>仅消费已发布的只读组织视图，缓存异常与取消原样传播。</summary>
internal sealed class PublishedOrganizationFacts(IOrganizationQuery query) : IOrganizationFactsSource
{
    // 单次业务读取只取得一份发布视图。
    public async Task<IReadOnlyDictionary<string, object?>?> FindCompanyAsync(string id, CancellationToken cancellationToken) =>
        (await query.ReadAsync(cancellationToken)).Find(OrganizationRecordKind.Company, id);

    // 先取得可用视图再处理空集合，整个批次使用同一份发布结果。
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FindCompaniesAsync(
        IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        var view = await query.ReadAsync(cancellationToken);
        return ids.Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)
            .Select(id => view.Find(OrganizationRecordKind.Company, id))
            .Where(static row => row is not null).Select(static row => row!).ToArray();
    }

    // 编码规范化及索引规则由发布视图拥有。
    public async Task<IReadOnlyDictionary<string, object?>?> FindCompanyByCodeAsync(string code, CancellationToken cancellationToken) =>
        (await query.ReadAsync(cancellationToken)).FindCompanyByCode(code);

    // 根目标不存在或未启用都返回否，由调用规则维持既有错误语义。
    public async Task<bool> IsCompanyEnabledAsync(string id, CancellationToken cancellationToken) =>
        (await query.ReadAsync(cancellationToken)).Find(OrganizationRecordKind.Company, id)?["IsEnabled"] is true;

    // 公司字段错误由调用规则提供；全部部门祖先在同一视图中判断。
    public async Task<bool> IsDepartmentChainEnabledAsync(string companyId, string departmentId, CancellationToken cancellationToken) =>
        OrganizationTargetChain.IsEnabled(await query.ReadAsync(cancellationToken), companyId, departmentId, cancellationToken);
}
