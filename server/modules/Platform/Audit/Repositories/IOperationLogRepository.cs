using ICP.Platform.Modules.Audit.Models;

namespace ICP.Platform.Modules.Audit.Repositories;

/// <summary>
/// 操作日志仓储接口
/// </summary>
public interface IOperationLogRepository
{
    Task<long> AddAsync(OperationLog log);
    Task<List<OperationLog>> QueryAsync(
        string? userId,
        string? moduleId,
        string? operationType,
        DateTime? startTime,
        DateTime? endTime,
        int skip,
        int take);
    Task<int> CountAsync(
        string? userId,
        string? moduleId,
        string? operationType,
        DateTime? startTime,
        DateTime? endTime);
}
