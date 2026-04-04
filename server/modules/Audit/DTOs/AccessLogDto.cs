namespace ICP.Platform.Modules.Audit.DTOs;

/// <summary>
/// 访问日志DTO
/// </summary>
public class AccessLogDto
{
    public long Id { get; set; }
    public DateTime AccessTime { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string RequestPath { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public long ResponseTime { get; set; }
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public string? RequestParams { get; set; }
    public string? ExceptionMessage { get; set; }
}

/// <summary>
/// 访问日志查询请求
/// </summary>
public class QueryAccessLogRequest
{
    public string? UserId { get; set; }
    public string? RequestPath { get; set; }
    public int? MinStatusCode { get; set; }
    public int? MaxStatusCode { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
