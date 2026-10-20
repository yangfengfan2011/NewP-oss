using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 对正式对象写入统一施加动作声明、事务协调及可信审计作用域。
/// </summary>
public interface IObjectOperationExecutor
{
    /// <summary>判断对象拥有者是否声明并实现指定动作。</summary>
    bool CanExecute(string objectKey, ObjectOperationAction action);

    /// <summary>通过唯一正式入口执行对象写入；更新和删除使用不可变目标标识，创建时为 null。</summary>
    Task<StandardCrudMutationResult> ExecuteAsync(
        string objectKey,
        ObjectOperationAction action,
        string? targetId,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);
    /// <summary>标准写携带不可改写的正式来源；专用业务动作仍由其实现验证各自合同。</summary>
    Task<StandardCrudMutationResult> ExecuteAsync(string objectKey, ObjectOperationAction action, string? targetId,
        IReadOnlyDictionary<string, object?> values, ObjectOperationWriteSource? source, CancellationToken cancellationToken = default);
}
