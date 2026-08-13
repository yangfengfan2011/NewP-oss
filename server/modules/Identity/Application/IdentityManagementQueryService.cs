using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Repositories;

namespace ICP.Platform.Modules.Identity.Application;

public sealed record IdentityManagementAccountSearch(
    string? Query,
    IdentityAccountStatus? Status,
    bool? IsPersonnelBound,
    bool? IsLocalAdministrator,
    int Page,
    int PageSize);

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
    private readonly IIdentityRepository _repository;

    public IdentityManagementQueryService(IIdentityRepository repository)
    {
        _repository = repository;
    }

    public async ValueTask<IdentityManagementAccountPage> SearchAsync(
        IdentityManagementAccountSearch search,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var accounts = await _repository.SearchAccountsAsync(cancellationToken);
        var query = accounts
            .Where(account => search.Status is null
                ? account.Status != IdentityAccountStatus.Deleted
                : account.Status == search.Status)
            .Where(account => search.IsPersonnelBound is null || account.IsPersonnelBound == search.IsPersonnelBound)
            .Where(account => search.IsLocalAdministrator is null || account.IsLocalAdministrator == search.IsLocalAdministrator)
            .Where(account => string.IsNullOrWhiteSpace(search.Query)
                || account.ECODE.Contains(search.Query, StringComparison.OrdinalIgnoreCase)
                || account.ENAME.Contains(search.Query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(account => account.ECODE, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var skip = (long)(search.Page - 1) * search.PageSize;
        var items = skip >= query.Length
            ? []
            : query
                .Skip((int)skip)
                .Take(search.PageSize)
                .Select(ToView)
                .ToArray();
        return new IdentityManagementAccountPage(items, search.Page, search.PageSize, query.Length);
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
