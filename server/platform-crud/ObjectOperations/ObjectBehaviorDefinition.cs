namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 声明对象标识、提交协调范围、支持动作及对象拥有者开放横切的整个动作。
/// </summary>
public sealed class ObjectBehaviorDefinition
{
    /// <summary>创建对象拥有者的静态行为声明，并复制动作集合以防注册后漂移。</summary>
    public ObjectBehaviorDefinition(
        string objectKey,
        string ownerModuleId,
        string modelKey,
        IEnumerable<ObjectOperationAction> supportedActions,
        IEnumerable<ObjectOperationAction> openExtensionActions,
        IEnumerable<string>? protectedInputFields = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerModuleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);
        ArgumentNullException.ThrowIfNull(supportedActions);
        ArgumentNullException.ThrowIfNull(openExtensionActions);
        ObjectKey = objectKey;
        OwnerModuleId = ownerModuleId;
        ModelKey = modelKey;
        SupportedActions = supportedActions.Distinct().ToArray();
        OpenExtensionActions = openExtensionActions.Distinct().ToArray();
        ProtectedInputFields = protectedInputFields?
            .Select(static field => field?.Trim() ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
    }

    /// <summary>对象在正式操作入口中的稳定标识。</summary>
    public string ObjectKey { get; }
    /// <summary>拥有对象含义、规则和开放动作的模块。</summary>
    public string OwnerModuleId { get; }
    /// <summary>写入前需要取得协调权的持久化模型键。</summary>
    public string ModelKey { get; }
    /// <summary>对象拥有者实际实现的动作。</summary>
    public IReadOnlyList<ObjectOperationAction> SupportedActions { get; }
    /// <summary>对象拥有者允许其他组件横切的整个动作。</summary>
    public IReadOnlyList<ObjectOperationAction> OpenExtensionActions { get; }
    /// <summary>扩展不能补充、替换或移除的主键及服务端字段。</summary>
    public IReadOnlyList<string> ProtectedInputFields { get; }
}
