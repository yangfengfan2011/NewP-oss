using System.Text;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Persistence.Auditing;

namespace ICP.Platform.Modules.Organization.Services;

// 人员生命周期核心组合业务与持久化依赖，由专用业务服务调用。
internal sealed class PersonLifecycleActionCore(
    IPersonUserBindingRepository repository,
    IIdentityAccountDirectory accountDirectory,
    IIdentityLifecycleParticipant identityParticipant,
    IAuthenticationSessionRegistry sessionRegistry,
    IEntityAuditValueProvider auditValues)
{
    /// <summary>在服务已取得协调权的事务内校验输入并同步人员、账号与会话事实；不自行提交。</summary>
    internal async ValueTask<PersonLifecycleDto> ExecuteAsync(
        string personEid,
        SetPersonLifecycleStatusRequest request,
        PersonLifecycleActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(actor);
        ValidateTrustedActor(actor.ActorId);
        if (string.IsNullOrWhiteSpace(personEid))
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.InvalidRequest);
        }

        var reason = NormalizeReason(request.Reason);
        var person = await GetRequiredPersonAsync(personEid, cancellationToken);
        var account = await FindBoundAccountAsync(person, cancellationToken);
        if (IsConverged(person, account, request.IsEnabled))
        {
            return ToDto(person, account);
        }

        var audit = ResolveAuditValues(person, actor.ActorId);
        await repository.SetEnablementAsync(
            personEid,
            request.IsEnabled,
            audit,
            cancellationToken);

        IdentityAccountSnapshot? changedAccount = account;
        if (person.AccountEid is not null)
        {
            if (request.IsEnabled)
            {
                changedAccount = (await identityParticipant.RegisterPersonnelDisableHoldReleaseAsync(
                    new PersonnelDisableHoldReleaseRegistration
                    {
                        AccountEid = person.AccountEid,
                        ExpectedRevision = account!.Revision,
                        ActorId = actor.ActorId,
                        TraceId = actor.TraceId
                    },
                    cancellationToken)).Account;
            }
            else
            {
                var identityResult = await identityParticipant.RegisterPersonnelDisableAsync(
                    new PersonnelAccountDisableRegistration
                    {
                        AccountEid = person.AccountEid,
                        ExpectedRevision = account!.Revision,
                        Reason = reason,
                        ActorId = actor.ActorId,
                        TraceId = actor.TraceId
                    },
                    cancellationToken);
                changedAccount = identityResult.Account;
                if (identityResult.Changed)
                {
                    _ = await sessionRegistry.RevokeAllAsync(
                        new AccountAuthenticationSessionsRevocation
                        {
                            AccountEid = person.AccountEid,
                            Reason = reason,
                            ActorId = actor.ActorId
                        },
                        cancellationToken);
                }
            }
        }

        var changedPerson = await GetRequiredPersonAsync(personEid, cancellationToken);
        return new PersonLifecycleDto(
            changedPerson.PersonEid,
            request.IsEnabled,
            changedPerson.AccountEid,
            ToAccount(changedAccount));
    }

    // 读取当前人员事实，不存在时返回稳定领域失败。
    private async ValueTask<PersonUserBindingState> GetRequiredPersonAsync(string personEid, CancellationToken cancellationToken) =>
        await repository.FindAsync(personEid, cancellationToken)
        ?? throw new PersonLifecycleException(PersonLifecycleFailureCodes.PersonNotFound);

    // 读取既有绑定账号，依赖故障保持生命周期失败语义。
    private async ValueTask<IdentityAccountSnapshot?> FindBoundAccountAsync(
        PersonUserBindingState person,
        CancellationToken cancellationToken)
    {
        if (person.AccountEid is null)
        {
            return null;
        }

        try
        {
            return await accountDirectory.FindAsync(person.AccountEid, cancellationToken)
                ?? throw new PersonLifecycleException(PersonLifecycleFailureCodes.DependencyUnavailable);
        }
        catch (PersonLifecycleException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.DependencyUnavailable, innerException: exception);
        }
    }

    // 同时核对人员状态与账号阻断，已收敛重试不重复登记。
    private static bool IsConverged(
        PersonUserBindingState person,
        IdentityAccountSnapshot? account,
        bool targetEnabled)
    {
        if (person.IsEnabled != targetEnabled)
        {
            return false;
        }

        return person.AccountEid is null || (targetEnabled
            ? account is { PersonnelDisableHold: false }
            : account is { Status: IdentityAccountStatus.Disabled, PersonnelDisableHold: true });
    }

    // 拒绝缺少服务端主体或追踪标识的调用。
    private static void ValidateActor(PersonLifecycleActor actor)
    {
        if (actor is null || string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.TraceId))
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.InvalidActor);
        }
    }

    // 服务端 actor 必须与当前可信审计主体一致，包括幂等与无变化路径。
    private void ValidateTrustedActor(string actorId)
    {
        if (!string.Equals(auditValues.Current().ActorId, actorId, StringComparison.Ordinal))
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.InvalidActor);
        }
    }

    // 按 Unicode 标量规范化原因并保持现有长度约束。
    private static string NormalizeReason(string reason)
    {
        var normalized = (reason ?? string.Empty).Trim();
        var length = normalized.EnumerateRunes().Count();
        if (length is < 1 or > 500)
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.InvalidReason);
        }

        return normalized;
    }

    // 组合人员和账号当前生命周期视图。
    private static PersonLifecycleDto ToDto(PersonUserBindingState person, IdentityAccountSnapshot? account) =>
        new(person.PersonEid, person.IsEnabled, person.AccountEid, ToAccount(account));

    // 投影账号公开状态而不暴露内部持久化形状。
    private static PersonLifecycleAccountDto? ToAccount(IdentityAccountSnapshot? account) => account is null
        ? null
        : new(account.AccountEid, account.Status.ToString(), account.Revision, account.PersonnelDisableHold);

    // 直接 DML 与普通 EF 保存共用可信 actor、UTC 和更新时间下界；调用方 actor 只作为协议一致性校验。
    private EntityAuditValues ResolveAuditValues(PersonUserBindingState person, string actorId)
    {
        if (string.IsNullOrWhiteSpace(person.CreatedBy) || person.CreatedAt is null)
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.DependencyUnavailable);
        }
        var values = auditValues.ForUpdate(person.CreatedBy, person.CreatedAt.Value);
        if (!string.Equals(values.UpdatedBy, actorId, StringComparison.Ordinal))
        {
            throw new PersonLifecycleException(PersonLifecycleFailureCodes.InvalidActor);
        }
        return values;
    }

}
