using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class CompanyCrudDeleteExecutor(EfPlatformDbContext dbContext)
    : IStandardCrudDeleteGuard, IStandardCrudConditionalDeleteExecutor
{
    public string ResourceKey => "organization.company";

    /// <summary>
    /// 在删除前提供公司当前引用状态；最终并发完整性仍由原子谓词和 Restrict 外键兜底。
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

        return await HasReferenceAsync(id, cancellationToken) ? ReferencedError() : null;
    }

    /// <summary>
    /// 在单个数据库删除表达式中组合公司标识、部门与人员引用谓词，并稳定翻译已知约束竞态。
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

        var companies = Companies(descriptor);
        int affected;
        try
        {
            if (dbContext.Database.IsRelational())
            {
                var departments = Departments();
                var persons = Persons();
                affected = await companies
                    .Where(company =>
                        EF.Property<string>(company, descriptor.IdField) == id
                        && !departments.Any(department => EF.Property<string>(department, "CompanyId") == id)
                        && !persons.Any(person => EF.Property<string?>(person, "CompanyId") == id))
                    .ExecuteDeleteAsync(cancellationToken);
            }
            else
            {
                var company = await companies.FirstOrDefaultAsync(
                    row => EF.Property<string>(row, descriptor.IdField) == id,
                    cancellationToken);
                if (company is null || await HasReferenceAsync(id, cancellationToken))
                {
                    affected = 0;
                }
                else
                {
                    companies.Remove(company);
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

        var exists = await companies.AsNoTracking().AnyAsync(
            company => EF.Property<string>(company, descriptor.IdField) == id,
            cancellationToken);
        return exists
            ? StandardCrudMutationResult.Failure(ReferencedError())
            : StandardCrudMutationResult.Failure(NotFound(descriptor, id));
    }

    // Company、Department 与 Person 都由 Organization 持久化边界拥有，引用判断保持数据库侧聚合。
    private async Task<bool> HasReferenceAsync(string id, CancellationToken cancellationToken) =>
        await Departments().AsNoTracking().AnyAsync(
            department => EF.Property<string>(department, "CompanyId") == id,
            cancellationToken)
        || await Persons().AsNoTracking().AnyAsync(
            person => EF.Property<string?>(person, "CompanyId") == id,
            cancellationToken);

    // 公司集合来自 descriptor 的模型名，不形成第二套写入服务。
    private DbSet<Dictionary<string, object?>> Companies(StandardCrudDescriptor descriptor) =>
        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey(descriptor.ModelName));

    // 部门引用无论启用状态都阻止物理删除。
    private DbSet<Dictionary<string, object?>> Departments() =>
        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("Department"));

    // 人员归属引用无论人员状态都阻止物理删除。
    private DbSet<Dictionary<string, object?>> Persons() =>
        dbContext.Set<Dictionary<string, object?>>(dbContext.ResolveModelKey("Person"));

    // 部门或人员引用统一为资源特定冲突，避免暴露约束名称与 provider 文本。
    private static StandardCrudError ReferencedError() => new()
    {
        StatusCode = 409,
        Code = "Organization.Company.DeleteBlockedByReference",
        Message = "公司仍被部门或人员引用，不能删除。",
        ReasonKey = "organization.company.deleteBlockedByReference"
    };

    // 空标识在进入存储表达式前稳定拒绝。
    private static StandardCrudError InvalidIdError() => new()
    {
        StatusCode = 400,
        Code = "Organization.Company.InvalidId",
        Message = "公司标识不能为空。",
        ReasonKey = "organization.company.invalidId"
    };

    // 未命中且公司已不存在时沿用标准 CRUD 的 404 形状。
    private static StandardCrudError NotFound(StandardCrudDescriptor descriptor, string id) => new()
    {
        StatusCode = 404,
        Code = "StandardCrud.NotFound",
        Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 中不存在标识 {id}。",
        ReasonKey = "standardCrud.notFound"
    };
}
