using ICP.Platform.Modules.Organization.Models;

namespace ICP.Platform.Modules.Organization.Directories;

/// <summary>
/// Organization 内部门层级选择的受限读取入口；不会扩大共享 Standard CRUD Lookup 协议。
/// </summary>
public interface IDepartmentHierarchyDirectory
{
    /// <summary>
    /// 在指定公司内分页读取可选上级，并在分页前排除当前部门及其任意深度后代。
    /// </summary>
    Task<OrganizationDirectoryPage<Department>> LookupParentCandidatesAsync(
        string companyId,
        string? currentDepartmentId,
        OrganizationDirectoryLookupRequest request,
        CancellationToken cancellationToken = default);
}
