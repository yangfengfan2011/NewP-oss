using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Authorization.DTOs;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>角色读取共用标准查询与 Entity 显示 owner，公开 DTO 形状保持兼容。</summary>
public sealed class RoleManagementQuery(IStandardCrudService crud, IStandardCrudResourceRegistry resources)
{
    private StandardCrudDescriptor Descriptor => resources.FindByResourceKey("authorization.role")
        ?? throw new InvalidOperationException("Role read resource is unavailable.");

    /// <summary>筛选、稳定排序和分页在数据库执行，当前页审计名称一次批量解析。</summary>
    public async Task<object> ListAsync(string? keyword, int pageIndex, int pageSize, CancellationToken cancellationToken)
    {
        var result = await crud.ListAsync(Descriptor, new StandardCrudListQuery(
            Page: pageIndex, PageSize: pageSize, Filters: new Dictionary<string, string?> { ["query"] = keyword }), cancellationToken);
        return new { items = result.Items, total = result.Total, pageIndex = result.Page, pageSize = result.PageSize,
            sortBy = result.SortBy, sortDirection = result.SortDirection };
    }

    /// <summary>将公共 raw/display 投影映射回既有角色详情契约，不自行格式化时间或解析账号。</summary>
    public async Task<RoleDto?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var detail = await crud.GetDetailAsync(Descriptor, id, cancellationToken);
        if (detail is null) return null;
        var row = detail.Fields;
        return new RoleDto
        {
            Id = (string)row["EID"]!, Code = (string)row["ECODE"]!, DisplayName = (string)row["ENAME"]!,
            Description = row["Description"] as string, IsEnabled = (bool)row["IsEnabled"]!, IsBuiltIn = (bool)row["IsBuiltIn"]!,
            CreatedBy = (string)row["CreatedBy"]!, CreatedAt = (DateTime)row["CreatedAt"]!,
            UpdatedBy = (string)row["UpdatedBy"]!, UpdatedAt = (DateTime)row["UpdatedAt"]!,
            CreatedBy_DisplayValue = row["CreatedBy_DisplayValue"] as string,
            CreatedAt_DisplayValue = row["CreatedAt_DisplayValue"] as string,
            UpdatedBy_DisplayValue = row["UpdatedBy_DisplayValue"] as string,
            UpdatedAt_DisplayValue = row["UpdatedAt_DisplayValue"] as string
        };
    }
}
