using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Controllers;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Identity.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.Identity.Tests;

public sealed class IdentityManagementControllerTests
{
    [Fact]
    public async Task List_DefaultsToFirstTwentyAndForwardsConfirmedFilters()
    {
        var queries = new RecordingManagementQueryService();
        var controller = new IdentityAccountsController(queries, new UnusedAccountApplicationService());

        var result = await controller.List(
            query: "  ali  ",
            status: IdentityAccountStatus.Disabled,
            isPersonnelBound: true,
            isLocalAdministrator: false,
            page: null,
            pageIndex: null,
            pageSize: null,
            sortBy: null,
            sortDirection: null,
            cancellationToken: CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<IdentityAccountPageResponse>(response.Value);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(new IdentityManagementAccountSearch(
            "ali",
            IdentityAccountStatus.Disabled,
            true,
            false,
            1,
            20), queries.LastSearch);
    }

    [Fact]
    public async Task List_MapsStandardHostPagingAndSortWithoutChangingIdentityRoute()
    {
        var queries = new RecordingManagementQueryService
        {
            SearchResult = new IdentityManagementAccountPage([], 3, 50, 121)
        };
        var controller = new IdentityAccountsController(queries, new UnusedAccountApplicationService());

        var result = await controller.List(
            query: null,
            status: null,
            isPersonnelBound: null,
            isLocalAdministrator: null,
            page: null,
            pageIndex: 3,
            pageSize: 50,
            sortBy: "ENAME",
            sortDirection: "desc",
            cancellationToken: CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<IdentityAccountPageResponse>(response.Value);
        Assert.Equal("api/identity/accounts", typeof(IdentityAccountsController)
            .GetCustomAttributes(typeof(RouteAttribute), true)
            .Cast<RouteAttribute>()
            .Single()
            .Template);
        Assert.Equal(121, page.TotalCount);
        Assert.Equal(121, page.Total);
        Assert.Equal(new IdentityManagementAccountSearch(
            null,
            null,
            null,
            null,
            3,
            50,
            "ENAME",
            "desc"), queries.LastSearch);
    }

    [Fact]
    public async Task Snapshot_ReturnsAccountWithoutLoadingSecurityEvents()
    {
        var queries = new RecordingManagementQueryService
        {
            AccountResult = AccountView()
        };
        var controller = new IdentityAccountsController(queries, new UnusedAccountApplicationService());

        var result = await controller.GetSnapshot("account-1", CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var account = Assert.IsType<IdentityAccountViewResponse>(response.Value);
        Assert.Equal("account-1", account.EID);
        Assert.Equal("account-1", queries.LastAccountEid);
        Assert.Null(queries.LastEventAccountEid);
    }

    [Fact]
    public async Task SecurityEvents_ValidatesAndForwardsIndependentPagination()
    {
        var queries = new RecordingManagementQueryService
        {
            SecurityEventResult = new IdentityManagementSecurityEventPage(
            [
                new IdentityManagementSecurityEventView(
                    "event-1",
                    "AccountCreated",
                    "created",
                    new DateTime(2030, 1, 2, 0, 4, 5, DateTimeKind.Utc),
                    "2030/1/2 08:04:05",
                    "trace-1",
                    IdentitySecurityActorKind.System,
                    null,
                    "system",
                    new DateTime(2030, 1, 2, 0, 4, 5, DateTimeKind.Utc),
                    "system",
                    new DateTime(2030, 1, 2, 0, 4, 5, DateTimeKind.Utc))
            ], 2, 10, 23)
        };
        var controller = new IdentityAccountsController(queries, new UnusedAccountApplicationService());

        var result = await controller.GetSecurityEvents("account-1", page: 2, pageSize: 10, CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var events = Assert.IsType<IdentitySecurityEventPageResponse>(response.Value);
        Assert.Equal(23, events.TotalCount);
        var item = Assert.Single(events.Items);
        Assert.Equal(new DateTime(2030, 1, 2, 0, 4, 5, DateTimeKind.Utc), item.OccurredAt);
        Assert.Equal("2030/1/2 08:04:05", item.OccurredAt_DisplayValue);
        Assert.Equal("account-1", queries.LastEventAccountEid);
        Assert.Equal(2, queries.LastEventPage);
        Assert.Equal(10, queries.LastEventPageSize);
    }

    [Fact]
    public async Task Create_ReturnsOneTimeCredentialOnlyInNoStoreSuccessResponse()
    {
        var accounts = new RecordingAccountApplicationService
        {
            CreateResult = new IdentityAccountCreationResult(Account(), "temporary-once")
        };
        var controller = Controller(accounts);

        var result = await controller.Create(
            new CreateIdentityAccountRequest("alice", "Alice"),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<IdentityCredentialIssuanceResponse>(created.Value);
        Assert.Equal("temporary-once", response.TemporaryCredential);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
        Assert.DoesNotContain("temporary-once", JsonSerializer.Serialize(response.Account), StringComparison.Ordinal);
        Assert.Equal("account-1", accounts.LastCreate?.Actor.ActorId);
    }

    [Fact]
    public async Task UpdateName_StaleRevisionReturnsStableConflictWithCurrentSafeSnapshot()
    {
        var accounts = new RecordingAccountApplicationService
        {
            Failure = new IdentityApplicationException(IdentityFailureCodes.RevisionConflict, Account())
        };
        var controller = Controller(accounts);

        var result = await controller.UpdateName(
            "account-1",
            new UpdateIdentityAccountRequest("New Name", 1),
            CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal("application/problem+json", Assert.Single(conflict.ContentTypes));
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(IdentityFailureCodes.RevisionConflict, problem.Extensions["code"]);
        Assert.Equal("account-1", Assert.IsType<IdentityAccountResponse>(problem.Extensions["current"]).EID);
    }

    [Fact]
    public async Task PasswordChange_UsesAuthenticatedSubjectAndKeepsNewSessionCredentialOutOfJson()
    {
        var accounts = new RecordingAccountApplicationService
        {
            ChangePasswordResult = new IdentityPasswordChangeResult(
                Account(),
                new AuthenticationSessionIssuance
                {
                    Credential = "new-session-secret",
                    Session = new AuthenticationSessionSnapshot
                    {
                        SessionEid = "session-2",
                        AccountEid = "account-1",
                        Type = AuthenticationSessionType.Regular,
                        TimeZoneId = "Asia/Shanghai",
                        ExpiresAtUtc = new DateTime(2030, 1, 1, 8, 0, 0, DateTimeKind.Utc)
                    }
                })
        };
        var controller = new IdentitySelfServiceController(accounts)
        {
            ControllerContext = Context()
        };

        var result = await controller.ChangePassword(
            new ChangeIdentityPasswordRequest("old-password", "new-password", 1),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsAssignableFrom<IAuthenticationSessionCookieResponse>(ok.Value);
        Assert.Equal("new-session-secret", response.GetSessionCredential());
        Assert.DoesNotContain("new-session-secret", JsonSerializer.Serialize(ok.Value), StringComparison.Ordinal);
        Assert.Equal("account-1", accounts.LastPasswordChange?.AccountEid);
        Assert.Equal("Asia/Taipei", accounts.LastPasswordChange?.TimeZoneId);
        Assert.Equal("trusted-session", accounts.LastPasswordChange?.SessionEid);
    }

    [Fact]
    public async Task PasswordChange_RejectsPrincipalWithoutTrustedSessionTimeZone()
    {
        var accounts = new RecordingAccountApplicationService();
        var controller = new IdentitySelfServiceController(accounts)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "account-1")
                    ], "test"))
                }
            }
        };

        var result = await controller.ChangePassword(
            new ChangeIdentityPasswordRequest("old-password", "new-password", 1),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Assert.Null(accounts.LastPasswordChange);
    }

    [Fact]
    public async Task PasswordChange_InvalidCurrentCredentialKeepsStableFieldProblemContract()
    {
        var accounts = new RecordingAccountApplicationService
        {
            Failure = new IdentityApplicationException(IdentityFailureCodes.InvalidCurrentCredential)
        };
        var controller = new IdentitySelfServiceController(accounts)
        {
            ControllerContext = Context()
        };

        var result = await controller.ChangePassword(
            new ChangeIdentityPasswordRequest("wrong-password", "new-password", 1),
            CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", Assert.Single(response.ContentTypes));
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(IdentityFailureCodes.InvalidCurrentCredential, problem.Extensions["code"]);
        Assert.Equal("trace-1", problem.Extensions["traceId"]);
        var errors = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string[]>>(problem.Extensions["errors"]);
        Assert.Equal([IdentityFailureCodes.InvalidCurrentCredential], errors["CurrentPassword"]);
    }

    [Fact]
    public void Routes_SeparateLocalAdministratorManagementFromAuthenticatedSelfService()
    {
        var actions = typeof(IdentityAccountsController).GetMethods().Where(method => method.GetCustomAttributes(typeof(RequireFeatureAttribute), true).Length > 0).ToArray();
        Assert.Equal(10, actions.Length);
        foreach (var action in actions)
            Assert.Contains(action.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(), policy => policy.Policy == "AdminOnly");
        Assert.Single(typeof(IdentityAccountsController).GetMethod(nameof(IdentityAccountsController.SetStatus))!.GetCustomAttributes(typeof(DenyAllAttribute), true));
        Assert.Single(typeof(IdentitySelfServiceController).GetCustomAttributes(typeof(AuthenticatedOnlyAttribute), true));
    }

    [Fact]
    public async Task Detail_IncludesDeletedTombstoneAndReversePagedReadOnlyEvents()
    {
        var repository = new InMemoryIdentityRepository();
        repository.Accounts.Add(new IdentityAccount
        {
            EID = "deleted-1",
            ECODE = "retired",
            NormalizedECODE = "RETIRED",
            ENAME = "Retired",
            Status = IdentityAccountStatus.Deleted,
            Revision = 3,
            CreatedBy = "actor-1",
            CreatedAt = new DateTime(2029, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedBy = "actor-2",
            UpdatedAt = new DateTime(2029, 1, 3, 0, 0, 0, DateTimeKind.Utc)
        });
        for (var index = 1; index <= 3; index++)
        {
            repository.SecurityEvents.Add(new IdentitySecurityEvent
            {
                EID = $"event-{index}",
                TargetAccountEID = "deleted-1",
                EventType = $"Event{index}",
                Reason = "safe reason",
                OccurredAt = new DateTime(2029, 1, index, 0, 0, 0, DateTimeKind.Utc),
                TraceId = $"trace-{index}",
                ActorKind = IdentitySecurityActorKind.IdentityAccount,
                ActorAccountEID = "actor-1",
                CreatedBy = "actor-1",
                CreatedAt = new DateTime(2029, 1, index, 0, 0, 0, DateTimeKind.Utc),
                UpdatedBy = "actor-1",
                UpdatedAt = new DateTime(2029, 1, index, 0, 0, 0, DateTimeKind.Utc)
            });
        }

        var detail = await IdentityCrudQueryTestFactory.Create(
                repository,
                new UnusedPlatformQueryExecutor(),
                new TestTemporalDisplayProjectionExecutor())
            .FindAsync("deleted-1", eventPage: 2, eventPageSize: 2);

        Assert.Equal(IdentityAccountStatus.Deleted, detail?.Account.Status);
        Assert.Equal(3, detail?.Events.TotalCount);
        Assert.Equal("event-1", Assert.Single(detail!.Events.Items).EventEid);
    }

    private static IdentityAccountsController Controller(RecordingAccountApplicationService accounts) => new(
        new RecordingManagementQueryService(),
        accounts)
    {
        ControllerContext = Context()
    };

    private static ControllerContext Context()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "account-1"),
                new Claim(ClaimTypes.Name, "alice"),
                new Claim(AuthenticationSessionClaimTypes.TimeZoneId, "Asia/Taipei"),
                new Claim(AuthenticationSessionClaimTypes.SessionEid, "trusted-session"),
                new Claim(ClaimTypes.Role, "LocalAdmin")
            ], "test"))
        };
        context.TraceIdentifier = "trace-1";
        return new ControllerContext { HttpContext = context };
    }

    private static IdentityAccountSnapshot Account() => new()
    {
        AccountEid = "account-1",
        ECODE = "alice",
        ENAME = "Alice",
        Status = IdentityAccountStatus.Enabled,
        Revision = 1
    };

    private static IdentityManagementAccountView AccountView() => new(
        "account-1",
        "alice",
        "Alice",
        IdentityAccountStatus.Enabled,
        1,
        false,
        false,
        false,
        "account-1",
        new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        "account-1",
        new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    private sealed class RecordingManagementQueryService : IIdentityManagementQueryService
    {
        public IdentityManagementAccountPage? SearchResult { get; init; }

        public IdentityManagementAccountSearch? LastSearch { get; private set; }

        public IdentityManagementAccountView? AccountResult { get; init; }

        public IdentityManagementSecurityEventPage? SecurityEventResult { get; init; }

        public string? LastAccountEid { get; private set; }

        public string? LastEventAccountEid { get; private set; }

        public int? LastEventPage { get; private set; }

        public int? LastEventPageSize { get; private set; }

        public ValueTask<IdentityManagementAccountPage> SearchAsync(
            IdentityManagementAccountSearch search,
            CancellationToken cancellationToken = default)
        {
            LastSearch = search;
            return ValueTask.FromResult(SearchResult ?? new IdentityManagementAccountPage([], search.Page, search.PageSize, 0));
        }

        public ValueTask<IdentityManagementAccountDetail?> FindAsync(
            string accountEid,
            int eventPage,
            int eventPageSize,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IdentityManagementAccountView?> FindAccountAsync(
            string accountEid,
            CancellationToken cancellationToken = default)
        {
            LastAccountEid = accountEid;
            return ValueTask.FromResult(AccountResult);
        }

        public ValueTask<IdentityManagementSecurityEventPage?> SearchSecurityEventsAsync(
            string accountEid,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            LastEventAccountEid = accountEid;
            LastEventPage = page;
            LastEventPageSize = pageSize;
            return ValueTask.FromResult(SecurityEventResult);
        }
    }

    private sealed class UnusedAccountApplicationService : IIdentityAccountApplicationService
    {
        public ValueTask<IdentityAccountCreationResult> CreateAsync(CreateIdentityAccountCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IdentityAccountSnapshot> UpdateNameAsync(UpdateIdentityAccountNameCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IdentityAccountSnapshot> SetStatusAsync(SetIdentityAccountStatusCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IdentityCredentialResetResult> ResetCredentialAsync(ResetIdentityCredentialCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IdentityPasswordChangeResult> ChangePasswordAsync(ChangeIdentityPasswordCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IdentityAccountSnapshot> DeleteAsync(DeleteIdentityAccountCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingAccountApplicationService : IIdentityAccountApplicationService
    {
        public IdentityAccountCreationResult? CreateResult { get; init; }
        public IdentityPasswordChangeResult? ChangePasswordResult { get; init; }
        public IdentityApplicationException? Failure { get; init; }
        public CreateIdentityAccountCommand? LastCreate { get; private set; }
        public ChangeIdentityPasswordCommand? LastPasswordChange { get; private set; }

        public ValueTask<IdentityAccountCreationResult> CreateAsync(CreateIdentityAccountCommand command, CancellationToken cancellationToken = default)
        {
            LastCreate = command;
            return ValueTask.FromResult(CreateResult ?? throw new NotSupportedException());
        }

        public ValueTask<IdentityAccountSnapshot> UpdateNameAsync(UpdateIdentityAccountNameCommand command, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<IdentityAccountSnapshot>(Failure is null ? new NotSupportedException() : Failure);

        public ValueTask<IdentityAccountSnapshot> SetStatusAsync(SetIdentityAccountStatusCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IdentityCredentialResetResult> ResetCredentialAsync(ResetIdentityCredentialCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IdentityPasswordChangeResult> ChangePasswordAsync(ChangeIdentityPasswordCommand command, CancellationToken cancellationToken = default)
        {
            LastPasswordChange = command;
            return Failure is not null
                ? ValueTask.FromException<IdentityPasswordChangeResult>(Failure)
                : ValueTask.FromResult(ChangePasswordResult ?? throw new NotSupportedException());
        }

        public ValueTask<IdentityAccountSnapshot> DeleteAsync(DeleteIdentityAccountCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
