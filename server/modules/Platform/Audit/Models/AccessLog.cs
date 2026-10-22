namespace ICP.Platform.Modules.Audit.Models;

/// <summary>
/// 访问日志
/// </summary>
public class AccessLog
{
    /// <summary>
    /// 日志ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 访问时间
    /// </summary>
    public DateTime AccessTime { get; set; }

    /// <summary>
    /// 用户ID
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 请求路径
    /// </summary>
    public string RequestPath { get; set; } = string.Empty;

    /// <summary>
    /// HTTP方法
    /// </summary>
    public string HttpMethod { get; set; } = string.Empty;

    /// <summary>
    /// 状态码
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// 响应时间（毫秒）
    /// </summary>
    public long ResponseTime { get; set; }

    /// <summary>
    /// 客户端IP
    /// </summary>
    public string? ClientIp { get; set; }

    /// <summary>
    /// User Agent
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// 请求参数（可选，敏感信息需过滤）
    /// </summary>
    public string? RequestParams { get; set; }

    /// <summary>
    /// 异常信息（如果有）
    /// </summary>
    public string? ExceptionMessage { get; set; }
}
