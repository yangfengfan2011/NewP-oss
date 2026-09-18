namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 标识对象拥有者声明的一个明确写动作。
/// </summary>
public sealed record ObjectOperationAction
{
    /// <summary>标准创建动作。</summary>
    public static ObjectOperationAction Create { get; } = new("create");
    /// <summary>标准更新动作。</summary>
    public static ObjectOperationAction Update { get; } = new("update");
    /// <summary>标准删除动作。</summary>
    public static ObjectOperationAction Delete { get; } = new("delete");

    /// <summary>创建一个稳定命名的对象动作。</summary>
    public ObjectOperationAction(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>动作的稳定值。</summary>
    public string Value { get; }

    public override string ToString() => Value;
}
