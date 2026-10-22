using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Runtime;

public sealed class StandardCrudEndpoint
{
    private readonly IStandardCrudService _service;

    public StandardCrudEndpoint(IStandardCrudService service)
    {
        _service = service;
    }

    /// <summary>
    /// 将 Controller 的列表请求转交给标准 CRUD 服务，保持 endpoint 门面无业务分支。
    /// </summary>
    public Task<StandardCrudListResult> ListAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default) =>
        _service.ListAsync(descriptor, query, cancellationToken);

    /// <summary>
    /// 将 Controller 的详情请求转交给标准 CRUD 服务，未找到语义由服务返回 null。
    /// </summary>
    public Task<StandardCrudDetail?> GetDetailAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default) =>
        _service.GetDetailAsync(descriptor, id, cancellationToken);

    /// <summary>
    /// 将 options 请求转交给标准 CRUD 服务，避免 Controller 直接依赖仓储。
    /// </summary>
    public Task<StandardCrudLookupResult> OptionsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudLookupRequest request,
        CancellationToken cancellationToken = default) =>
        _service.LookupAsync(descriptor, request, cancellationToken);

    /// <summary>
    /// 将创建请求转交给标准 CRUD 服务，写入能力由服务阶段性开关控制。
    /// </summary>
    public Task<StandardCrudMutationResult> CreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default) =>
        _service.CreateAsync(descriptor, values, cancellationToken);

    /// <summary>
    /// 将更新请求转交给标准 CRUD 服务，字段白名单不在 endpoint 门面处理。
    /// </summary>
    public Task<StandardCrudMutationResult> UpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default) =>
        _service.UpdateAsync(descriptor, id, values, cancellationToken);

    /// <summary>
    /// 将启用请求转交给标准 CRUD 服务，避免 Host 或 Controller 下沉状态切换规则。
    /// </summary>
    public Task<StandardCrudMutationResult> EnableAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default) =>
        _service.EnableAsync(descriptor, id, cancellationToken);

    /// <summary>将固定停用请求交给标准服务。</summary>
    public Task<StandardCrudMutationResult> DisableAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default) =>
        _service.DisableAsync(descriptor, id, cancellationToken);

    /// <summary>
    /// 将删除请求转交给标准 CRUD 服务，删除策略由服务内核统一判定。
    /// </summary>
    public Task<StandardCrudMutationResult> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default) =>
        _service.DeleteAsync(descriptor, id, cancellationToken);
}
