using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeOwnerAuthorizationConcurrencySqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "owner authorization cannot overwrite a revocation committed while its ACL lock was pending")]
    public Task PartOwnerReplacement_RechecksObjectAuthorizationAfterWaitingForItsAclLock() => RecheckAfterAclWaitAsync(false);

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "generic authorization cannot overwrite a revocation committed while its ACL lock was pending")]
    public Task GenericReplacement_RechecksObjectAuthorizationAfterWaitingForItsAclLock() => RecheckAfterAclWaitAsync(true);

    private static async Task RecheckAfterAclWaitAsync(bool generic)
    {
        var gate = new AclGate();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(gate)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "OwnerAclRace297");
        var read = fixture.FeatureCatalog.First(point => point.ModuleId == "Part" && point.BehaviorKey == "part.part.read");
        var manage = fixture.FeatureCatalog.First(point => point.ModuleId == "Part" && point.BehaviorKey == "part.part.authorize"
            && point.ActionScope == "list.rowActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "OwnerRace297", "Owner race manager", "ownerrace297", "Owner race manager", read, manage);
        using var ordinary = actor.Client;
        var path = generic ? "/api/authorization/data-scope/objects/part.part/" + part.Id : "/api/part/parts/" + part.Id + "/permissions";
        DataScopeObjectDto Configuration(string opinion) => new("part.part", part.Id, [new("owner-race-entry", "user", actor.AccountId, 1,
            new Dictionary<string, string> { ["read"] = "allow", ["authorize"] = opinion })]);
        using (var initialized = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, Configuration("allow")))
            Assert.Equal(HttpStatusCode.NoContent, initialized.StatusCode);
        var previousEvents = await EventsAsync(fixture, part.Id);
        var administrator = await admin.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        gate.Context = fixture.Services.GetRequiredService<IHttpContextAccessor>();
        gate.Administrator = administrator.GetProperty("id").GetString()!;
        gate.Actor = actor.AccountId;
        gate.Target = "Authorization.DataScopeObject:part.part:" + part.Id;
        gate.Active = true;
        var revocation = FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, Configuration("deny"));
        Task<HttpResponseMessage>? replacement = null;
        try
        {
            await gate.BlockerLocked.Task.WaitAsync(TimeSpan.FromSeconds(30));
            replacement = FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put, path, Configuration("allow"));
            await gate.OrdinaryAttempt.Task.WaitAsync(TimeSpan.FromSeconds(30));
            gate.Release.TrySetResult();
            using var revoked = await revocation;
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
            using var response = await replacement;
            var stored = await FeatureHttpTestRequests.ReadAsync(admin, manage, path);
            var eventDelta = await EventsAsync(fixture, part.Id) - previousEvents;
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                $"Expected PUT 403 after committed revocation, got {(int)response.StatusCode}; current={stored.GetRawText()}; eventDelta={eventDelta}");
            Assert.Equal("deny", Assert.Single(stored.GetProperty("subjects").EnumerateArray()).GetProperty("actions").GetProperty("authorize").GetString());
            Assert.Equal(1, eventDelta);
        }
        finally
        {
            gate.Release.TrySetResult();
            var cancelPending = !revocation.IsCompleted || replacement is { IsCompleted: false };
            if (cancelPending) { admin.CancelPendingRequests(); ordinary.CancelPendingRequests(); }
            try
            {
                Task[] requests = replacement is null ? [revocation] : [revocation, replacement];
                await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (OperationCanceledException) when (cancelPending) { }
            finally { gate.Active = false; }
        }
    }

    private static async Task<int> EventsAsync(BusinessAuditHostFixture fixture, string objectId)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("BusinessChange")
            .CountAsync(row => EF.Property<string>(row, "EventType") == "dataScope.object.changed"
                && EF.Property<string>(row, "TargetId") == "part.part/" + objectId);
    }

    // 只在真实SQL appLock边界控制两个正常HTTP请求的调度，不替换决策或伪造资格。
    private sealed class AclGate : DbCommandInterceptor
    {
        internal IHttpContextAccessor? Context { get; set; }
        internal string Administrator { get; set; } = "";
        internal string Actor { get; set; } = "";
        internal string Target { get; set; } = "";
        internal bool Active { get; set; }
        internal TaskCompletionSource BlockerLocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource OrdinaryAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool Matches(DbCommand command) => Active && command.CommandText.Contains("sp_getapplock", StringComparison.Ordinal)
            && command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is string value && value == Target);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (Matches(command) && Context?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) == Actor)
                OrdinaryAttempt.TrySetResult();
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken token = default)
        {
            if (Matches(command) && Context?.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) == Administrator)
            {
                BlockerLocked.TrySetResult();
                await Release.Task.WaitAsync(token);
            }
            return result;
        }
    }
}
