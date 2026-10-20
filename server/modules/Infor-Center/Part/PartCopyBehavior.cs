using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Part;

/// <summary>零组件拥有者核证真实源及完整字段清单，再采用正常创建规则产生独立身份。</summary>
public sealed class PartCopyBehavior(EfPlatformDbContext db, IStandardCrudRepository repository, IStandardCrudResourceRegistry resources,
    IDataExecutionAccess executionAccess, Func<IObjectOperationExecutor> operations) : IObjectBehavior
{
    private static readonly string[] ClassifiedFields = ["EID", "ECODE", "ENAME", "Description", "OwnerId", "OwnerDepartmentId", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"];
    public static readonly ObjectOperationAction Copy = new("copy");
    public static ObjectBehaviorDefinition BehaviorDefinition { get; } = new("part.part.copy", "Part", "Part", [Copy], [],
        ["SourceId", "ExpectedCode", "ExpectedName", "ExpectedDescription"], [new(Copy, new("part.part", ObjectOperationAction.Create))]);
    public ObjectBehaviorDefinition Definition => BehaviorDefinition;

    /// <inheritdoc/>
    public async ValueTask<StandardCrudMutationResult> ExecuteAsync(ObjectOperationContext context, CancellationToken cancellationToken = default)
    {
        var sourceId = (string)context.Values["SourceId"]!;
        var descriptor = resources.FindByResourceKey("part.part")!;
        if ((await executionAccess.EvaluateInternalObjectAsync(PartCopyExecutions.Source(), "visibility", new("part.part", sourceId), cancellationToken)).Decision != DataExecutionDecision.Allowed) return Rejected(404, "Workspace.TargetUnavailable");
        if ((await executionAccess.EvaluateInternalObjectAsync(PartCopyExecutions.Source(), "copy", new("part.part", sourceId), cancellationToken)).Decision != DataExecutionDecision.Allowed) return Rejected(403, "Workspace.ObjectCopyDenied");
        var source = await repository.GetByIdAsync(descriptor, sourceId, cancellationToken);
        if (source is null) return Rejected(404, "Workspace.TargetUnavailable");
        // 全部实际业务字段必须分类；正式字段演进时失败关闭，避免新增业务数据被静默遗漏。
        RequireFieldClassification(db.Model.FindEntityType("Part")!.GetProperties().Select(property => property.Name));
        if (!Equals(source.GetValueOrDefault("ECODE"), context.Values.GetValueOrDefault("ExpectedCode"))
            || !Equals(source.GetValueOrDefault("ENAME"), context.Values.GetValueOrDefault("ExpectedName"))
            || !Equals(source.GetValueOrDefault("Description"), context.Values.GetValueOrDefault("ExpectedDescription")))
            return Rejected(409, "Workspace.CopySourceChanged");
        var values = (IReadOnlyDictionary<string, object?>)context.Values["Values"]!;
        if (values.Keys.Any(key => key is not ("ECODE" or "ENAME" or "Description"))) return Rejected(400, "Workspace.ServerFieldForbidden");
        var result = await operations().ExecuteAsync("part.part", ObjectOperationAction.Create, null, values,
            new ObjectOperationWriteSource.Declared(PartCopyExecutions.Create()), cancellationToken);
        // 普通创建不取得目录图锁，可能在预检后赢得同码竞争；只在真实数据库确认编码重复时定位该字段。
        if (result.Error?.Code == "StandardCrud.PersistenceConflict" && values.GetValueOrDefault("ECODE") is string code
            && await db.Set<Dictionary<string, object?>>("Part").AsNoTracking().AnyAsync(row => EF.Property<string>(row, "ECODE") == code, cancellationToken))
            return StandardCrudMutationResult.Failure(new() { StatusCode = 409, Code = "Part.CodeDuplicated", ReasonKey = "Part.CodeDuplicated",
                Message = "零组件编码必须唯一。", FieldErrors = [new("ECODE", "Part.CodeDuplicated", "零组件编码必须唯一。")] });
        return result;
    }

    // 注册和实际持久模型共用明确清单；字段演进必须先分类，不能只在DTO中增加字段后静默丢失复制数据。
    internal static void RequireFieldClassification(IEnumerable<string> fields)
    {
        if (!fields.ToHashSet(StringComparer.Ordinal).SetEquals(ClassifiedFields)) throw new InvalidOperationException("Part.CopyFieldClassificationRequired");
    }

    // 稳定错误不返回源字段或名称；Workspace 仍负责完整出现位置与最终关系事务。
    private static StandardCrudMutationResult Rejected(int status, string code) => StandardCrudMutationResult.Failure(new()
    { StatusCode = status, Code = code, ReasonKey = code, Message = code });
}
