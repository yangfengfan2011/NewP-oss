using System.Collections.Frozen;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 为后置扩展提供与行为内部 tracked 容器隔离的输入和结果快照。
/// </summary>
public sealed class ObjectOperationAfterContext
{
    // 从核心行为结果复制 after 可见事实，禁止扩展持有原始容器引用。
    internal ObjectOperationAfterContext(
        string objectKey,
        ObjectOperationAction action,
        IReadOnlyDictionary<string, object?> inputValues,
        StandardCrudMutationResult result)
    {
        ObjectKey = objectKey;
        Action = action;
        InputValues = inputValues.ToFrozenDictionary(StringComparer.Ordinal);
        ResultFields = result.Detail?.Fields.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>目标对象稳定键。</summary>
    public string ObjectKey { get; }
    /// <summary>目标整个动作。</summary>
    public ObjectOperationAction Action { get; }
    /// <summary>核心行为实际消费的最终输入快照。</summary>
    public IReadOnlyDictionary<string, object?> InputValues { get; }
    /// <summary>核心行为成功结果的字段快照；无详情时为 null。</summary>
    public IReadOnlyDictionary<string, object?>? ResultFields { get; }
}
