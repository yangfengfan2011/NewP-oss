using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudDeleteGuard
{
    string ResourceKey { get; }

    /// <summary>
    /// 在条件删除前执行模块守卫；返回错误时内核必须阻止删除。
    /// </summary>
    Task<StandardCrudError?> CanDeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default);
}
