using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Repositories;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;

namespace ICP.Platform.Modules.Identity.Application;

public sealed record IdentityManagementAccountSearch(
    string? Query,
    IdentityAccountStatus? Status,
    bool? IsPersonnelBound,
    bool? IsLocalAdministrator,
    int Page,
    int PageSize,
    string? SortBy = null,
    string? SortDirection = null);

public sealed record IdentityManagementAccountView(
    string AccountEid,
    string ECODE,
    string ENAME,
    IdentityAccountStatus Status,
    long Revision,
    bool IsLocalAdministrator,
    bool IsPersonnelBound,
    bool PersonnelDisableHold,
    string CreatedBy,
    DateTime CreatedAt,
    string UpdatedBy,
    DateTime UpdatedAt);

public sealed record IdentityManagementSecurityEventView(
    string EventEid,
    string EventType,
    string Reason,
    DateTime OccurredAt,
    string TraceId,
    IdentitySecurityActorKind ActorKind,
    string? ActorAccountEid,
    string CreatedBy,
    DateTime CreatedAt,
    string UpdatedBy,
    DateTime UpdatedAt);

public sealed record IdentityManagementAccountPage(
    IReadOnlyList<IdentityManagementAccountView> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record IdentityManagementSecurityEventPage(
    IReadOnlyList<IdentityManagementSecurityEventView> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record IdentityManagementAccountDetail(
    IdentityManagementAccountView Account,
    IdentityManagementSecurityEventPage Events);

public interface IIdentityManagementQueryService
{
    ValueTask<IdentityManagementAccountPage> SearchAsync(
        IdentityManagementAccountSearch search,
        CancellationToken cancellationToken = default);

    ValueTask<IdentityManagementAccountDetail?> FindAsync(
        string accountEid,
        int eventPage,
        int eventPageSize,
        CancellationToken cancellationToken = default);
}

public sealed class IdentityManagementQueryService : IIdentityManagementQueryService
{
    private static readonly PlatformQueryDescriptor AccountListDescriptor = CreateAccountListDescriptor();
    private readonly IIdentityRepository _repository;
    private readonly IPlatformQueryExecutor _queryExecutor;

    /// <summary>
    /// 创建 Identity 管理查询服务；账号列表由平台查询内核执行，详情及安全事件仍由 Identity 仓储拥有。
    /// </summary>
    public IdentityManagementQueryService(
        IIdentityRepository repository,
        IPlatformQueryExecutor queryExecutor)
    {
        _repository = repository;
        _queryExecutor = queryExecutor;
    }

    /// <summary>
    /// 在数据库查询层完成账号筛选、稳定排序、计数和分页，再映射为 Identity API 视图。
    /// </summary>
    public async ValueTask<IdentityManagementAccountPage> SearchAsync(
        IdentityManagementAccountSearch search,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var request = new PlatformQueryRequest(
            Fields: null,
            Page: search.Page,
            PageSize: search.PageSize,
            SortBy: search.SortBy,
            SortDirection: search.SortDirection,
            Conditions: BuildAccountListConditions(search));
        var result = await _queryExecutor.QueryAsync(AccountListDescriptor, request, cancellationToken);
        var items = result.Items.Select(ToAccountView).ToArray();
        return new IdentityManagementAccountPage(items, result.Page, result.PageSize, result.Total);
    }

    public async ValueTask<IdentityManagementAccountDetail?> FindAsync(
        string accountEid,
        int eventPage,
        int eventPageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountEid))
        {
            return null;
        }

        var account = await _repository.FindAccountAsync(accountEid, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var events = (await _repository.SearchSecurityEventsAsync(accountEid, cancellationToken))
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.EID, StringComparer.Ordinal)
            .ToArray();
        var skip = (long)(eventPage - 1) * eventPageSize;
        var page = skip >= events.Length
            ? []
            : events
                .Skip((int)skip)
                .Take(eventPageSize)
                .Select(ToView)
                .ToArray();
        return new IdentityManagementAccountDetail(
            ToView(account),
            new IdentityManagementSecurityEventPage(page, eventPage, eventPageSize, events.Length));
    }

    internal static IdentityManagementAccountView ToView(IdentityAccount account) => new(
        account.EID,
        account.ECODE,
        account.ENAME,
        account.Status,
        account.Revision,
        account.IsLocalAdministrator,
        account.IsPersonnelBound,
        account.PersonnelDisableHold,
        account.CreatedBy,
        account.CreatedAt,
        account.UpdatedBy,
        account.UpdatedAt);

    // 默认状态条件以 OR 组表达 Enabled 与 Disabled；Deleted 只能通过显式状态筛选进入结果。
    private static IReadOnlyList<PlatformQueryCondition> BuildAccountListConditions(
        IdentityManagementAccountSearch search)
    {
        var conditions = new List<PlatformQueryCondition>();
        if (search.Status is null)
        {
            conditions.Add(new PlatformQueryCondition("Status", "=", "Enabled", "status", "or"));
            conditions.Add(new PlatformQueryCondition("Status", "=", "Disabled", "status", "or"));
        }
        else
        {
            conditions.Add(new PlatformQueryCondition("Status", "=", search.Status.Value.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(search.Query))
        {
            conditions.Add(new PlatformQueryCondition("ECODE", "contains", search.Query, "keyword", "or"));
            conditions.Add(new PlatformQueryCondition("ENAME", "contains", search.Query, "keyword", "or"));
        }

        if (search.IsPersonnelBound is not null)
        {
            conditions.Add(new PlatformQueryCondition("IsPersonnelBound", "=", search.IsPersonnelBound.Value));
        }

        if (search.IsLocalAdministrator is not null)
        {
            conditions.Add(new PlatformQueryCondition("IsLocalAdministrator", "=", search.IsLocalAdministrator.Value));
        }

        return conditions;
    }

    // 平台查询结果只包含账号列表稳定 DTO 所需字段，避免把 NormalizedECODE 等内部字段暴露给 API。
    private static IdentityManagementAccountView ToAccountView(IReadOnlyDictionary<string, object?> row) => new(
        ReadRequired<string>(row, "EID"),
        ReadRequired<string>(row, "ECODE"),
        ReadRequired<string>(row, "ENAME"),
        Enum.Parse<IdentityAccountStatus>(ReadRequired<string>(row, "Status"), ignoreCase: false),
        ReadRequired<long>(row, "Revision"),
        ReadRequired<bool>(row, "IsLocalAdministrator"),
        ReadRequired<bool>(row, "IsPersonnelBound"),
        ReadRequired<bool>(row, "PersonnelDisableHold"),
        ReadRequired<string>(row, "CreatedBy"),
        ReadRequired<DateTime>(row, "CreatedAt"),
        ReadRequired<string>(row, "UpdatedBy"),
        ReadRequired<DateTime>(row, "UpdatedAt"));

    // 缺少查询投影字段表示模块 descriptor 与稳定 API 已漂移，应显式失败而不是返回静默默认值。
    private static T ReadRequired<T>(IReadOnlyDictionary<string, object?> row, string field)
    {
        if (!row.TryGetValue(field, out var value) || value is not T typed)
        {
            throw new InvalidOperationException($"Identity account list query did not return required field '{field}'.");
        }

        return typed;
    }

    // Identity 只声明账号查询白名单和稳定 API 投影；执行机制及稳定 EID 兜底排序由平台查询内核负责。
    private static PlatformQueryDescriptor CreateAccountListDescriptor() => new()
    {
        ResourceKey = "identity.account",
        ModelName = "Identity/IdentityAccount",
        IdField = "EID",
        AllowedModelFields =
        [
            "EID",
            "ECODE",
            "ENAME",
            "Status",
            "Revision",
            "IsLocalAdministrator",
            "IsPersonnelBound",
            "PersonnelDisableHold",
            "CreatedBy",
            "CreatedAt",
            "UpdatedBy",
            "UpdatedAt"
        ],
        DefaultFields =
        [
            "EID",
            "ECODE",
            "ENAME",
            "Status",
            "Revision",
            "IsLocalAdministrator",
            "IsPersonnelBound",
            "PersonnelDisableHold",
            "CreatedBy",
            "CreatedAt",
            "UpdatedBy",
            "UpdatedAt"
        ],
        SortableFields = ["ECODE", "ENAME", "Status"],
        DefaultSortField = "ECODE",
        DefaultSortDirection = "asc"
    };

    private static IdentityManagementSecurityEventView ToView(IdentitySecurityEvent securityEvent) => new(
        securityEvent.EID,
        securityEvent.EventType,
        securityEvent.Reason,
        securityEvent.OccurredAt,
        securityEvent.TraceId,
        securityEvent.ActorKind,
        securityEvent.ActorAccountEID,
        securityEvent.CreatedBy,
        securityEvent.CreatedAt,
        securityEvent.UpdatedBy,
        securityEvent.UpdatedAt);
}
