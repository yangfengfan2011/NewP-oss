using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Document;

/// <summary>保持标准创建与更新；删除在所属事务先释放使用并解除自有下级。</summary>
public sealed class DocumentObjectBehavior : ObjectBehaviorBase
{
    private readonly EfPlatformDbContext _db;
    private readonly IStandardCrudRepository _repository;
    private readonly IStandardCrudResourceRegistry _resources;
    private readonly IPlatformTransactionExecutor _transactions;
    private readonly DocumentInitialIdentity _identity;
    private readonly DocumentFiles _files;
    public static ObjectBehaviorDefinition BehaviorDefinition { get; } = new("document.document", "Document", "Document",
        [ObjectOperationAction.Create, ObjectOperationAction.Update, ObjectOperationAction.Delete], [],
        ["EID", "OwnerId", "OwnerDepartmentId", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt", "LatestVersionId"]);

    /// <summary>复用标准 writer 的字段、授权和错误裁决，清理保持 Document 所有权。</summary>
    public DocumentObjectBehavior(IStandardCrudResourceRegistry resources, StandardCrudDefaultWriter writer, EfPlatformDbContext db,
        IStandardCrudRepository repository, IPlatformTransactionExecutor transactions, DocumentInitialIdentity identity, DocumentFiles files)
        : base(BehaviorDefinition, resources, writer)
    {
        _db = db; _repository = repository; _resources = resources; _transactions = transactions; _identity = identity; _files = files;
    }

    /// <inheritdoc/>
    protected override async ValueTask<StandardCrudMutationResult> CreateAsync(ObjectOperationContext context, CancellationToken cancellationToken)
    {
        if (RejectIdentityInput(context.Values) is { } rejected) return rejected;
        var result = await base.CreateAsync(context, cancellationToken);
        if (!result.Succeeded) return result;
        // 首版、版次和指针随 master 一起 Flush；原事务内规范重读失败仍整体回滚。
        if (await FlushKnownConflictAsync(cancellationToken) is { } conflict) return StandardCrudMutationResult.Failure(conflict);
        var descriptor = _resources.FindByResourceKey(Definition.ObjectKey)!;
        var row = await _repository.GetByIdAsync(descriptor, (string)result.Detail!.Fields["EID"]!, cancellationToken)
            ?? throw new InvalidOperationException("Document.CreatedIdentityUnavailable");
        return StandardCrudMutationResult.Success(new StandardCrudDetail { Fields = row });
    }

    /// <inheritdoc/>
    protected override async ValueTask<StandardCrudMutationResult> UpdateAsync(ObjectOperationContext context, CancellationToken cancellationToken)
    {
        if (RejectIdentityInput(context.Values) is { } rejected) return rejected;
        var result = await base.UpdateAsync(context, cancellationToken);
        if (!result.Succeeded) return result;
        return await FlushKnownConflictAsync(cancellationToken) is { } conflict ? StandardCrudMutationResult.Failure(conflict) : result;
    }

    // Recorder 只登记变化；所属完整动作负责 Flush，已知数据库约束复用标准错误且仍由外层回滚。
    private async ValueTask<StandardCrudError?> FlushKnownConflictAsync(CancellationToken cancellationToken)
    {
        try { await _transactions.FlushAsync(cancellationToken); return null; }
        catch (DbUpdateException exception) when (EfPersistenceConstraintClassifier.IsKnownConstraintConflict(_db.Database.ProviderName, exception))
        { return StandardCrudDefaultWriter.PersistenceConflict(_resources.FindByResourceKey(Definition.ObjectKey)!); }
    }

    // 业务字段裁剪不能静默接收技术身份；明确拒绝服务端指针和下级身份伪造。
    private static StandardCrudMutationResult? RejectIdentityInput(IReadOnlyDictionary<string, object?> values)
    {
        string[] fields = ["LatestVersionId", "DocumentId", "VersionId", "CurrentIterationId", "VersionOrdinal", "IterationOrdinal"];
        var field = fields.FirstOrDefault(values.ContainsKey);
        return field is null ? null : StandardCrudMutationResult.Failure(new StandardCrudError
        {
            StatusCode = 400, Code = "StandardCrud.ServerOwnedFieldReadOnly", ReasonKey = "standardCrud.serverOwnedFieldReadOnly",
            Message = "图文档版本身份由服务端维护。",
            FieldErrors = [new(field, "StandardCrud.ServerOwnedFieldReadOnly", "图文档版本身份由服务端维护。")]
        });
    }

    /// <inheritdoc/>
    protected override async ValueTask<StandardCrudMutationResult> DeleteAsync(ObjectOperationContext context, CancellationToken cancellationToken)
    {
        if (_db.Database.CurrentTransaction is null) throw new InvalidOperationException("Document.TransactionRequired");
        if (string.IsNullOrWhiteSpace(context.TargetId)
            || await _repository.GetByIdAsync(_resources.FindByResourceKey(Definition.ObjectKey)!, context.TargetId, cancellationToken) is null)
            return await base.DeleteAsync(context, cancellationToken);
        // Workspace 已先裁决位置/read/delete；拥有者在解除任何下级前验证完整当前身份链。
        await _identity.ReadCurrentAsync(context.TargetId, cancellationToken);
        await _files.ReleaseAsync(context.TargetId, cancellationToken);
        // 正式 Link FK 必须先解除；使用释放与关联删除仍在同一事务，失败由外层整体回滚。
        await _transactions.FlushAsync(cancellationToken);
        var versions = _db.Set<Dictionary<string, object?>>("DocumentVersion")
            .Where(row => EF.Property<string>(row, "DocumentId") == context.TargetId);
        var versionIds = versions.Select(row => EF.Property<string>(row, "EID"));
        await _db.Set<Dictionary<string, object?>>("DocumentIteration")
            .Where(row => versionIds.Contains(EF.Property<string>(row, "VersionId"))).ExecuteDeleteAsync(cancellationToken);
        await versions.ExecuteDeleteAsync(cancellationToken);
        DetachDeletedChildren(context.TargetId);
        // 默认删除失败结果由外层对象执行器回滚；单删和后项失败的混合批删共用这一事务。
        return await base.DeleteAsync(context, cancellationToken);
    }

    // ExecuteDelete 不更新 tracker；只解除已在 SQL 删除的本对象下级，保留混合事务其他对象的跟踪变化。
    private void DetachDeletedChildren(string documentId)
    {
        var versions = _db.ChangeTracker.Entries().Where(entry => entry.Metadata.Name == "DocumentVersion"
            && Equals(entry.Property("DocumentId").CurrentValue, documentId)).ToArray();
        var versionIds = versions.Select(entry => (string)entry.Property("EID").CurrentValue!).ToHashSet(StringComparer.Ordinal);
        var iterations = _db.ChangeTracker.Entries().Where(entry => entry.Metadata.Name == "DocumentIteration"
            && entry.Property("VersionId").CurrentValue is string id && versionIds.Contains(id)).ToArray();
        foreach (var entry in iterations) entry.State = EntityState.Detached;
        foreach (var entry in versions) entry.State = EntityState.Detached;
    }
}
