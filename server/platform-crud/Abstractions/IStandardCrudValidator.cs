using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudValidator
{
    string ResourceKey { get; }

    /// <summary>
    /// 校验创建输入中模块专属的不变量，返回 null 表示允许继续写入。
    /// </summary>
    Task<StandardCrudError?> ValidateCreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验更新输入中模块专属的不变量，返回 null 表示允许继续写入。
    /// </summary>
    Task<StandardCrudError?> ValidateUpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);
}
