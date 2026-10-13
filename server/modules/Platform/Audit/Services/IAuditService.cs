using ICP.Platform.Modules.Audit.DTOs;
using ICP.Platform.Modules.Audit.Models;

namespace ICP.Platform.Modules.Audit.Services;

/// <summary>
/// 审计服务接口
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// 记录操作日志
    /// </summary>
    Task<long> LogOperationAsync(CreateOperationLogRequest request, string? userId, string? userName, string? clientIp);

    /// <summary>
    /// 记录访问日志
    /// </summary>
    Task<long> LogAccessAsync(AccessLog accessLog);

    /// <summary>
    /// 查询操作日志
    /// </summary>
    Task<(List<OperationLogDto> Items, int Total)> QueryOperationLogsAsync(QueryOperationLogRequest request);

    /// <summary>
    /// 查询访问日志
    /// </summary>
    Task<(List<AccessLogDto> Items, int Total)> QueryAccessLogsAsync(QueryAccessLogRequest request);
}
