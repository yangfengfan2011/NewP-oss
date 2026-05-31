namespace ICP.Platform.Crud.Models;

/// <summary>
/// 标准 CRUD 导出列表查询；只承载前端当前列表状态，最终字段与分页边界由后端导出服务裁剪。
/// </summary>
public sealed record StandardCrudExportListQuery(
    int PageIndex = 1,
    int PageSize = 20,
    string? SortBy = null,
    string? SortDirection = null,
    IReadOnlyDictionary<string, string?>? Filters = null);

/// <summary>
/// 标准 CRUD 导出请求；mode 表达导出范围，columns 表达当前 view.columns 的运行时投影。
/// </summary>
public sealed record StandardCrudExportRequest(
    string Mode,
    IReadOnlyList<StandardCrudExportColumn> Columns,
    StandardCrudExportListQuery? Query = null);
