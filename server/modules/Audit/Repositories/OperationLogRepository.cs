using ICP.Platform.Modules.Audit.Models;

namespace ICP.Platform.Modules.Audit.Repositories;

/// <summary>
/// 操作日志仓储内存实现（用于演示，生产环境应使用数据库）
/// </summary>
public class OperationLogRepository : IOperationLogRepository
{
    private readonly List<OperationLog> _logs = new();
    private long _nextId = 1;

    public Task<long> AddAsync(OperationLog log)
    {
        log.Id = _nextId++;
        _logs.Add(log);
        return Task.FromResult(log.Id);
    }

    public Task<List<OperationLog>> QueryAsync(
        string? userId,
        string? moduleId,
        string? operationType,
        DateTime? startTime,
        DateTime? endTime,
        int skip,
        int take)
    {
        var query = _logs.AsQueryable();

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(l => l.UserId == userId);

        if (!string.IsNullOrEmpty(moduleId))
            query = query.Where(l => l.ModuleId == moduleId);

        if (!string.IsNullOrEmpty(operationType))
            query = query.Where(l => l.OperationType == operationType);

        if (startTime.HasValue)
            query = query.Where(l => l.OperationTime >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(l => l.OperationTime <= endTime.Value);

        var result = query
            .OrderByDescending(l => l.OperationTime)
            .Skip(skip)
            .Take(take)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<int> CountAsync(
        string? userId,
        string? moduleId,
        string? operationType,
        DateTime? startTime,
        DateTime? endTime)
    {
        var query = _logs.AsQueryable();

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(l => l.UserId == userId);

        if (!string.IsNullOrEmpty(moduleId))
            query = query.Where(l => l.ModuleId == moduleId);

        if (!string.IsNullOrEmpty(operationType))
            query = query.Where(l => l.OperationType == operationType);

        if (startTime.HasValue)
            query = query.Where(l => l.OperationTime >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(l => l.OperationTime <= endTime.Value);

        return Task.FromResult(query.Count());
    }
}
