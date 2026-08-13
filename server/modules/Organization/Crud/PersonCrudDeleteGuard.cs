using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonCrudDeleteGuard : IStandardCrudDeleteGuard, IStandardCrudConditionalDeleteExecutor
{
    private readonly IPersonUserBindingRepository _bindingRepository;
    private readonly EfPlatformDbContext _dbContext;

    public PersonCrudDeleteGuard(
        IPersonUserBindingRepository bindingRepository,
        EfPlatformDbContext dbContext)
    {
        _bindingRepository = bindingRepository;
        _dbContext = dbContext;
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

    public async Task<StandardCrudMutationResult> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return StandardCrudMutationResult.Failure(InvalidIdError());
        }

        var persons = _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey(descriptor.ModelName));
        var affected = await persons
            .Where(row => EF.Property<string>(row, descriptor.IdField) == id
                && EF.Property<string?>(row, "AccountEID") == null)
            .ExecuteDeleteAsync(cancellationToken);
        if (affected == 1)
        {
            return StandardCrudMutationResult.Success();
        }

        var remaining = await _bindingRepository.FindAsync(id, cancellationToken);
        return remaining?.AccountEid is not null
            ? StandardCrudMutationResult.Failure(BoundError())
            : StandardCrudMutationResult.Failure(new StandardCrudError
            {
                StatusCode = 404,
                Code = "StandardCrud.NotFound",
                Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 中不存在标识 {id}。",
                ReasonKey = "standardCrud.notFound"
            });
    }

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
