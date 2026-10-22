using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudDisplayValueResolver
{
    string ResourceKey { get; }

    /// <summary>
    /// 批量解析本次请求的业务显示字段；CRUD 与 Link 共用此入口，不传入路由或写入策略。
    /// 返回行与输入按位置对应，每行只包含请求的 companion，值为 string 或 null；不得改写输入。
    /// </summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
        CancellationToken cancellationToken = default);
}
