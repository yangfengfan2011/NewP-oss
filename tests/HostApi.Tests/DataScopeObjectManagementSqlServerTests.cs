using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeObjectManagementSqlServerTests
{
    // 写入资格已移到真实SQL锁内；原InMemory测试的拒绝顺序和错误传播由同一完整操作验证。
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "generic write qualifies before payload validation and preserves decision faults/cancellation")]
    public async Task GenericReplacement_QualifiesBeforePayloadValidation_AndPropagatesDecisionFailures()
    {
        var faults = new DecisionFaults();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddScoped<IDataScopeObjectDecisionProvider>(provider => new FaultingDecisions(
                (IDataScopeObjectDecisionProvider)provider.GetRequiredService<IDataScopeReadPlanProvider>(), faults)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var part = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "WriteEligibility");
        var read = fixture.FeatureCatalog.First(point => point.ModuleId == "Part" && point.BehaviorKey == "part.part.read");
        var manage = fixture.FeatureCatalog.First(point => point.ModuleId == "Part" && point.BehaviorKey == "part.part.authorize"
            && point.ActionScope == "list.rowActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "WriteEligibility", "授权写入资格", "writeeligibility", "授权写入资格", read, manage);
        using var ordinary = actor.Client;
        var path = "/api/authorization/data-scope/objects/part.part/" + part.Id;
        DataScopeObjectDto Configuration(string readOpinion, string authorizeOpinion) => new("part.part", part.Id,
            [new("write-eligibility", "user", actor.AccountId, 1, new Dictionary<string, string>
                { ["read"] = readOpinion, ["authorize"] = authorizeOpinion })]);
        var allowed = Configuration("allow", "allow");
        var invalid = new[] { allowed with { ResourceKey = "other.resource" }, allowed with { ObjectId = "other-target" },
            allowed with { Subjects = [allowed.Subjects[0] with { Actions = new Dictionary<string, string> { ["invented"] = "allow" } }] } };
        foreach (var denial in new[] { "read", "authorize" })
        {
            using var initialized = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path,
                Configuration(denial == "read" ? "deny" : "allow", denial == "authorize" ? "deny" : "allow"));
            Assert.Equal(HttpStatusCode.NoContent, initialized.StatusCode);
            foreach (var input in invalid)
            {
                using var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put, path, input);
                Assert.Equal(denial == "read" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, response.StatusCode);
            }
        }
        using (var initialized = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, allowed))
            Assert.Equal(HttpStatusCode.NoContent, initialized.StatusCode);
        var before = await FeatureHttpTestRequests.ReadAsync(admin, manage, path);
        foreach (var input in invalid)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put, path, input);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("DataScope.InvalidConfiguration", problem.GetProperty("code").GetString());
            Assert.Equal(input == invalid[2] ? "AuthorizationErrors.DATA_SCOPE_OBJECT_SUBJECT_INVALID"
                : "AuthorizationErrors.DATA_SCOPE_OBJECT_TARGET_MISMATCH", problem.GetProperty("reasonKey").GetString());
            Assert.True(problem.TryGetProperty("fieldErrors", out _));
        }
        var oversizedId = new string('x', 129);
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put,
            "/api/authorization/data-scope/objects/part.part/" + oversizedId, allowed with { ObjectId = oversizedId }))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        faults.ObjectId = part.Id;
        faults.Failure = new RolePermissionValidationException("decision source failed");
        try
        {
            using var failed = await FeatureHttpTestRequests.SendAsync(ordinary, manage, HttpMethod.Put, path, allowed);
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        }
        finally { faults.Failure = null; }
        using (var scope = fixture.Services.CreateScope())
        using (var cancellation = new CancellationTokenSource())
        using (var activity = new Activity("authorization-decision-cancellation").Start())
        {
            var context = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var previous = context.HttpContext;
            context.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.AccountId)], "test"))
            };
            context.HttpContext.Request.Headers["X-NewP-Feature"] = manage.Position;
            var failure = new OperationCanceledException(cancellation.Token);
            faults.Failure = failure;
            try
            {
                Assert.Same(failure, await Assert.ThrowsAsync<OperationCanceledException>(() =>
                    scope.ServiceProvider.GetRequiredService<DataScopeObjectManagement>().SaveAsync("part.part", part.Id, allowed, cancellation.Token)));
            }
            finally { faults.Failure = null; context.HttpContext = previous; }
        }
        Assert.Equal(before.GetRawText(), (await FeatureHttpTestRequests.ReadAsync(admin, manage, path)).GetRawText());
    }

    private sealed class DecisionFaults
    {
        internal string? ObjectId { get; set; }
        internal Exception? Failure { get; set; }
    }

    private sealed class FaultingDecisions(IDataScopeObjectDecisionProvider inner, DecisionFaults faults) : IDataScopeObjectDecisionProvider
    {
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken token = default)
        {
            if (objectId == faults.ObjectId && faults.Failure is { } failure) throw failure;
            return inner.IsAllowedAsync(resourceKey, objectId, actionKey, token);
        }

        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate,
            string actionKey, CancellationToken token = default) => inner.IsCandidateAllowedAsync(resourceKey, candidate, actionKey, token);
    }
}
