using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.Directories;

/// <summary>
/// Organization 内部门事实的窄读取入口，所有人员消费者显式携带所属公司。
/// </summary>
public interface IDepartmentDirectory
{
    /// <summary>按稳定标识读取部门；未找到时返回 null。</summary>
    Task<Department?> GetByIdAsync(string departmentId, CancellationToken cancellationToken = default);

    /// <summary>一次读取一组部门并按稳定标识建立索引。</summary>
    Task<IReadOnlyDictionary<string, Department>> GetByIdsAsync(
        IReadOnlyCollection<string> departmentIds,
        CancellationToken cancellationToken = default);

    /// <summary>在指定公司内按业务编码读取部门，供受限夹具复用既有前置数据。</summary>
    Task<Department?> GetByCompanyAndCodeAsync(
        string companyId,
        string code,
        CancellationToken cancellationToken = default);

    /// <summary>在指定公司内完成关键字、启用状态、稳定排序和分页，并独立解析当前值。</summary>
    Task<OrganizationDirectoryPage<Department>> LookupAsync(
        string companyId,
        OrganizationDirectoryLookupRequest request,
        CancellationToken cancellationToken = default);
}
