using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Organization.Contracts;
namespace ICP.Platform.Modules.Organization.Directories;
/// <summary>审计当前名称从唯一组织查询取得，历史字段事实仍由 Audit 拥有。</summary>
public sealed class BusinessAuditOrganizationNames(IOrganizationQuery query, string kind, OrganizationRecordKind recordKind) : IBusinessAuditNameDirectory
{
    public string Kind => kind;
    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, string>> FindManyAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
    {
        var view = await query.ReadAsync(cancellationToken);
        return view.ResolveNames(recordKind, ids).ToDictionary(pair => pair.Key, pair => pair.Value.Name, StringComparer.Ordinal);
    }
}
