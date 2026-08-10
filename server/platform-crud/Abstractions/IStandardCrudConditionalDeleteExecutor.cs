using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

/// <summary>
/// 为条件删除资源提供把业务谓词与物理删除合并为同一存储操作的模块 seam。
/// </summary>
public interface IStandardCrudConditionalDeleteExecutor
{
    string ResourceKey { get; }

    Task<StandardCrudMutationResult> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default);
}
