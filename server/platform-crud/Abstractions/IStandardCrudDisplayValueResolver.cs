using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudDisplayValueResolver
{
    string ResourceKey { get; }

    /// <summary>
    /// 为一批行补齐跨表显示值，避免列表和详情直接耦合外部持久化细节。
    /// 调用方负责决定本次真正请求了哪些 display value 字段，resolver 只做受控解析。
    /// 在模块尚未迁到批量实现前，默认回退到逐行 ResolveAsync，避免平台升级直接打断已注册扩展。
    /// </summary>
    async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
        CancellationToken cancellationToken = default)
    {
        var result = new List<IReadOnlyDictionary<string, object?>>(rows.Count);
        foreach (var row in rows)
        {
            result.Add(await ResolveAsync(descriptor, row, cancellationToken));
        }

        return result;
    }

    /// <summary>
    /// 兼容旧版单行显示值解析器；后续模块完成批量迁移后，可逐步收敛到 ResolveListAsync。
    /// </summary>
    Task<IReadOnlyDictionary<string, object?>> ResolveAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> row,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            $"标准 CRUD 资源 {ResourceKey} 的显示值解析器未实现批量 ResolveListAsync，也未提供兼容的单行 ResolveAsync。");
}
