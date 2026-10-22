using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

/// <summary>
/// 为条件删除资源提供把业务谓词与物理删除合并为同一存储操作的模块 seam。
/// </summary>
public interface IStandardCrudConditionalDeleteExecutor
{
    string ResourceKey { get; }

    /// <summary>
    /// 以单一存储操作原子组合资源标识、模块业务谓词与物理删除。
    /// 恰好删除一行时返回成功；谓词不满足、资源不存在或输入无效时返回带稳定错误的失败结果，
    /// 取消和存储异常则原样传播给调用方。
    /// </summary>
    Task<StandardCrudMutationResult> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default);
}
