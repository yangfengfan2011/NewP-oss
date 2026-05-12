namespace ICP.Platform.Crud.Descriptors;

/// <summary>
/// 声明标准 CRUD 资源的写入必填字段；这里只表达字段、作用范围和错误契约，缺失判断由运行时内核执行。
/// </summary>
public sealed class StandardCrudRequiredField
{
    /// <summary>
    /// 必填字段名；必须来自当前资源 descriptor 的标准字段集合，避免形成隐式字段契约。
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    /// 必填规则适用的写入阶段；更新阶段只校验本次提交字段是否被清空。
    /// </summary>
    public StandardCrudRequiredFieldScope Scope { get; init; } = StandardCrudRequiredFieldScope.CreateAndUpdate;

    /// <summary>
    /// 必填失败时返回给 API 调用方的稳定错误码。
    /// </summary>
    public required string ErrorCode { get; init; }

    /// <summary>
    /// 必填失败时返回给 API 调用方的本地化错误消息。
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// 必填失败时返回给前端或日志定位的稳定原因键。
    /// </summary>
    public required string ReasonKey { get; init; }
}
