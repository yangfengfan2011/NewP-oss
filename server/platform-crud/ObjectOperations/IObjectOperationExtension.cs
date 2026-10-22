using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.ObjectOperations;

/// <summary>
/// 由组件贡献到对象拥有者已开放动作的前后横切实现。
/// </summary>
public interface IObjectOperationExtension
{
    /// <summary>
    /// 在核心行为前补充受控输入或返回稳定错误拒绝整次操作。
    /// </summary>
    /// <param name="context">只允许通过受控方法修改非保护字段的前置上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>允许继续时为 null；拒绝时为稳定错误。</returns>
    ValueTask<StandardCrudError?> BeforeAsync(
        ObjectOperationBeforeContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 在核心行为完成后、数据库提交前观察不可变输入与结果快照。
    /// </summary>
    /// <param name="context">不暴露 tracked 输入或结果容器的后置上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask AfterAsync(
        ObjectOperationAfterContext context,
        CancellationToken cancellationToken = default);
}
