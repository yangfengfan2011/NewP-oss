using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Modules.Organization.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonCrudDeleteGuard : IStandardCrudDeleteGuard, IStandardCrudConditionalDeleteExecutor
{
    private readonly IPersonUserBindingRepository _bindingRepository;
    private readonly OrganizationConditionalDeleteExecutor _conditionalDelete;
    private readonly Func<IObjectOperationExecutor>? _resolveObjectOperations;
    private readonly IObjectOperationExecutionContext? _executionContext;

    /// <summary>条件删除复用组织统一提交参与者，同时保留永久绑定保护。</summary>
    public PersonCrudDeleteGuard(
        IPersonUserBindingRepository bindingRepository,
        OrganizationConditionalDeleteExecutor conditionalDelete,
        Func<IObjectOperationExecutor> resolveObjectOperations,
        IObjectOperationExecutionContext executionContext)
    {
        _bindingRepository = bindingRepository;
        _conditionalDelete = conditionalDelete;
        _resolveObjectOperations = resolveObjectOperations;
        _executionContext = executionContext;
    }

    // 定向存储测试只调用核心 adapter；正式 DI 构造始终注入统一入口和当前行为身份。
    internal PersonCrudDeleteGuard(
        IPersonUserBindingRepository bindingRepository,
        OrganizationConditionalDeleteExecutor conditionalDelete)
    {
        _bindingRepository = bindingRepository;
        _conditionalDelete = conditionalDelete;
    }

    public string ResourceKey => "organization.person";

    /// <summary>
    /// 条件删除的人员边界：已绑定账号时必须稳定拒绝，避免标准删除绕过永久绑定入口。
    /// </summary>
    public async Task<StandardCrudError?> CanDeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return InvalidIdError();
        }

        var person = await _bindingRepository.FindAsync(id, cancellationToken);
        return person?.AccountEid is null
            ? null
            : BoundError();
    }

    /// <summary>在统一事务内执行原子条件删除并登记实际删除标识。</summary>
    public async Task<StandardCrudMutationResult> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return StandardCrudMutationResult.Failure(InvalidIdError());
        }

        if (_resolveObjectOperations is not null
            && _executionContext?.IsExecuting(ResourceKey, ObjectOperationAction.Delete, id) != true)
        {
            return await _resolveObjectOperations().ExecuteAsync(
                ResourceKey,
                ObjectOperationAction.Delete,
                id,
                new Dictionary<string, object?>(StringComparer.Ordinal),
                cancellationToken);
        }

        return await _conditionalDelete.DeleteAsync(
            descriptor,
            id,
            new OrganizationConditionalDeletePolicy(
                (target, _) => target.Where(row => EF.Property<string?>(row, "AccountEID") == null),
                async (_, persistedId, token) =>
                {
                    if (persistedId is null)
                    {
                        return StandardCrudMutationResult.Failure(NotFound(descriptor, id));
                    }

                    var remaining = await _bindingRepository.FindAsync(persistedId, token);
                    return remaining?.AccountEid is not null
                        ? StandardCrudMutationResult.Failure(BoundError())
                        : StandardCrudMutationResult.Failure(NotFound(descriptor, id));
                }),
            OrganizationConditionalDeleteBehavior.RelationalOnly,
            cancellationToken);
    }

    // 未匹配记录或并发已删除时保留标准 CRUD 的 404 形状。
    private static StandardCrudError NotFound(StandardCrudDescriptor descriptor, string id) => new()
    {
        StatusCode = 404,
        Code = "StandardCrud.NotFound",
        Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 中不存在标识 {id}。",
        ReasonKey = "standardCrud.notFound"
    };

    private static StandardCrudError BoundError() => new()
    {
        StatusCode = 409,
        Code = "Organization.Person.DeleteBlockedByUserBinding",
        Message = "当前人员已绑定账号，不能删除。",
        ReasonKey = "organization.person.deleteBlockedByUserBinding"
    };

    private static StandardCrudError InvalidIdError() => new()
    {
        StatusCode = 400,
        Code = "Organization.Person.InvalidId",
        Message = "人员标识不能为空。",
        ReasonKey = "organization.person.invalidId"
    };
}
