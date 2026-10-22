using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 对象拥有者提供的正式写行为。
/// </summary>
public interface IObjectBehavior
{
    /// <summary>对象拥有者的静态行为声明。</summary>
    ObjectBehaviorDefinition Definition { get; }

    /// <summary>执行由统一入口创建的单次操作上下文。</summary>
    ValueTask<StandardCrudMutationResult> ExecuteAsync(
        ObjectOperationContext context,
        CancellationToken cancellationToken = default);
}
