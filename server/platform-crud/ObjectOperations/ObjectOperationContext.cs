using System.Collections.Frozen;

namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 保存一次对象操作的动作与输入；状态随调用传递，不存放在共享行为实例中。
/// </summary>
public sealed class ObjectOperationContext
{
    // 只允许执行器创建最终输入上下文，调用方不能绕过 before 与保护字段规则。
    internal ObjectOperationContext(
        string objectKey,
        ObjectOperationAction action,
        string? targetId,
        IReadOnlyDictionary<string, object?> values) : this(objectKey, action, targetId, values, null) { }

    // 来源由执行器单独传入；Before扩展只能修改业务输入，不能替换来源。
    internal ObjectOperationContext(string objectKey, ObjectOperationAction action, string? targetId,
        IReadOnlyDictionary<string, object?> values, ObjectOperationWriteSource? source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(values);
        WriteSource = source;
        ObjectKey = objectKey;
        Action = action;
        TargetId = targetId;
        Values = values.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>标准写的正式来源；专用行为使用自己的执行合同，未知调用不能由业务值补入。</summary>
    public ObjectOperationWriteSource? WriteSource { get; }

    /// <summary>当前对象的稳定标识。</summary>
    public string ObjectKey { get; }
    /// <summary>本次执行的明确动作。</summary>
    public ObjectOperationAction Action { get; }
    /// <summary>更新或删除的不可变目标标识；创建时为 null。</summary>
    public string? TargetId { get; }
    /// <summary>本次调用独占的输入副本。</summary>
    public IReadOnlyDictionary<string, object?> Values { get; }
}
