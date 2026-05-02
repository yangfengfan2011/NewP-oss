using ICP.Platform.Crud.Descriptors;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudDisplayValueResolver
{
    string ResourceKey { get; }

    /// <summary>
    /// 为当前行补齐跨表显示值，避免列表和详情直接耦合外部持久化细节。
    /// </summary>
    Task<IReadOnlyDictionary<string, object?>> ResolveAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> row,
        CancellationToken cancellationToken = default);
}
