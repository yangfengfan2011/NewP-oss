namespace ICP.Platform.Crud.Models;

/// <summary>
/// 标准 CRUD 导出文件结果；Endpoint 只负责把该稳定文件外形转成 HTTP 响应。
/// </summary>
public sealed record StandardCrudExportFile(
    string FileName,
    string ContentType,
    byte[] Content);
