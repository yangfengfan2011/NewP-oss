using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonCrudDeleteGuard : IStandardCrudDeleteGuard, IStandardCrudConditionalDeleteExecutor
{
    private readonly IPersonUserBindingRepository _bindingRepository;
    private readonly EfPlatformDbContext _dbContext;
    private readonly IPlatformTransactionExecutor? _transactions;

    /// <summary>条件删除复用组织统一提交参与者，同时保留永久绑定保护。</summary>
    public PersonCrudDeleteGuard(
        IPersonUserBindingRepository bindingRepository,
        EfPlatformDbContext dbContext,
        IPlatformTransactionExecutor? transactions = null)
    {
        _bindingRepository = bindingRepository;
        _dbContext = dbContext;
        _transactions = transactions;
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

        if (_transactions is { IsActive: false } && _transactions.HasParticipants(descriptor.ModelName))
            return await _transactions.ExecuteAsync([descriptor.ModelName],
                token => new ValueTask<StandardCrudMutationResult>(DeleteAsync(descriptor, id, token)), cancellationToken);

        var persons = _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey(descriptor.ModelName));
        // 数据库可接受主键别名；后续谓词和缓存登记都使用实际持久化标识。
        var persistedId = await persons.AsNoTracking()
            .Where(row => EF.Property<string>(row, descriptor.IdField) == id)
            .Select(row => EF.Property<string>(row, descriptor.IdField))
            .SingleOrDefaultAsync(cancellationToken);
        if (persistedId is null)
            return StandardCrudMutationResult.Failure(NotFound(descriptor, id));

        var affected = await persons
            .Where(row => EF.Property<string>(row, descriptor.IdField) == persistedId
                && EF.Property<string?>(row, "AccountEID") == null)
            .ExecuteDeleteAsync(cancellationToken);
        if (affected == 1)
        {
            if (_transactions?.HasParticipants(descriptor.ModelName) is true)
                _transactions.RegisterChange(new PlatformRecordChange(descriptor.ModelName, persistedId, null, null));
            return StandardCrudMutationResult.Success();
        }

        var remaining = await _bindingRepository.FindAsync(persistedId, cancellationToken);
        return remaining?.AccountEid is not null
            ? StandardCrudMutationResult.Failure(BoundError())
            : StandardCrudMutationResult.Failure(NotFound(descriptor, id));
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
