namespace ICP.Platform.Modules.Audit.Models;

/// <summary>
/// 操作日志
/// </summary>
public class OperationLog
{
    /// <summary>
    /// 日志ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 操作时间
    /// </summary>
    public DateTime OperationTime { get; set; }

    /// <summary>
    /// 操作用户ID
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// 操作用户名
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 模块ID
    /// </summary>
    public string ModuleId { get; set; } = string.Empty;

    /// <summary>
    /// 操作类型（Create/Update/Delete/Execute等）
    /// </summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>
    /// 操作目标（如：角色、配置项等）
    /// </summary>
    public string? OperationTarget { get; set; }

    /// <summary>
    /// 操作目标ID
    /// </summary>
    public string? TargetId { get; set; }

    /// <summary>
    /// 操作描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 操作结果（Success/Failed）
    /// </summary>
    public string Result { get; set; } = "Success";

    /// <summary>
    /// 错误信息（如果失败）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 客户端IP
    /// </summary>
    public string? ClientIp { get; set; }

    /// <summary>
    /// 扩展数据（JSON格式）
    /// </summary>
    public string? ExtensionData { get; set; }
}
