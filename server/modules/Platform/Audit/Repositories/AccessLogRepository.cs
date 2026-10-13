using ICP.Platform.Modules.Audit.Models;

namespace ICP.Platform.Modules.Audit.Repositories;

/// <summary>
/// 访问日志仓储内存实现（用于演示，生产环境应使用数据库）
/// </summary>
public class AccessLogRepository : IAccessLogRepository
{
    private readonly List<AccessLog> _logs = new();
    private long _nextId = 1;

    public Task<long> AddAsync(AccessLog log)
    {
        log.Id = _nextId++;
        _logs.Add(log);
        return Task.FromResult(log.Id);
    }

    public Task<List<AccessLog>> QueryAsync(
        string? userId,
        string? requestPath,
        int? minStatusCode,
        int? maxStatusCode,
        DateTime? startTime,
        DateTime? endTime,
        int skip,
        int take)
    {
        var query = _logs.AsQueryable();

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(l => l.UserId == userId);

        if (!string.IsNullOrEmpty(requestPath))
            query = query.Where(l => l.RequestPath.Contains(requestPath));

        if (minStatusCode.HasValue)
            query = query.Where(l => l.StatusCode >= minStatusCode.Value);

        if (maxStatusCode.HasValue)
            query = query.Where(l => l.StatusCode <= maxStatusCode.Value);

        if (startTime.HasValue)
            query = query.Where(l => l.AccessTime >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(l => l.AccessTime <= endTime.Value);

        var result = query
            .OrderByDescending(l => l.AccessTime)
            .Skip(skip)
            .Take(take)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<int> CountAsync(
        string? userId,
        string? requestPath,
        int? minStatusCode,
        int? maxStatusCode,
        DateTime? startTime,
        DateTime? endTime)
    {
        var query = _logs.AsQueryable();

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(l => l.UserId == userId);

        if (!string.IsNullOrEmpty(requestPath))
            query = query.Where(l => l.RequestPath.Contains(requestPath));

        if (minStatusCode.HasValue)
            query = query.Where(l => l.StatusCode >= minStatusCode.Value);

        if (maxStatusCode.HasValue)
            query = query.Where(l => l.StatusCode <= maxStatusCode.Value);

        if (startTime.HasValue)
            query = query.Where(l => l.AccessTime >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(l => l.AccessTime <= endTime.Value);

        return Task.FromResult(query.Count());
    }
}
