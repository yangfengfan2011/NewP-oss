using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudRepository
{
    /// <summary>
    /// 按资源主键读取详情字段；未找到时返回 null，避免仓储层决定 HTTP 语义。
    /// </summary>
    Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按资源描述符生成选项列表；label 字段组合规则由 descriptor 显式声明。
    /// </summary>
    Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按等值字段检查资源行是否存在；excludedId 用于 update 时排除当前对象。
    /// </summary>
    Task<bool> ExistsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudUniqueConstraint constraint,
        IReadOnlyDictionary<string, object?> equalityValues,
        string? excludedId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建一条标准 CRUD 资源记录；字段白名单由服务层处理，仓储只写入已确认值。
    /// </summary>
    Task<IReadOnlyDictionary<string, object?>> CreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新一条标准 CRUD 资源记录；未找到时返回 null，便于服务层统一生成稳定错误。
    /// </summary>
    Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 以调用方已读取的 long revision 执行条件更新；实现不支持该能力时返回 null，使声明并发保护的资源失败关闭。
    /// </summary>
    Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        long expectedRevision,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, object?>?>(null);

    /// <summary>
    /// 删除一条标准 CRUD 资源记录；删除策略与守卫必须在服务层先完成判断。
    /// </summary>
    Task<bool> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default);
}
