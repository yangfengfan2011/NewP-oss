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
    private bool coreActive;
    private bool coreEnded;

    /// <summary>在当前真实core取得一次嵌套创建交接；来源与业务Values分离，不携带允许结果。</summary>
    /// <param name="resourceKey">实际被调用owner的规范资源。</param>
    /// <param name="objectName">该owner的已登记对象名。</param>
    /// <param name="boundary">owner形成完整候选后交给平台的服务端续体。</param>
    public ObjectOperationWriteSource.CreateContinuation ContinueCreate(string resourceKey, string objectName, ObjectCreationBoundary boundary)
    {
        EnsureCoreActive();
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectName);
        ArgumentNullException.ThrowIfNull(boundary);
        return new(this, resourceKey, objectName, boundary);
    }

    // 仅实际executor开启一次core生命周期，嵌套动作不会终止仍在执行的root。
    internal IDisposable ActivateCore()
    {
        if (coreActive || coreEnded) throw new InvalidOperationException("OBJECT_OPERATION_CORE_REUSED");
        coreActive = true;
        return new CoreLease(this);
    }

    // 来源和持久化续体每次调用时重验，不能从保留的上下文跨请求复用。
    internal void EnsureCoreActive()
    {
        if (!coreActive) throw new InvalidOperationException("OBJECT_OPERATION_CORE_EXPIRED");
    }

    private sealed class CoreLease(ObjectOperationContext context) : IDisposable
    {
        /// <inheritdoc/>
        public void Dispose()
        {
            context.coreActive = false;
            context.coreEnded = true;
        }
    }
}
