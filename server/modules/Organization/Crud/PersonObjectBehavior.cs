using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Persistence.Auditing;
using System.Diagnostics;

namespace ICP.Platform.Modules.Organization.Crud;

/// <summary>
/// 统一承载人员普通 CRUD 与独立启用、停用、永久账号绑定动作。
/// </summary>
public sealed class PersonObjectBehavior : ObjectBehaviorBase
{
    /// <summary>人员独立启用动作。</summary>
    public static ObjectOperationAction EnableAction { get; } = new("enable");
    /// <summary>人员独立停用动作。</summary>
    public static ObjectOperationAction DisableAction { get; } = new("disable");
    /// <summary>人员永久绑定账号动作。</summary>
    public static ObjectOperationAction BindUserAction { get; } = new("bind-user");

    /// <summary>人员全部现有正式写入共享统一行为与前后横切。</summary>
    public static ObjectBehaviorDefinition BehaviorDefinition { get; } = new(
        "organization.person",
        "Organization",
        "Organization/Person",
        [
            ObjectOperationAction.Create,
            ObjectOperationAction.Update,
            ObjectOperationAction.Delete,
            EnableAction,
            DisableAction,
            BindUserAction
        ],
        [
            ObjectOperationAction.Create,
            ObjectOperationAction.Update,
            ObjectOperationAction.Delete,
            EnableAction,
            DisableAction,
            BindUserAction
        ],
        ["EID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"]);

    private readonly PersonLifecycleService _lifecycle;
    private readonly PersonUserBindingService _binding;
    private readonly PersonObjectActionInvocationContext _invocations;
    private readonly IEntityAuditValueProvider _auditValues;

    /// <summary>组合公共 CRUD 核心与人员专用动作核心，不保存单次调用状态。</summary>
    public PersonObjectBehavior(
        IStandardCrudResourceRegistry resources,
        StandardCrudDefaultWriter defaultWriter,
        PersonLifecycleService lifecycle,
        PersonUserBindingService binding,
        PersonObjectActionInvocationContext invocations,
        IEntityAuditValueProvider auditValues)
        : base(BehaviorDefinition, resources, defaultWriter)
    {
        _lifecycle = lifecycle;
        _binding = binding;
        _invocations = invocations;
        _auditValues = auditValues;
    }

    /// <inheritdoc />
    protected override async ValueTask<StandardCrudMutationResult> ExecuteDeclaredActionAsync(
        ObjectOperationContext context,
        CancellationToken cancellationToken)
    {
        if (context.Action == EnableAction || context.Action == DisableAction)
        {
            return await ExecuteLifecycleAsync(context, cancellationToken);
        }
        if (context.Action == BindUserAction)
        {
            return await ExecuteBindingAsync(context, cancellationToken);
        }
        return await base.ExecuteDeclaredActionAsync(context, cancellationToken);
    }

    // 动作方向来自不可变 action identity；before 只能规范化原因，不能把启用改成停用。
    private async ValueTask<StandardCrudMutationResult> ExecuteLifecycleAsync(
        ObjectOperationContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _lifecycle.ExecuteCoreAsync(
                RequireTarget(context),
                new SetPersonLifecycleStatusRequest(
                    context.Action == EnableAction,
                    ReadString(context.Values, "Reason")),
                ResolveLifecycleActor(),
                cancellationToken);
            return Success(result.Dto, result.Person);
        }
        catch (PersonLifecycleException exception)
        {
            return StandardCrudMutationResult.Failure(ToError(exception));
        }
    }

    // 永久绑定只消费 before 后的业务字段；可信 actor 保持在独立服务端调用作用域。
    private async ValueTask<StandardCrudMutationResult> ExecuteBindingAsync(
        ObjectOperationContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _binding.ExecuteCoreAsync(
                RequireBindingTarget(context),
                new BindPersonUserRequest(
                    ReadString(context.Values, "AccountEid"),
                    ReadInt64(context.Values, "ExpectedRevision"),
                    ReadString(context.Values, "Reason")),
                ResolveBindingActor(),
                cancellationToken);
            return Success(result.Dto, result.Person);
        }
        catch (PersonUserBindingException exception)
        {
            return StandardCrudMutationResult.Failure(ToError(exception));
        }
    }

    // 行为结果同时携带原 DTO 与实际 Person 审计事实，供服务适配器和 after 扩展消费同一结果。
    private static StandardCrudMutationResult Success(object dto, PersonUserBindingState person) =>
        StandardCrudMutationResult.Success(new StandardCrudDetail
        {
            Fields = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Result"] = dto,
                ["EID"] = person.PersonEid,
                ["CreatedBy"] = person.CreatedBy,
                ["CreatedAt"] = person.CreatedAt,
                ["UpdatedBy"] = person.UpdatedBy,
                ["UpdatedAt"] = person.UpdatedAt
            }
        });

    // 专用动作必须携带不可变 Person 标识。
    private static string RequireTarget(ObjectOperationContext context) =>
        !string.IsNullOrWhiteSpace(context.TargetId)
            ? context.TargetId
            : throw new PersonLifecycleException(PersonLifecycleFailureCodes.InvalidRequest);

    // 绑定动作缺少目标时沿用绑定服务的稳定输入失败。
    private static string RequireBindingTarget(ObjectOperationContext context) =>
        !string.IsNullOrWhiteSpace(context.TargetId)
            ? context.TargetId
            : throw new PersonUserBindingException(PersonUserBindingFailureCodes.InvalidRequest);

    // before 扩展可以规范化字符串，但缺失或错误类型由对象核心收敛为稳定输入失败。
    private static string ReadString(IReadOnlyDictionary<string, object?> values, string field) =>
        values.TryGetValue(field, out var value) && value is string text
            ? text
            : string.Empty;

    // 账号 revision 保留现有 long 协议，不引入新的并发字段或转换规则。
    private static long ReadInt64(IReadOnlyDictionary<string, object?> values, string field) =>
        values.TryGetValue(field, out var value) && value is long number
            ? number
            : 0L;

    // 旧服务调用保留显式 actor/trace；直接公共入口从同一审计作用域取得可信主体。
    private PersonLifecycleActor ResolveLifecycleActor() =>
        _invocations.Current<PersonLifecycleActor>()
        ?? new PersonLifecycleActor(_auditValues.Current().ActorId, CurrentTraceId());

    // 永久绑定与生命周期使用同一可信主体规则，不能从 before 可修改值构造 actor。
    private PersonUserBindingActor ResolveBindingActor() =>
        _invocations.Current<PersonUserBindingActor>()
        ?? new PersonUserBindingActor(_auditValues.Current().ActorId, CurrentTraceId());

    // 非 HTTP 直接调用使用 Activity 标识；没有 Activity 时仍提供稳定非空的服务端 trace。
    private static string CurrentTraceId() =>
        Activity.Current?.Id ?? "object-operation";

    // 生命周期稳定失败沿用现有 HTTP 状态、code 和 current 形状。
    private static StandardCrudError ToError(PersonLifecycleException exception) => new()
    {
        StatusCode = exception.StatusCode,
        Code = exception.FailureCode,
        Message = exception.Message,
        Args = exception.Current is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal) { ["current"] = exception.Current }
    };

    // 绑定稳定失败沿用现有 HTTP 状态、code 和 current 形状。
    private static StandardCrudError ToError(PersonUserBindingException exception) => new()
    {
        StatusCode = exception.StatusCode,
        Code = exception.FailureCode,
        Message = exception.Message,
        Args = exception.Current is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal) { ["current"] = exception.Current }
    };
}
