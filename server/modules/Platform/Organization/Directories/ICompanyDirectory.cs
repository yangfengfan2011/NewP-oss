using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.Directories;

/// <summary>
/// Organization 内公司事实的窄读取入口，供人员校验、显示投影、Lookup 与验收夹具复用。
/// </summary>
public interface ICompanyDirectory
{
    /// <summary>按稳定标识读取公司；未找到时返回 null。</summary>
    Task<Company?> GetByIdAsync(string companyId, CancellationToken cancellationToken = default);

    /// <summary>一次读取一组公司并按稳定标识建立索引。</summary>
    Task<IReadOnlyDictionary<string, Company>> GetByIdsAsync(
        IReadOnlyCollection<string> companyIds,
        CancellationToken cancellationToken = default);

    /// <summary>按业务编码读取公司，供受限夹具复用既有前置数据。</summary>
    Task<Company?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>在数据源侧完成关键字、启用状态、稳定排序和分页，并独立解析当前值。</summary>
    Task<OrganizationDirectoryPage<Company>> LookupAsync(
        OrganizationDirectoryLookupRequest request,
        CancellationToken cancellationToken = default);
}
