using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Persistence.Transactions;

namespace ICP.Platform.Modules.Organization.Services;

/// <summary>将既有人员账号协议适配到统一对象入口，查询复用内部绑定核心。</summary>
public sealed class PersonUserBindingService : IPersonUserBindingService
{
    private readonly PersonUserBindingActionCore _core;
    private readonly Func<IObjectOperationExecutor> _resolveObjectOperations;
    private readonly PersonObjectActionInvocationContext _invocations;

    // 扩展可依赖旧服务，延迟解析执行器避免装配回环；写入口不存在直达核心的构造旁路。
    internal PersonUserBindingService(
        PersonUserBindingActionCore core,
        Func<IObjectOperationExecutor> resolveObjectOperations,
        PersonObjectActionInvocationContext invocations)
    {
        _core = core;
        _resolveObjectOperations = resolveObjectOperations;
        _invocations = invocations;
    }

    // 候选查询复用内部绑定核心的人员及账号规则。
    public ValueTask<IReadOnlyList<PersonUserBindingCandidateDto>> SearchCandidatesAsync(
        string personEid,
        string? query,
        CancellationToken cancellationToken = default) =>
        _core.SearchCandidatesAsync(personEid, query, cancellationToken);

    /// <summary>在组织写协调和共享事务内永久绑定人员及账号，提交后统一发布完整人员结果。</summary>
    public async ValueTask<PersonUserBindingDto> BindAsync(
        string personEid,
        BindPersonUserRequest request,
        PersonUserBindingActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (actor is null)
        {
            throw new PersonUserBindingException(PersonUserBindingFailureCodes.InvalidActor);
        }
        using var invocation = _invocations.Begin(actor);
        try
        {
            var result = await _resolveObjectOperations().ExecuteAsync(
                PersonObjectBehavior.BehaviorDefinition.ObjectKey,
                PersonObjectBehavior.BindUserAction,
                personEid,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["AccountEid"] = request.AccountEid,
                    ["ExpectedRevision"] = request.ExpectedRevision,
                    ["Reason"] = request.Reason
                },
                cancellationToken);
            return ReadResult(result);
        }
        catch (PlatformCacheUnavailableException)
        {
            throw;
        }
        catch (PersonUserBindingException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    // 对象入口保留原 DTO；扩展拒绝和领域拒绝都按原 HTTP code/status 返回。
    private static PersonUserBindingDto ReadResult(StandardCrudMutationResult result)
    {
        if (result.Succeeded && result.Detail?.Fields.GetValueOrDefault("Result") is PersonUserBindingDto dto)
        {
            return dto;
        }
        var error = result.Error ?? throw new InvalidOperationException("Person binding behavior returned no result.");
        ThrowCacheFailure(error);
        error.Args.TryGetValue("current", out var current);
        throw new PersonUserBindingException(error.Code, current, statusCode: error.StatusCode);
    }

    // 对象执行器统一表达提交后发布失败；专用 HTTP 边界恢复原异常类型与稳定 code。
    private static void ThrowCacheFailure(StandardCrudError error)
    {
        if (error.Code == "Persistence.CommittedCacheUnavailable")
        {
            throw new PlatformCacheUnavailableException("Organization", databaseCommitted: true);
        }
        if (error.Code == "Persistence.CommitOutcomeUnknown")
        {
            throw new PlatformCacheUnavailableException("Organization", commitOutcomeUnknown: true);
        }
        if (error.Code == "Persistence.CacheUnavailable")
        {
            throw new PlatformCacheUnavailableException("Organization");
        }
    }
}
