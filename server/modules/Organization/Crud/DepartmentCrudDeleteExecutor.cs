using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>部门条件删除保留引用约束，并通过统一提交入口登记实际删除结果。</summary>
public sealed class DepartmentCrudDeleteExecutor(
    EfPlatformDbContext dbContext,
    OrganizationConditionalDeleteExecutor conditionalDelete)
    : IStandardCrudDeleteGuard, IStandardCrudConditionalDeleteExecutor
{
    public string ResourceKey => "organization.department";

    /// <summary>
    /// 在删除前提供当前子部门和人员引用状态；最终并发完整性仍由原子谓词和 Restrict 外键兜底。
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

        if (await HasChildReferenceAsync(descriptor, id, cancellationToken))
        {
            return ChildReferencedError();
        }

        return await HasPersonReferenceAsync(id, cancellationToken) ? PersonReferencedError() : null;
    }

    /// <summary>
    /// 在单个数据库删除表达式中组合部门标识与人员引用谓词，并稳定翻译已知约束竞态。
    /// </summary>
    public async Task<StandardCrudMutationResult> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return StandardCrudMutationResult.Failure(InvalidIdError());
        }

        var departments = Departments(descriptor);
        return await conditionalDelete.DeleteAsync(
            descriptor,
            id,
            new OrganizationConditionalDeletePolicy(
                (target, persistedId) =>
                {
                    var persons = Persons();
                    return target.Where(_ =>
                        !departments.Any(child =>
                            EF.Property<string?>(child, "ParentDepartmentId") == persistedId)
                        && !persons.Any(person => EF.Property<string?>(person, "DepartmentId") == persistedId));
                },
                async (failure, persistedId, token) =>
                {
                    if (failure == OrganizationConditionalDeleteFailure.ConstraintConflict)
                    {
                        return StandardCrudMutationResult.Failure(ReferenceRaceError());
                    }

                    if (persistedId is null)
                    {
                        return StandardCrudMutationResult.Failure(NotFound(descriptor, id));
                    }

                    var exists = await departments.AsNoTracking().AnyAsync(
                        department => EF.Property<string>(department, descriptor.IdField) == persistedId,
                        token);
                    if (!exists)
                    {
                        return StandardCrudMutationResult.Failure(NotFound(descriptor, id));
                    }

                    if (await HasChildReferenceAsync(descriptor, persistedId, token))
                    {
                        return StandardCrudMutationResult.Failure(ChildReferencedError());
                    }

                    return StandardCrudMutationResult.Failure(PersonReferencedError());
                }),
            OrganizationConditionalDeleteBehavior.TrackedFallbackWithConstraintTranslation,
            cancellationToken);
    }

    // 任意状态的直接子部门都阻止物理删除；更深层后代必然经由直接子级形成引用。
    private Task<bool> HasChildReferenceAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken) =>
        Departments(descriptor).AsNoTracking().AnyAsync(
            department => EF.Property<string?>(department, "ParentDepartmentId") == id,
            cancellationToken);

    // 人员引用判断保持数据库侧聚合，停用人员同样阻止物理删除。
    private Task<bool> HasPersonReferenceAsync(string id, CancellationToken cancellationToken) =>
        Persons().AsNoTracking().AnyAsync(
            person => EF.Property<string?>(person, "DepartmentId") == id,
            cancellationToken);

    // 部门集合来自 descriptor 的模型名，不形成第二套写入服务。
    private DbSet<Dictionary<string, object?>> Departments(StandardCrudDescriptor descriptor) =>
        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey(descriptor.ModelName));

    // 人员归属引用无论人员状态都阻止物理删除。
    private DbSet<Dictionary<string, object?>> Persons() =>
        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("Person"));

    // 人员引用转为资源特定冲突，避免暴露约束名称与 provider 文本。
    private static StandardCrudError PersonReferencedError() => new()
    {
        StatusCode = 409,
        Code = "Organization.Department.DeleteBlockedByPerson",
        Message = "部门仍被人员引用，不能删除。",
        ReasonKey = "organization.department.deleteBlockedByPerson"
    };

    // 子部门引用使用独立稳定码，方便管理员区分需要先调整层级的冲突。
    private static StandardCrudError ChildReferencedError() => new()
    {
        StatusCode = 409,
        Code = "Organization.Department.DeleteBlockedByChild",
        Message = "部门仍有子部门，不能删除。",
        ReasonKey = "organization.department.deleteBlockedByChild"
    };

    // 提交期由数据库 Restrict 兜底的竞态不泄漏 provider 约束名称。
    private static StandardCrudError ReferenceRaceError() => new()
    {
        StatusCode = 409,
        Code = "Organization.Department.DeleteBlockedByReference",
        Message = "部门在删除提交时仍被引用，不能删除。",
        ReasonKey = "organization.department.deleteBlockedByReference"
    };

    // 空标识在进入存储表达式前稳定拒绝。
    private static StandardCrudError InvalidIdError() => new()
    {
        StatusCode = 400,
        Code = "Organization.Department.InvalidId",
        Message = "部门标识不能为空。",
        ReasonKey = "organization.department.invalidId"
    };

    // 未命中且部门已不存在时沿用标准 CRUD 的 404 形状。
    private static StandardCrudError NotFound(StandardCrudDescriptor descriptor, string id) => new()
    {
        StatusCode = 404,
        Code = "StandardCrud.NotFound",
        Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 中不存在标识 {id}。",
        ReasonKey = "standardCrud.notFound"
    };
}
