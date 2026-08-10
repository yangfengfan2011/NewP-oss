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
        var controller = new IdentityUsersController(queries, new UnusedAccountApplicationService());

        var result = await controller.List(
            query: "  ali  ",
            status: IdentityAccountStatus.Disabled,
            isPersonnelBound: true,
            isLocalAdministrator: false,
            page: null,
            pageSize: null,
            cancellationToken: CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<IdentityUserPageResponse>(response.Value);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(new IdentityManagementUserSearch(
            "ali",
            IdentityAccountStatus.Disabled,
            true,
            false,
            1,
            20), queries.LastSearch);
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
            new CreateIdentityUserRequest("alice", "Alice"),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<IdentityCredentialIssuanceResponse>(created.Value);
        Assert.Equal("temporary-once", response.TemporaryCredential);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
        Assert.DoesNotContain("temporary-once", JsonSerializer.Serialize(response.Account), StringComparison.Ordinal);
        Assert.Equal("user-1", accounts.LastCreate?.Actor.ActorId);
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
            "user-1",
            new UpdateIdentityUserRequest("New Name", 1),
            CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Equal("application/problem+json", Assert.Single(conflict.ContentTypes));
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(IdentityFailureCodes.RevisionConflict, problem.Extensions["code"]);
        Assert.Equal("user-1", Assert.IsType<IdentityAccountResponse>(problem.Extensions["current"]).EID);
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
                        UserEid = "user-1",
                        Type = AuthenticationSessionType.Regular,
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
        Assert.Equal("user-1", accounts.LastPasswordChange?.UserEid);
    }

    [Fact]
    public void Routes_SeparateLocalAdministratorManagementFromAuthenticatedSelfService()
    {
        var usersPolicy = Assert.Single(typeof(IdentityUsersController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("AdminOnly", usersPolicy.Policy);
        Assert.Single(typeof(IdentitySelfServiceController).GetCustomAttributes(typeof(AuthenticatedOnlyAttribute), true));
    }

    [Fact]
    public async Task Detail_IncludesDeletedTombstoneAndReversePagedReadOnlyEvents()
    {
        var repository = new InMemoryIdentityRepository();
        repository.Users.Add(new User
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
                TargetUserEID = "deleted-1",
                EventType = $"Event{index}",
                Reason = "safe reason",
                OccurredAt = new DateTime(2029, 1, index, 0, 0, 0, DateTimeKind.Utc),
                TraceId = $"trace-{index}",
                ActorKind = IdentitySecurityActorKind.User,
                ActorUserEID = "actor-1",
                CreatedBy = "actor-1",
                CreatedAt = new DateTime(2029, 1, index, 0, 0, 0, DateTimeKind.Utc),
                UpdatedBy = "actor-1",
                UpdatedAt = new DateTime(2029, 1, index, 0, 0, 0, DateTimeKind.Utc)
            });
        }

        var detail = await new IdentityManagementQueryService(repository)
            .FindAsync("deleted-1", eventPage: 2, eventPageSize: 2);

        Assert.Equal(IdentityAccountStatus.Deleted, detail?.Account.Status);
        Assert.Equal(3, detail?.Events.TotalCount);
        Assert.Equal("event-1", Assert.Single(detail!.Events.Items).EventEid);
    }

    private static IdentityUsersController Controller(RecordingAccountApplicationService accounts) => new(
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
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
                new Claim(ClaimTypes.Name, "alice"),
                new Claim(ClaimTypes.Role, "LocalAdmin")
            ], "test"))
        };
        context.TraceIdentifier = "trace-1";
        return new ControllerContext { HttpContext = context };
    }

    private static IdentityAccountSnapshot Account() => new()
    {
        UserEid = "user-1",
        ECODE = "alice",
        ENAME = "Alice",
        Status = IdentityAccountStatus.Enabled,
        Revision = 1
    };

    private sealed class RecordingManagementQueryService : IIdentityManagementQueryService
    {
        public IdentityManagementUserSearch? LastSearch { get; private set; }

        public ValueTask<IdentityManagementUserPage> SearchAsync(
            IdentityManagementUserSearch search,
            CancellationToken cancellationToken = default)
        {
            LastSearch = search;
            return ValueTask.FromResult(new IdentityManagementUserPage([], search.Page, search.PageSize, 0));
        }

        public ValueTask<IdentityManagementUserDetail?> FindAsync(
            string userEid,
            int eventPage,
            int eventPageSize,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
            return ValueTask.FromResult(ChangePasswordResult ?? throw new NotSupportedException());
        }

        public ValueTask<IdentityAccountSnapshot> DeleteAsync(DeleteIdentityAccountCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
