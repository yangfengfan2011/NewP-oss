using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>岗位条件删除保留人员引用约束，并通过统一提交入口登记实际删除结果。</summary>
public sealed class PositionCrudDeleteExecutor(
    EfPlatformDbContext dbContext,
    OrganizationConditionalDeleteExecutor conditionalDelete)
    : IStandardCrudDeleteGuard, IStandardCrudConditionalDeleteExecutor
{
    public string ResourceKey => "organization.position";

    /// <summary>
    /// 提供条件删除前的可读判定；最终并发完整性仍由原子删除与数据库外键共同保证。
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

        return await Persons(descriptor)
            .AsNoTracking()
            .AnyAsync(person => EF.Property<string?>(person, "PositionId") == id, cancellationToken)
            ? ReferencedError()
            : null;
    }

    /// <summary>
    /// 在同一存储操作内组合岗位标识、当前人员引用谓词与物理删除，避免先查后删窗口。
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

        var positions = Positions(descriptor);
        var persons = Persons(descriptor);
        return await conditionalDelete.DeleteAsync(
            descriptor,
            id,
            new OrganizationConditionalDeletePolicy(
                (target, persistedId) => target.Where(_ =>
                    !persons.Any(person => EF.Property<string?>(person, "PositionId") == persistedId)),
                async (_, persistedId, token) =>
                {
                    if (persistedId is null)
                    {
                        return StandardCrudMutationResult.Failure(NotFound(descriptor, id));
                    }

                    var exists = await positions.AsNoTracking().AnyAsync(
                        position => EF.Property<string>(position, descriptor.IdField) == persistedId,
                        token);
                    return exists
                        ? StandardCrudMutationResult.Failure(ReferencedError())
                        : StandardCrudMutationResult.Failure(NotFound(descriptor, id));
                }),
            OrganizationConditionalDeleteBehavior.TrackedFallback,
            cancellationToken);
    }

    // Position 与 Person 都由 Organization 拥有，executor 只在模块持久化边界内组合二者。
    private DbSet<Dictionary<string, object?>> Positions(StandardCrudDescriptor descriptor) =>
        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey(descriptor.ModelName));

    // 当前引用谓词只读取 Person 的 PositionId，不引入分配历史或其他生命周期标记。
    private DbSet<Dictionary<string, object?>> Persons(StandardCrudDescriptor descriptor)
    {
        _ = descriptor;
        return dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("Person"));
    }

    // 当前人员引用是可重试的业务冲突，不把数据库约束文本暴露给调用方。
    private static StandardCrudError ReferencedError() => new()
    {
        StatusCode = 409,
        Code = "Organization.Position.DeleteBlockedByPersonReference",
        Message = "岗位当前被人员引用，不能删除。",
        ReasonKey = "organization.position.deleteBlockedByPersonReference"
    };

    // 空标识在进入存储表达式前稳定拒绝。
    private static StandardCrudError InvalidIdError() => new()
    {
        StatusCode = 400,
        Code = "Organization.Position.InvalidId",
        Message = "岗位标识不能为空。",
        ReasonKey = "organization.position.invalidId"
    };

    // 未命中且岗位已不存在时沿用标准 CRUD 的 404 形状。
    private static StandardCrudError NotFound(StandardCrudDescriptor descriptor, string id) => new()
    {
        StatusCode = 404,
        Code = "StandardCrud.NotFound",
        Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 中不存在标识 {id}。",
        ReasonKey = "standardCrud.notFound"
    };
}
