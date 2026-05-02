using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudActionHandler
{
    string ResourceKey { get; }
    string ActionKey { get; }

    /// <summary>
    /// 执行模块注册的标准 CRUD 动作，动作语义保留在代码扩展点内。
    /// </summary>
    Task<StandardCrudMutationResult> ExecuteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default);
}
