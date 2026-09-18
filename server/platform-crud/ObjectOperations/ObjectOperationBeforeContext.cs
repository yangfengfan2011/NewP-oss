using System.Collections.Frozen;

namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 为前置扩展提供每次调用独占的输入副本，并阻止扩展改写对象拥有者保护的字段。
/// </summary>
public sealed class ObjectOperationBeforeContext
{
    private readonly Dictionary<string, object?> _values;
    private readonly HashSet<string> _protectedFields;

    // 创建每次调用独占的可控输入，保护字段集合在进入扩展前冻结。
    internal ObjectOperationBeforeContext(
        string objectKey,
        ObjectOperationAction action,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyCollection<string> protectedFields)
    {
        ObjectKey = objectKey;
        Action = action;
        _values = new Dictionary<string, object?>(values, StringComparer.Ordinal);
        _protectedFields = protectedFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>目标对象稳定键。</summary>
    public string ObjectKey { get; }
    /// <summary>目标整个动作。</summary>
    public ObjectOperationAction Action { get; }
    /// <summary>返回当前输入的独立冻结快照，不暴露内部可变容器。</summary>
    public IReadOnlyDictionary<string, object?> Values => SnapshotValues();

    /// <summary>
    /// 补充或替换一个非保护业务字段；主键和审计字段由对象拥有者保留。
    /// </summary>
    public void SetValue(string field, object? value)
    {
        EnsureMutable(field);
        _values[field] = value;
    }

    /// <summary>
    /// 移除一个非保护业务字段；最终必填与业务规则仍由对象行为检查。
    /// </summary>
    public void RemoveValue(string field)
    {
        EnsureMutable(field);
        _values.Remove(field);
    }

    // 在进入对象行为前冻结最终输入，使扩展保留的上下文不能继续影响本次写入。
    internal IReadOnlyDictionary<string, object?> SnapshotValues() =>
        _values.ToFrozenDictionary(StringComparer.Ordinal);

    // 对保护字段采用大小写不敏感比较，避免扩展通过字段拼写绕过保护。
    private void EnsureMutable(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        if (_protectedFields.Contains(field))
        {
            throw new InvalidOperationException(
                $"OBJECT_OPERATION_INPUT_PROTECTED: {ObjectKey}/{Action.Value}/{field}");
        }
    }
}
