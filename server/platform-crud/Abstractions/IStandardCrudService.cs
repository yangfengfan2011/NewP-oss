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

    /// <summary>
    /// 读取标准 CRUD options；首阶段由仓储负责 label 组合，服务层只保留校验入口。
    /// </summary>
    Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default);

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
    /// 停用标准 CRUD 资源；当前阶段先返回稳定未启用错误，避免隐式写入状态字段。
    /// </summary>
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
