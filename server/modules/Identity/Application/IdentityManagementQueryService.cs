using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Modules.Identity.Repositories;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using ICP.Platform.Presentation;

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
    DateTime UpdatedAt)
{
    public string? CreatedBy_DisplayValue { get; init; }
    public string? CreatedAt_DisplayValue { get; init; }
    public string? UpdatedBy_DisplayValue { get; init; }
    public string? UpdatedAt_DisplayValue { get; init; }
}

public sealed record IdentityManagementSecurityEventView(
    string EventEid,
    string EventType,
    string Reason,
    DateTime OccurredAt,
    string? OccurredAtDisplayValue,
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
    /// <summary>
    /// 查询满足筛选条件的账号分页。
    /// </summary>
    ValueTask<IdentityManagementAccountPage> SearchAsync(
        IdentityManagementAccountSearch search,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询账号及其一页安全事件，供既有管理页面兼容消费。
    /// </summary>
    ValueTask<IdentityManagementAccountDetail?> FindAsync(
        string accountEid,
        int eventPage,
        int eventPageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 只查询账号快照，不读取安全事件集合。
    /// </summary>
    ValueTask<IdentityManagementAccountView?> FindAccountAsync(
        string accountEid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 独立查询目标账号的一页安全事件；账号不存在时返回 null。
    /// </summary>
    ValueTask<IdentityManagementSecurityEventPage?> SearchSecurityEventsAsync(
        string accountEid,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

public sealed class IdentityManagementQueryService : IIdentityManagementQueryService
{
    private static readonly TemporalFieldDisplayProjectionPlan OccurredAtDisplayPlan =
        CompileOccurredAtDisplayPlan();
    private readonly IIdentityRepository _repository;
    private readonly IStandardCrudService _crud;
    private readonly IStandardCrudResourceRegistry _resources;
    private readonly ITemporalDisplayProjectionExecutor _temporalDisplayProjectionExecutor;

    /// <summary>
    /// 创建 Identity 管理查询服务；账号列表与详情采用通用 CRUD，安全事件仍由 Identity 仓储拥有。
    /// </summary>
    public IdentityManagementQueryService(
        IIdentityRepository repository,
        IStandardCrudService crud,
        ITemporalDisplayProjectionExecutor temporalDisplayProjectionExecutor,
        IStandardCrudResourceRegistry resources)
    {
        _repository = repository;
        _crud = crud;
        _resources = resources;
        _temporalDisplayProjectionExecutor = temporalDisplayProjectionExecutor;
    }

    /// <summary>
    /// 在数据库查询层完成账号筛选、稳定排序、计数和分页，再映射为 Identity API 视图。
    /// </summary>
    public async ValueTask<IdentityManagementAccountPage> SearchAsync(
        IdentityManagementAccountSearch search,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var result = await _crud.ListAsync(AccountDescriptor, new StandardCrudListQuery(
            search.Page, search.PageSize, search.SortBy, search.SortDirection,
            new Dictionary<string, string?>
            {
                ["query"] = search.Query, ["status"] = search.Status?.ToString(),
                ["IsPersonnelBound"] = search.IsPersonnelBound?.ToString(),
                ["IsLocalAdministrator"] = search.IsLocalAdministrator?.ToString()
            }), cancellationToken);
        var items = result.Items.Select(ToAccountView).ToArray();
        return new IdentityManagementAccountPage(items, result.Page, result.PageSize, result.Total);
    }

    /// <summary>
    /// 为既有管理页面组合账号快照和安全事件分页。
    /// </summary>
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

        var account = await FindAccountAsync(accountEid, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var events = await LoadSecurityEventPageAsync(accountEid, eventPage, eventPageSize, cancellationToken);
        return new IdentityManagementAccountDetail(account, events);
    }

    /// <summary>
    /// 读取账号快照而不触发安全事件查询，供共享详情 Drawer 与普通字段页使用。
    /// </summary>
    public async ValueTask<IdentityManagementAccountView?> FindAccountAsync(
        string accountEid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountEid))
        {
            return null;
        }

        var detail = await _crud.GetDetailAsync(AccountDescriptor, accountEid, cancellationToken);
        return detail is null ? null : ToAccountView(detail.Fields);
    }

    /// <summary>
    /// 在确认账号存在后独立读取安全事件分页，使重型摘要不进入普通详情装载路径。
    /// </summary>
    public async ValueTask<IdentityManagementSecurityEventPage?> SearchSecurityEventsAsync(
        string accountEid,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountEid)
            || await _repository.FindAccountAsync(accountEid, cancellationToken) is null)
        {
            return null;
        }

        return await LoadSecurityEventPageAsync(accountEid, page, pageSize, cancellationToken);
    }

    // 安全事件仍由 Identity 仓储拥有；这里统一稳定倒序和分页响应，供新旧两个只读入口复用。
    private async ValueTask<IdentityManagementSecurityEventPage> LoadSecurityEventPageAsync(
        string accountEid,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var events = (await _repository.SearchSecurityEventsAsync(accountEid, cancellationToken))
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.EID, StringComparer.Ordinal)
            .ToArray();
        var skip = (long)(page - 1) * pageSize;
        var items = skip >= events.Length
            ? []
            : events
                .Skip((int)skip)
                .Take(pageSize)
                .Select(ToView)
                .ToArray();
        return new IdentityManagementSecurityEventPage(items, page, pageSize, events.Length);
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
        ReadRequired<DateTime>(row, "UpdatedAt"))
    {
        CreatedBy_DisplayValue = row.GetValueOrDefault("CreatedBy_DisplayValue") as string,
        CreatedAt_DisplayValue = row.GetValueOrDefault("CreatedAt_DisplayValue") as string,
        UpdatedBy_DisplayValue = row.GetValueOrDefault("UpdatedBy_DisplayValue") as string,
        UpdatedAt_DisplayValue = row.GetValueOrDefault("UpdatedAt_DisplayValue") as string,
    };

    // 缺少查询投影字段表示模块 descriptor 与稳定 API 已漂移，应显式失败而不是返回静默默认值。
    private static T ReadRequired<T>(IReadOnlyDictionary<string, object?> row, string field)
    {
        if (!row.TryGetValue(field, out var value) || value is not T typed)
        {
            throw new InvalidOperationException($"Identity account list query did not return required field '{field}'.");
        }

        return typed;
    }

    private ICP.Platform.Crud.Descriptors.StandardCrudDescriptor AccountDescriptor =>
        _resources.FindByResourceKey("identity.account") ?? throw new InvalidOperationException("IDENTITY_ACCOUNT_CRUD_REQUIRED");

    // Identity 只提交业务 raw 并消费公共 projection 结果，不自行解释 display.type 或 format。
    private IdentityManagementSecurityEventView ToView(IdentitySecurityEvent securityEvent)
    {
        var occurredAt = _temporalDisplayProjectionExecutor.Project(
            OccurredAtDisplayPlan,
            securityEvent.OccurredAt);
        return new IdentityManagementSecurityEventView(
            securityEvent.EID,
            securityEvent.EventType,
            securityEvent.Reason,
            occurredAt.RawValue,
            occurredAt.DisplayValue,
            securityEvent.TraceId,
            securityEvent.ActorKind,
            securityEvent.ActorAccountEID,
            securityEvent.CreatedBy,
            securityEvent.CreatedAt,
            securityEvent.UpdatedBy,
            securityEvent.UpdatedAt);
    }

    // Identity 只按字段名选择统一 planner 的 typed 结果；策略与格式均由 Model authoring 编译决定。
    private static TemporalFieldDisplayProjectionPlan CompileOccurredAtDisplayPlan()
    {
        var securityEventModel = IdentityModelDefinitions.All.Single(model =>
            string.Equals(model.Name, nameof(IdentitySecurityEvent), StringComparison.Ordinal));
        var compilation = new ModelDisplayProjectionPlanner().Compile(
            securityEventModel,
            IdentityModelDefinitions.All);
        var plan = compilation.Plan?.Fields.SingleOrDefault(field =>
            string.Equals(
                field.SourceField,
                nameof(IdentitySecurityEvent.OccurredAt),
                StringComparison.Ordinal));
        return plan as TemporalFieldDisplayProjectionPlan
            ?? throw new InvalidOperationException("IDENTITY_SECURITY_EVENT_TEMPORAL_DISPLAY_PLAN_REQUIRED");
    }
}
