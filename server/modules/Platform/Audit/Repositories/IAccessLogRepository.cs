using ICP.Platform.Modules.Audit.Models;

namespace ICP.Platform.Modules.Audit.Repositories;

/// <summary>
/// 访问日志仓储接口
/// </summary>
public interface IAccessLogRepository
{
    Task<long> AddAsync(AccessLog log);
    Task<List<AccessLog>> QueryAsync(
        string? userId,
        string? requestPath,
        int? minStatusCode,
        int? maxStatusCode,
        DateTime? startTime,
        DateTime? endTime,
        int skip,
        int take);
    Task<int> CountAsync(
        string? userId,
        string? requestPath,
        int? minStatusCode,
        int? maxStatusCode,
        DateTime? startTime,
        DateTime? endTime);
}
