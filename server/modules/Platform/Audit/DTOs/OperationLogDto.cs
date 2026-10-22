namespace ICP.Platform.Modules.Audit.DTOs;

/// <summary>
/// 操作日志DTO
/// </summary>
public class OperationLogDto
{
    public long Id { get; set; }
    public DateTime OperationTime { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string ModuleId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string? OperationTarget { get; set; }
    public string? TargetId { get; set; }
    public string? Description { get; set; }
    public string Result { get; set; } = "Success";
    public string? ErrorMessage { get; set; }
    public string? ClientIp { get; set; }
    public string? ExtensionData { get; set; }
}

/// <summary>
/// 创建操作日志请求
/// </summary>
public class CreateOperationLogRequest
{
    public string ModuleId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string? OperationTarget { get; set; }
    public string? TargetId { get; set; }
    public string? Description { get; set; }
    public string Result { get; set; } = "Success";
    public string? ErrorMessage { get; set; }
    public string? ExtensionData { get; set; }
}

/// <summary>
/// 操作日志查询请求
/// </summary>
public class QueryOperationLogRequest
{
    public string? UserId { get; set; }
    public string? ModuleId { get; set; }
    public string? OperationType { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
