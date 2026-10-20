using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationButtonCreateSqlServerTests
{
    [Fact]
    public void PositionCreate_RequestUsesOnlyTheActualClientCreateFields()
    {
        var descriptor = PositionCrudDescriptorFactory.Create();
        var values = Values("完整候选325");
        Assert.All(values.Keys, field => Assert.Contains(field, descriptor.FieldSet.CreateFields));
        Assert.All(descriptor.RequiredFields.Where(field => field.Scope is StandardCrudRequiredFieldScope.CreateOnly or StandardCrudRequiredFieldScope.CreateAndUpdate),
            field => Assert.True(values.ContainsKey(field.Field)));
        var enabled = Assert.Single(descriptor.FieldPolicies, field => field.Field == "IsEnabled");
        Assert.Equal(StandardCrudFieldValueOwner.ServerOnCreate, enabled.ValueOwner);
        Assert.Equal(true, enabled.CreateDefaultValue);
        Assert.DoesNotContain("IsEnabled", values.Keys);
        Assert.DoesNotContain("EID", values.Keys);
        Assert.DoesNotContain("CreatedBy", values.Keys);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "完整服务端候选create允许但read拒绝仍提交，候选拒绝或故障没有行和审计")]
    public async Task PositionCreate_ChecksTheCompleteCandidateWithoutRequiringTheNewObjectToBeReadable()
    {
        var probe = new CandidateProbe();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddScoped<IDataScopeObjectDecisionProvider>(provider =>
            new CandidateDecisions((DataScopeReadPlans)provider.GetRequiredService<IDataScopeReadPlanProvider>(), probe)));
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var feature = http.Organization("positions", "create", "list.pageActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Candidate325", "正式候选",
            "candidate-325", "候选主体", http.Organization("positions"), feature);
        using var ordinary = actor.Client;
        var button = fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog.Single(item =>
            item.ViewKey == "organization.position.list" && item.ActionKey == "create");
        await RuleAsync(fixture, admin, "allow");
        using (var created = await SendAsync(ordinary, feature, button.Id, "完整候选325"))
        {
            Assert.True(created.StatusCode == HttpStatusCode.Created, $"{created.StatusCode}: {await created.Content.ReadAsStringAsync()}");
            var receipt = await created.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(receipt.GetProperty("succeeded").GetBoolean());
            Assert.False(receipt.TryGetProperty("fields", out _));
        }
        var candidate = Assert.Single(probe.Candidates);
        Assert.StartsWith("Position_", Assert.IsType<string>(candidate["EID"]));
        Assert.Equal(true, candidate["IsEnabled"]);
        Assert.Equal(actor.AccountId, candidate["CreatedBy"]);
        Assert.IsType<DateTime>(candidate["CreatedAt"]);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var actual = await db.Set<Dictionary<string, object?>>("Position").AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "ENAME") == "完整候选325");
            Assert.Equal(candidate["EID"], actual["EID"]);
            Assert.Equal(true, actual["IsEnabled"]);
            Assert.Equal(actor.AccountId, actual["CreatedBy"]);
        }
        using (var unreadable = await FeatureHttpTestRequests.SendAsync(ordinary, http.Organization("positions"), HttpMethod.Get,
            "/api/organization/positions/" + candidate["EID"], dataAccess: http.Declaration("organization.position.read", http.Organization("positions"))))
            Assert.Equal(HttpStatusCode.NotFound, unreadable.StatusCode);

        // 自定义业务可直接检查全新候选，不依赖CRUD描述符、按钮、合同或调用者清单。
        var direct = new Dictionary<string, object?>(candidate) { ["EID"] = "Position_" + Guid.NewGuid().ToString("N") };
        Assert.Equal(DataExecutionDecision.Allowed, await CheckCandidateAsync(fixture, actor.AccountId, direct));
        await RuleAsync(fixture, admin, "deny");
        Assert.Equal(DataExecutionDecision.Denied, await CheckCandidateAsync(fixture, actor.AccountId, direct));
        var rows = await SnapshotAsync(fixture, "Position");
        var changes = await SnapshotAsync(fixture, "BusinessChange");
        using (var denied = await SendAsync(ordinary, feature, button.Id, "不得创建325"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(rows, await SnapshotAsync(fixture, "Position"));
        Assert.Equal(changes, await SnapshotAsync(fixture, "BusinessChange"));
        probe.Throw = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => CheckCandidateAsync(fixture, actor.AccountId, direct));
        using (var failed = await SendAsync(ordinary, feature, button.Id, "故障不得创建325"))
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(rows, await SnapshotAsync(fixture, "Position"));
        Assert.Equal(changes, await SnapshotAsync(fixture, "BusinessChange"));
    }

    private static async Task<DataExecutionDecision> CheckCandidateAsync(BusinessAuditHostFixture fixture, string actor,
        IReadOnlyDictionary<string, object?> candidate)
    {
        using var scope = fixture.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previous = accessor.HttpContext;
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, actor)], "trusted-business-candidate")) };
        try { return await scope.ServiceProvider.GetRequiredService<IObjectActionAccess>().CheckCandidateAsync("Position", candidate, ["create"]); }
        finally { accessor.HttpContext = previous; }
    }

    private static IReadOnlyDictionary<string, object?> Values(string name) => new Dictionary<string, object?> { ["ENAME"] = name };

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, FeaturePoint feature, string button, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/organization/positions");
        request.Headers.Add("X-NewP-Button", button);
        request.Headers.Add("X-NewP-Feature", feature.Position);
        request.Content = JsonContent.Create(Values(name), options: new JsonSerializerOptions());
        return SendAndDisposeAsync(client, request);
    }
    private static async Task<HttpResponseMessage> SendAndDisposeAsync(HttpClient client, HttpRequestMessage request)
    {
        using (request) return await client.SendAsync(request);
    }
    private static async Task RuleAsync(BusinessAuditHostFixture fixture, HttpClient admin, string create)
    {
        var rule = new DataScopeRuleDto("CompleteCandidate325", "CompleteCandidate325", null, "完整候选", true, "organization.position",
            new("attribute", "IsEnabled", "equal", JsonSerializer.SerializeToElement(true)),
            [new("CandidateSubject325", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny", ["create"] = create })], []);
        using var response = await FeatureHttpTestRequests.SendAsync(admin,
            fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save"), HttpMethod.Put,
            "/api/authorization/data-scope/rules/CompleteCandidate325", rule);
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }
    private static async Task<string> SnapshotAsync(BusinessAuditHostFixture fixture, string model)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        return JsonSerializer.Serialize(await db.Set<Dictionary<string, object?>>(model).AsNoTracking()
            .OrderBy(row => EF.Property<string>(row, "EID")).ToListAsync());
    }
    private sealed class CandidateProbe
    {
        internal bool Throw { get; set; }
        internal List<IReadOnlyDictionary<string, object?>> Candidates { get; } = [];
    }
    private sealed class CandidateDecisions(IDataScopeObjectDecisionProvider inner, CandidateProbe probe) : IDataScopeObjectDecisionProvider
    {
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken cancellationToken = default) =>
            inner.IsAllowedAsync(resourceKey, objectId, actionKey, cancellationToken);
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey,
            CancellationToken cancellationToken = default)
        {
            if (resourceKey == "organization.position")
            {
                probe.Candidates.Add(new Dictionary<string, object?>(candidate));
                if (probe.Throw) throw new InvalidOperationException("injected candidate decision failure");
            }
            return inner.IsCandidateAllowedAsync(resourceKey, candidate, actionKey, cancellationToken);
        }
    }
}