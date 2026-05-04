using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>
/// 人员标准 CRUD 专属校验入口；PersonNo 全局唯一由 descriptor 的 StandardCrudUniqueConstraint 承接。
/// </summary>
public sealed class PersonCrudValidator : IStandardCrudValidator
{
    public string ResourceKey => "organization.person";

    /// <summary>
    /// 创建阶段暂不追加人员专属组合规则，保留入口避免后续规则回流 Controller。
    /// </summary>
    public Task<StandardCrudError?> ValidateCreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<StandardCrudError?>(null);

    /// <summary>
    /// 更新阶段暂不追加人员专属组合规则；PersonNo 只读和唯一性边界由标准 CRUD 内核统一处理。
    /// </summary>
    public Task<StandardCrudError?> ValidateUpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<StandardCrudError?>(null);
}
