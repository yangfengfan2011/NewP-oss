namespace ICP.Platform.Crud.Descriptors;

/// <summary>
/// 声明标准 CRUD 资源的唯一约束；这里只表达约束边界，执行流程由运行时服务统一实现。
/// </summary>
public sealed class StandardCrudUniqueConstraint
{
    /// <summary>
    /// 同一资源内唯一的约束标识，用于稳定错误定位和后续模板复用。
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// 参与唯一判断的核心字段；字段含义由资源 descriptor 声明，执行顺序由服务内核控制。
    /// </summary>
    public required IReadOnlyList<string> Fields { get; init; }

    /// <summary>
    /// 可选作用域字段；为空时表示全局唯一，不得用隐式 CompanyId 等约定扩展范围。
    /// </summary>
    public IReadOnlyList<string> ScopeFields { get; init; } = [];

    /// <summary>
    /// 字符串比较是否忽略大小写，仓储和服务候选值都必须遵守同一规则。
    /// </summary>
    public bool CaseInsensitive { get; init; } = true;

    /// <summary>
    /// 字符串比较前是否去除首尾空白，避免模块重复实现输入规范化。
    /// </summary>
    public bool TrimWhitespace { get; init; } = true;

    /// <summary>
    /// 当任一唯一字段缺失或为空时是否跳过本约束，默认保持失败信号显式。
    /// </summary>
    public bool IgnoreWhenAnyFieldMissing { get; init; } = false;

    /// <summary>
    /// 唯一冲突时返回给 API 调用方的稳定错误码。
    /// </summary>
    public required string ErrorCode { get; init; }

    /// <summary>
    /// 唯一冲突时返回给 API 调用方的本地化错误消息。
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// 唯一冲突时返回给前端或日志定位的稳定原因键。
    /// </summary>
    public required string ReasonKey { get; init; }
}
