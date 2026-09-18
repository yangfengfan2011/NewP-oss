using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>集中执行 Organization 条件删除的事务协调、物理删除与缓存变更登记。</summary>
public sealed class OrganizationConditionalDeleteExecutor
{
    private readonly EfPlatformDbContext _dbContext;
    private readonly IPlatformTransactionExecutor? _transactions;
    private readonly Func<IObjectOperationExecutor>? _resolveObjectOperations;
    private readonly IObjectOperationExecutionContext? _executionContext;

    /// <summary>正式模块构造延迟解析对象入口，使适配器外部直调仍执行对象规则与横切。</summary>
    public OrganizationConditionalDeleteExecutor(
        EfPlatformDbContext dbContext,
        Func<IObjectOperationExecutor> resolveObjectOperations,
        IObjectOperationExecutionContext executionContext,
        IPlatformTransactionExecutor? transactions = null)
    {
        _dbContext = dbContext;
        _transactions = transactions;
        _resolveObjectOperations = resolveObjectOperations;
        _executionContext = executionContext;
    }

    // 定向存储测试只执行条件删除核心；正式 DI 始终注入统一入口与当前行为身份。
    internal OrganizationConditionalDeleteExecutor(
        EfPlatformDbContext dbContext,
        IPlatformTransactionExecutor? transactions = null)
    {
        _dbContext = dbContext;
        _transactions = transactions;
    }

    /// <summary>在同一协调范围内解析实际主键、执行条件删除、解释失败并登记成功变更。</summary>
    internal async Task<StandardCrudMutationResult> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string requestedId,
        OrganizationConditionalDeletePolicy policy,
        OrganizationConditionalDeleteBehavior behavior,
        CancellationToken cancellationToken)
    {
        if (_resolveObjectOperations is not null
            && _executionContext?.IsExecuting(
                descriptor.ResourceKey,
                ObjectOperationAction.Delete,
                requestedId) != true)
        {
            var objectOperations = _resolveObjectOperations();
            if (objectOperations.CanExecute(descriptor.ResourceKey, ObjectOperationAction.Delete))
            {
                return await objectOperations.ExecuteAsync(
                    descriptor.ResourceKey,
                    ObjectOperationAction.Delete,
                    requestedId,
                    new Dictionary<string, object?>(StringComparer.Ordinal),
                    cancellationToken);
            }
        }

        if (_transactions is { IsActive: false } && _transactions.HasParticipants(descriptor.ModelName))
        {
            return await _transactions.ExecuteAsync(
                [descriptor.ModelName],
                token => new ValueTask<StandardCrudMutationResult>(DeleteAsync(
                    descriptor,
                    requestedId,
                    policy,
                    behavior,
                    token)),
                cancellationToken);
        }

        var records = _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey(descriptor.ModelName));
        var persistedId = await records.AsNoTracking()
            .Where(record => EF.Property<string>(record, descriptor.IdField) == requestedId)
            .Select(record => EF.Property<string>(record, descriptor.IdField))
            .SingleOrDefaultAsync(cancellationToken);
        if (persistedId is null)
        {
            return await policy.ExplainFailureAsync(
                OrganizationConditionalDeleteFailure.NotFound,
                null,
                cancellationToken);
        }

        var target = records.Where(
            record => EF.Property<string>(record, descriptor.IdField) == persistedId);
        var deletable = policy.ApplyCondition(target, persistedId);
        int affected;
        try
        {
            if (_dbContext.Database.IsRelational()
                || behavior == OrganizationConditionalDeleteBehavior.RelationalOnly)
            {
                affected = await deletable.ExecuteDeleteAsync(cancellationToken);
            }
            else
            {
                var record = await deletable.FirstOrDefaultAsync(cancellationToken);
                if (record is null)
                {
                    affected = 0;
                }
                else
                {
                    records.Remove(record);
                    affected = await _dbContext.SaveChangesAsync(cancellationToken);
                }
            }
        }
        catch (Exception exception) when (
            behavior == OrganizationConditionalDeleteBehavior.TrackedFallbackWithConstraintTranslation
            && EfPersistenceConstraintClassifier.IsKnownConstraintConflict(
                _dbContext.Database.ProviderName,
                exception))
        {
            return await policy.ExplainFailureAsync(
                OrganizationConditionalDeleteFailure.ConstraintConflict,
                persistedId,
                cancellationToken);
        }

        if (affected != 1)
        {
            return await policy.ExplainFailureAsync(
                OrganizationConditionalDeleteFailure.NotDeleted,
                persistedId,
                cancellationToken);
        }

        if (_transactions?.HasParticipants(descriptor.ModelName) is true)
        {
            _transactions.RegisterChange(new PlatformRecordChange(descriptor.ModelName, persistedId, null, null));
        }

        return StandardCrudMutationResult.Success();
    }
}

internal enum OrganizationConditionalDeleteBehavior
{
    RelationalOnly,
    TrackedFallback,
    TrackedFallbackWithConstraintTranslation
}

internal enum OrganizationConditionalDeleteFailure
{
    NotFound,
    NotDeleted,
    ConstraintConflict
}

/// <summary>由资源 adapter 提供条件删除的业务谓词及失败解释。</summary>
internal sealed class OrganizationConditionalDeletePolicy(
    Func<IQueryable<Dictionary<string, object?>>, string, IQueryable<Dictionary<string, object?>>> applyCondition,
    Func<OrganizationConditionalDeleteFailure, string?, CancellationToken, Task<StandardCrudMutationResult>> explainFailure)
{
    /// <summary>向已按实际持久化主键收窄的目标查询附加资源业务条件。</summary>
    public IQueryable<Dictionary<string, object?>> ApplyCondition(
        IQueryable<Dictionary<string, object?>> target,
        string persistedId) => applyCondition(target, persistedId);

    /// <summary>在当前协调与事务内把删除失败解释为资源稳定错误。</summary>
    public Task<StandardCrudMutationResult> ExplainFailureAsync(
        OrganizationConditionalDeleteFailure failure,
        string? persistedId,
        CancellationToken cancellationToken) => explainFailure(failure, persistedId, cancellationToken);
}
