using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class DepartmentCrudDeleteExecutor(EfPlatformDbContext dbContext)
    : IStandardCrudDeleteGuard, IStandardCrudConditionalDeleteExecutor
{
    public string ResourceKey => "organization.department";

    /// <summary>
    /// 在删除前提供根部门当前人员引用状态；最终并发完整性仍由原子谓词和 Restrict 外键兜底。
    /// </summary>
    public async Task<StandardCrudError?> CanDeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        _ = descriptor;
        if (string.IsNullOrWhiteSpace(id))
        {
            return InvalidIdError();
        }

        return await HasPersonReferenceAsync(id, cancellationToken) ? ReferencedError() : null;
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
        int affected;
        try
        {
            if (dbContext.Database.IsRelational())
            {
                var persons = Persons();
                affected = await departments
                    .Where(department =>
                        EF.Property<string>(department, descriptor.IdField) == id
                        && !persons.Any(person => EF.Property<string?>(person, "DepartmentId") == id))
                    .ExecuteDeleteAsync(cancellationToken);
            }
            else
            {
                var department = await departments.FirstOrDefaultAsync(
                    row => EF.Property<string>(row, descriptor.IdField) == id,
                    cancellationToken);
                if (department is null || await HasPersonReferenceAsync(id, cancellationToken))
                {
                    affected = 0;
                }
                else
                {
                    departments.Remove(department);
                    affected = await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
        }
        catch (Exception exception) when (EfPersistenceConstraintClassifier.IsKnownConstraintConflict(
            dbContext.Database.ProviderName,
            exception))
        {
            return StandardCrudMutationResult.Failure(ReferencedError());
        }

        if (affected == 1)
        {
            return StandardCrudMutationResult.Success();
        }

        var exists = await departments.AsNoTracking().AnyAsync(
            department => EF.Property<string>(department, descriptor.IdField) == id,
            cancellationToken);
        return exists
            ? StandardCrudMutationResult.Failure(ReferencedError())
            : StandardCrudMutationResult.Failure(NotFound(descriptor, id));
    }

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
    private static StandardCrudError ReferencedError() => new()
    {
        StatusCode = 409,
        Code = "Organization.Department.DeleteBlockedByPerson",
        Message = "部门仍被人员引用，不能删除。",
        ReasonKey = "organization.department.deleteBlockedByPerson"
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
