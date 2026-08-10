using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Repositories;

namespace ICP.Platform.Modules.Identity.Application;

public sealed record IdentityManagementUserSearch(
    string? Query,
    IdentityAccountStatus? Status,
    bool? IsPersonnelBound,
    bool? IsLocalAdministrator,
    int Page,
    int PageSize);

public sealed record IdentityManagementUserView(
    string UserEid,
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
    string? ActorUserEid,
    string CreatedBy,
    DateTime CreatedAt,
    string UpdatedBy,
    DateTime UpdatedAt);

public sealed record IdentityManagementUserPage(
    IReadOnlyList<IdentityManagementUserView> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record IdentityManagementSecurityEventPage(
    IReadOnlyList<IdentityManagementSecurityEventView> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record IdentityManagementUserDetail(
    IdentityManagementUserView Account,
    IdentityManagementSecurityEventPage Events);

public interface IIdentityManagementQueryService
{
    ValueTask<IdentityManagementUserPage> SearchAsync(
        IdentityManagementUserSearch search,
        CancellationToken cancellationToken = default);

    ValueTask<IdentityManagementUserDetail?> FindAsync(
        string userEid,
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

    public async ValueTask<IdentityManagementUserPage> SearchAsync(
        IdentityManagementUserSearch search,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var users = await _repository.SearchUsersAsync(cancellationToken);
        var query = users
            .Where(user => search.Status is null
                ? user.Status != IdentityAccountStatus.Deleted
                : user.Status == search.Status)
            .Where(user => search.IsPersonnelBound is null || user.IsPersonnelBound == search.IsPersonnelBound)
            .Where(user => search.IsLocalAdministrator is null || user.IsLocalAdministrator == search.IsLocalAdministrator)
            .Where(user => string.IsNullOrWhiteSpace(search.Query)
                || user.ECODE.Contains(search.Query, StringComparison.OrdinalIgnoreCase)
                || user.ENAME.Contains(search.Query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(user => user.ECODE, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var skip = (long)(search.Page - 1) * search.PageSize;
        var items = skip >= query.Length
            ? []
            : query
                .Skip((int)skip)
                .Take(search.PageSize)
                .Select(ToView)
                .ToArray();
        return new IdentityManagementUserPage(items, search.Page, search.PageSize, query.Length);
    }

    public async ValueTask<IdentityManagementUserDetail?> FindAsync(
        string userEid,
        int eventPage,
        int eventPageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userEid))
        {
            return null;
        }

        var user = await _repository.FindUserAsync(userEid, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var events = (await _repository.SearchSecurityEventsAsync(userEid, cancellationToken))
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
        return new IdentityManagementUserDetail(
            ToView(user),
            new IdentityManagementSecurityEventPage(page, eventPage, eventPageSize, events.Length));
    }

    internal static IdentityManagementUserView ToView(User user) => new(
        user.EID,
        user.ECODE,
        user.ENAME,
        user.Status,
        user.Revision,
        user.IsLocalAdministrator,
        user.IsPersonnelBound,
        user.PersonnelDisableHold,
        user.CreatedBy,
        user.CreatedAt,
        user.UpdatedBy,
        user.UpdatedAt);

    private static IdentityManagementSecurityEventView ToView(IdentitySecurityEvent securityEvent) => new(
        securityEvent.EID,
        securityEvent.EventType,
        securityEvent.Reason,
        securityEvent.OccurredAt,
        securityEvent.TraceId,
        securityEvent.ActorKind,
        securityEvent.ActorUserEID,
        securityEvent.CreatedBy,
        securityEvent.CreatedAt,
        securityEvent.UpdatedBy,
        securityEvent.UpdatedAt);
}
