namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 保存一次对象操作的动作与输入；状态随调用传递，不存放在共享行为实例中。
/// </summary>
public sealed class ObjectOperationContext
{
    internal ObjectOperationContext(
        string objectKey,
        ObjectOperationAction action,
        IReadOnlyDictionary<string, object?> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(values);
        ObjectKey = objectKey;
        Action = action;
        Values = new Dictionary<string, object?>(values, StringComparer.Ordinal);
    }

    /// <summary>当前对象的稳定标识。</summary>
    public string ObjectKey { get; }
    /// <summary>本次执行的明确动作。</summary>
    public ObjectOperationAction Action { get; }
    /// <summary>本次调用独占的输入副本。</summary>
    public IReadOnlyDictionary<string, object?> Values { get; }
}
