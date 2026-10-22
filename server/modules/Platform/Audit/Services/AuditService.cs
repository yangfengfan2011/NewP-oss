using ICP.Platform.Modules.Audit.DTOs;
using ICP.Platform.Modules.Audit.Models;
using ICP.Platform.Modules.Audit.Repositories;

namespace ICP.Platform.Modules.Audit.Services;

/// <summary>
/// 审计服务实现
/// </summary>
public class AuditService : IAuditService
{
    private readonly IOperationLogRepository _operationLogRepository;
    private readonly IAccessLogRepository _accessLogRepository;

    public AuditService(
        IOperationLogRepository operationLogRepository,
        IAccessLogRepository accessLogRepository)
    {
        _operationLogRepository = operationLogRepository;
        _accessLogRepository = accessLogRepository;
    }

    public async Task<long> LogOperationAsync(
        CreateOperationLogRequest request,
        string? userId,
        string? userName,
        string? clientIp)
    {
        var log = new OperationLog
        {
            OperationTime = DateTime.UtcNow,
            UserId = userId,
            UserName = userName,
            ModuleId = request.ModuleId,
            OperationType = request.OperationType,
            OperationTarget = request.OperationTarget,
            TargetId = request.TargetId,
            Description = request.Description,
            Result = request.Result,
            ErrorMessage = request.ErrorMessage,
            ClientIp = clientIp,
            ExtensionData = request.ExtensionData
        };

        return await _operationLogRepository.AddAsync(log);
    }

    public async Task<long> LogAccessAsync(AccessLog accessLog)
    {
        return await _accessLogRepository.AddAsync(accessLog);
    }

    public async Task<(List<OperationLogDto> Items, int Total)> QueryOperationLogsAsync(
        QueryOperationLogRequest request)
    {
        var skip = (request.PageIndex - 1) * request.PageSize;
        var logs = await _operationLogRepository.QueryAsync(
            request.UserId,
            request.ModuleId,
            request.OperationType,
            request.StartTime,
            request.EndTime,
            skip,
            request.PageSize);

        var total = await _operationLogRepository.CountAsync(
            request.UserId,
            request.ModuleId,
            request.OperationType,
            request.StartTime,
            request.EndTime);

        var items = logs.Select(log => new OperationLogDto
        {
            Id = log.Id,
            OperationTime = log.OperationTime,
            UserId = log.UserId,
            UserName = log.UserName,
            ModuleId = log.ModuleId,
            OperationType = log.OperationType,
            OperationTarget = log.OperationTarget,
            TargetId = log.TargetId,
            Description = log.Description,
            Result = log.Result,
            ErrorMessage = log.ErrorMessage,
            ClientIp = log.ClientIp,
            ExtensionData = log.ExtensionData
        }).ToList();

        return (items, total);
    }

    public async Task<(List<AccessLogDto> Items, int Total)> QueryAccessLogsAsync(
        QueryAccessLogRequest request)
    {
        var skip = (request.PageIndex - 1) * request.PageSize;
        var logs = await _accessLogRepository.QueryAsync(
            request.UserId,
            request.RequestPath,
            request.MinStatusCode,
            request.MaxStatusCode,
            request.StartTime,
            request.EndTime,
            skip,
            request.PageSize);

        var total = await _accessLogRepository.CountAsync(
            request.UserId,
            request.RequestPath,
            request.MinStatusCode,
            request.MaxStatusCode,
            request.StartTime,
            request.EndTime);

        var items = logs.Select(log => new AccessLogDto
        {
            Id = log.Id,
            AccessTime = log.AccessTime,
            UserId = log.UserId,
            UserName = log.UserName,
            RequestPath = log.RequestPath,
            HttpMethod = log.HttpMethod,
            StatusCode = log.StatusCode,
            ResponseTime = log.ResponseTime,
            ClientIp = log.ClientIp,
            UserAgent = log.UserAgent,
            RequestParams = log.RequestParams,
            ExceptionMessage = log.ExceptionMessage
        }).ToList();

        return (items, total);
    }
}
