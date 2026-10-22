namespace ICP.Platform.Crud.Models;

/// <summary>
/// 标准 CRUD 导出列请求；字段名必须再经过 descriptor 白名单校验，不能直接信任前端。
/// </summary>
public sealed record StandardCrudExportColumn(
    string Key,
    string Field,
    string Title);
