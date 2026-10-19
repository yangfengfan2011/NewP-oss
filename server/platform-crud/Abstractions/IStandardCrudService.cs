using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudService
{
    /// <summary>
    /// 读取标准 CRUD 列表，并在服务层统一触发查询扩展和显示值补齐。
    /// </summary>
    Task<StandardCrudListResult> ListAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取标准 CRUD 详情，并根据 descriptor 声明的摘要块执行 provider 降级编排。
    /// </summary>
    Task<StandardCrudDetail?> GetDetailAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>已验证真实来源的内部调用复用详情SQL与display，不借用独立模块的HTTP声明或功能位置。</summary>
    Task<StandardCrudDetail?> GetDetailFromSourceAsync(StandardCrudDescriptor descriptor, string id,
        ICP.Platform.Authorization.Contracts.DataExecutionInternalReadBinding source,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    /// <summary>
    /// 读取标准 CRUD options；首阶段由仓储负责 label 组合，服务层只保留校验入口。
    /// </summary>
    Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取可搜索、可分页的标准 CRUD lookup，并解析候选页外的当前值。
    /// </summary>
    async Task<StandardCrudLookupResult> LookupAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        var items = await OptionsAsync(
            descriptor,
            new StandardCrudListQuery(
                Page: request.Page,
                PageSize: request.PageSize,
                Filters: string.IsNullOrWhiteSpace(request.Keyword)
                    ? null
                    : new Dictionary<string, string?> { ["keyword"] = request.Keyword }),
            cancellationToken);
        return new StandardCrudLookupResult(items, [], request.Page, request.PageSize, false);
    }

    /// <summary>
    /// 创建标准 CRUD 资源；字段白名单和模块校验在后续写入阶段启用。
    /// </summary>
    Task<StandardCrudMutationResult> CreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新标准 CRUD 资源；字段白名单和模块校验在后续写入阶段启用。
    /// </summary>
    Task<StandardCrudMutationResult> UpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按固定启用方向更新声明的状态字段，复用既有更新校验。
    /// </summary>
    Task<StandardCrudMutationResult> EnableAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default);

    /// <summary>按固定停用方向更新声明的状态字段。</summary>
    Task<StandardCrudMutationResult> DisableAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除标准 CRUD 资源；删除策略和守卫将在后续阶段收口到服务层。
    /// </summary>
    Task<StandardCrudMutationResult> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default);
}
