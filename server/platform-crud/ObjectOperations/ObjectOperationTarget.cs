namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 标识对象行为或扩展可能调用的另一个正式对象操作。
/// </summary>
public sealed record ObjectOperationTarget
{
    /// <summary>创建一个静态目标操作声明。</summary>
    public ObjectOperationTarget(string objectKey, ObjectOperationAction action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(action);
        ObjectKey = objectKey;
        Action = action;
    }

    /// <summary>目标对象稳定键。</summary>
    public string ObjectKey { get; }

    /// <summary>目标对象的整个动作。</summary>
    public ObjectOperationAction Action { get; }
}

/// <summary>
/// 把对象行为自身的源动作与可能调用的目标操作关联。
/// </summary>
public sealed record ObjectOperationCall
{
    /// <summary>创建一个按源动作生效的静态调用声明。</summary>
    public ObjectOperationCall(ObjectOperationAction sourceAction, ObjectOperationTarget target)
    {
        ArgumentNullException.ThrowIfNull(sourceAction);
        ArgumentNullException.ThrowIfNull(target);
        SourceAction = sourceAction;
        Target = target;
    }

    /// <summary>发生调用的对象动作。</summary>
    public ObjectOperationAction SourceAction { get; }

    /// <summary>可能进入的目标对象操作。</summary>
    public ObjectOperationTarget Target { get; }
}
