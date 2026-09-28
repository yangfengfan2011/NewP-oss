using System.Security.Claims;
using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public sealed class DataScopeObjectManagementTests
{
    [Fact]
    public async Task GetChecksManagementEligibilityBeforeReadingConfiguration()
    {
        using var fixture = new Fixture();
        fixture.Features.Decision = PermissionDecision.Denied;

        var result = await fixture.Management.GetAsync("authorization.role", "target", default);

        Assert.Equal(DataScopeObjectStatus.Forbidden, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(1, fixture.Features.Calls);
        Assert.Empty(fixture.Decisions.Calls);
    }

    public static IEnumerable<object[]> DeniedOperations()
    {
        foreach (var operation in new[] { "get", "save", "catalog", "subjects", "explain" })
            foreach (var denial in new[] { "feature", "read", "authorize" })
                yield return [operation, denial];
    }

    [Theory]
    [MemberData(nameof(DeniedOperations))]
    public async Task EveryOperationChecksSameTargetBeforeConfigurationDirectoryOrExplanation(string operation, string denial)
    {
        using var fixture = new Fixture();
        if (denial == "feature") fixture.Features.Decision = PermissionDecision.Denied;
        else fixture.Decisions.DeniedAction = denial;

        // 同时提供无效操作输入，证明资格拒绝先于目标一致性、目录和分析账号校验。
        var result = await Invoke(fixture, operation);

        Assert.Equal(denial == "read" ? DataScopeObjectStatus.NotFound : DataScopeObjectStatus.Forbidden, result.Status);
        Assert.Null(result.ValidationMessage);
        Assert.Equal(1, fixture.Features.Calls);
        Assert.Equal(denial == "feature" ? 0 : denial == "read" ? 1 : 2, fixture.Decisions.Calls.Count);
        Assert.All(fixture.Decisions.Calls, call =>
        {
            Assert.Equal("authorization.role", call.Resource);
            Assert.Equal("target", call.Object);
        });
        Assert.Empty(fixture.Directory.Searches);
    }

    [Theory]
    [InlineData("save")]
    [InlineData("subjects")]
    public async Task EligibilityValidationFaultsAreNotConvertedToInputErrors(string operation)
    {
        using var fixture = new Fixture();
        var failure = new RolePermissionValidationException("eligibility source failed");
        fixture.Decisions.Failure = failure;

        Assert.Same(failure, await Assert.ThrowsAsync<RolePermissionValidationException>(() => Invoke(fixture, operation)));
        Assert.Empty(fixture.Directory.Searches);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("save")]
    [InlineData("catalog")]
    [InlineData("subjects")]
    [InlineData("explain")]
    public async Task CancellationPropagatesThroughCompleteOperation(string operation)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failure = new OperationCanceledException(cancellation.Token);
        fixture.Decisions.Failure = failure;

        Assert.Same(failure, await Assert.ThrowsAsync<OperationCanceledException>(() => Invoke(fixture, operation, cancellation.Token)));
        Assert.Equal(cancellation.Token, fixture.Decisions.Token);
        Assert.Empty(fixture.Directory.Searches);
    }

    [Theory]
    [InlineData("authorization.role", "other-target")]
    [InlineData("other.resource", "target")]
    public async Task SaveChecksRouteTargetBeforeRejectingDifferentPayload(string resource, string id)
    {
        using var fixture = new Fixture();
        var result = await fixture.Management.SaveAsync("authorization.role", "target", new(resource, id, []), default);

        Assert.Equal(DataScopeObjectStatus.InvalidInput, result.Status);
        Assert.Equal("对象授权目标与请求不一致。", result.ValidationMessage);
        var controller = new ICP.Platform.Modules.Authorization.Controllers.DataScopeObjectsController(fixture.Management);
        var response = Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(
            await controller.Save("authorization.role", "target", new(resource, id, []), default));
        var problem = Assert.IsType<Microsoft.AspNetCore.Mvc.ProblemDetails>(response.Value);
        Assert.Equal("DataScope.InvalidConfiguration", problem.Extensions["code"]);
        Assert.True(problem.Extensions.TryGetValue("reasonKey", out var reason));
        Assert.Equal("AuthorizationErrors.DATA_SCOPE_OBJECT_TARGET_MISMATCH", reason);
        Assert.True(problem.Extensions.ContainsKey("fieldErrors"));
        Assert.Equal(new[] { ("authorization.role", "target", "read"), ("authorization.role", "target", "authorize") }, fixture.Decisions.Calls.Take(2));
        Assert.Empty(fixture.Directory.Searches);
    }

    [Fact]
    public async Task SaveValidationAfterEligibilityRemainsConfigurationError()
    {
        using var fixture = new Fixture();
        var result = await fixture.Management.SaveAsync("authorization.role", "target", new("authorization.role", "target",
            [new("invalid", "user", "operator", 0, new Dictionary<string, string> { ["invented"] = "allow" })]), default);

        Assert.Equal(DataScopeObjectStatus.InvalidInput, result.Status);
        Assert.Equal("对象主体授权项或动作意见无效。", result.ValidationMessage);
        Assert.Equal(2, fixture.Decisions.Calls.Count);
    }

    [Fact]
    public async Task SubjectsReturnsOwnerPageAfterQualificationAndPreservesInput()
    {
        using var fixture = new Fixture();
        fixture.Directory.Page = new([new("candidate", "候选")], true, [new("current", "已有")]);
        var result = await fixture.Management.SubjectsAsync("authorization.role", "target", "user", "search", 2, ["current"], default);

        Assert.Equal(DataScopeObjectStatus.Succeeded, result.Status);
        Assert.Same(fixture.Directory.Page, result.Value);
        var search = Assert.Single(fixture.Directory.Searches);
        Assert.Equal(("user", "search", 2), (search.Type, search.Query, search.Page));
        Assert.Equal(new[] { "current" }, search.CurrentIds);
        Assert.Equal(2, fixture.Decisions.Calls.Count);
    }

    [Fact]
    public async Task SubjectsValidationIsTranslatedButExplanationDirectoryFaultIsNot()
    {
        using var fixture = new Fixture();
        var invalid = await fixture.Management.SubjectsAsync("authorization.role", "target", "user", null, 0, [], default);
        Assert.Equal(DataScopeObjectStatus.InvalidInput, invalid.Status);
        Assert.Equal("主体目录页码无效。", invalid.ValidationMessage);
        var failure = new RolePermissionValidationException("directory unavailable");
        fixture.Directory.Failure = failure;

        var result = await fixture.Management.SubjectsAsync("authorization.role", "target", "user", null, 1, [], default);
        Assert.Equal(DataScopeObjectStatus.InvalidInput, result.Status);
        Assert.Equal(failure.Message, result.ValidationMessage);
        Assert.Same(failure, await Assert.ThrowsAsync<RolePermissionValidationException>(() =>
            fixture.Management.ExplainAsync("authorization.role", "target", "operator", default)));
    }

    [Theory]
    [InlineData(null, "operator")]
    [InlineData("missing", "missing")]
    public async Task ExplainDefaultsToOperatorAndMissingAccountStopsBeforeDecision(string? accountId, string expected)
    {
        using var fixture = new Fixture();
        var result = await fixture.Management.ExplainAsync("authorization.role", "target", accountId, default);

        Assert.Equal(DataScopeObjectStatus.NotFound, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(new[] { expected }, Assert.Single(fixture.Directory.Searches).CurrentIds);
        Assert.Equal("operator", fixture.Http.User.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(2, fixture.Decisions.Calls.Count);
    }

    [Fact]
    public async Task ExplainRejectsWhitespaceAndOversizedAccountOnlyAfterEligibility()
    {
        foreach (var account in new[] { " ", new string('a', 129) })
        {
            using var fixture = new Fixture();
            var result = await fixture.Management.ExplainAsync("authorization.role", "target", account, default);
            Assert.Equal(DataScopeObjectStatus.InvalidInput, result.Status);
            Assert.Null(result.ValidationMessage);
            Assert.Empty(fixture.Directory.Searches);
            Assert.Equal(2, fixture.Decisions.Calls.Count);
        }
    }

    [Fact]
    public async Task CatalogAndIndependentEligibilityExposeNoObjectConfiguration()
    {
        using var fixture = new Fixture();
        Assert.Equal(DataScopeObjectEligibility.Allowed, await fixture.Management.CheckAsync("authorization.role", "target", default));
        var result = await fixture.Management.CatalogAsync("authorization.role", "target", default);

        Assert.Equal(DataScopeObjectStatus.Succeeded, result.Status);
        Assert.Equal("user", Assert.Single(result.Value!.SubjectTypes).Key);
        Assert.Empty(fixture.Directory.Searches);
        Assert.Equal(4, fixture.Decisions.Calls.Count);
    }

    private static async Task<DataScopeObjectResult> Invoke(Fixture fixture, string operation, CancellationToken token = default) => operation switch
    {
        "get" => await fixture.Management.GetAsync("authorization.role", "target", token),
        "save" => await fixture.Management.SaveAsync("authorization.role", "target", new("other.resource", "other-target", []), token),
        "catalog" => await fixture.Management.CatalogAsync("authorization.role", "target", token),
        "subjects" => await fixture.Management.SubjectsAsync("authorization.role", "target", "missing", null, 0, [], token),
        "explain" => await fixture.Management.ExplainAsync("authorization.role", "target", " ", token),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private sealed class Fixture : IDisposable
    {
        // 不登记授权存储模型：越过拒绝结果读取配置时，真实 EF 依赖立即失败。
        private readonly EfPlatformDbContext db = new(new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options, []);
        internal FeatureSource Features { get; } = new();
        internal ObjectDecisions Decisions { get; } = new();
        internal IdentityDirectory Directory { get; } = new();
        internal DefaultHttpContext Http { get; } = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")], "test"))
        };
        internal DataScopeObjectManagement Management { get; }

        internal Fixture()
        {
            var identities = new DataScopeIdentities([Directory]);
            var module = new CanonicalModulePlanModule("Authorization", "1.0", "授权", null, "", null, null,
                null, null, "", [], [], [], [], [], new("test", "test", "/"))
            {
                Features = [new("Authorization", "manage", "navigation", "授权", null, "authorization.role.authorize")]
            };
            var plan = new CanonicalModulePlan("test", "1.0", "", "", [], [module]);
            var catalog = new DataScopeCatalog(new StandardCrudResourceRegistry([RoleReadResource.Create()]), plan, db, identities);
            Http.Request.Headers["X-NewP-Feature"] = plan.FeatureCatalog[0].Position;
            var grants = new DataScopeObjectGrants(db, null!, catalog, identities, null!, null!);
            Management = new(catalog, plan, Features, Decisions, grants, identities, new HttpContextAccessor { HttpContext = Http });
        }

        public void Dispose() => db.Dispose();
    }

    private sealed class FeatureSource : IFeatureDecisionSource
    {
        internal PermissionDecision Decision { get; set; } = PermissionDecision.Granted;
        internal int Calls { get; private set; }
        public ValueTask<PermissionDecision> EvaluateAsync(string subjectId, string position, string moduleId,
            string behaviorKey, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(Decision);
        }
    }

    private sealed class ObjectDecisions : IDataScopeObjectDecisionProvider
    {
        internal List<(string Resource, string Object, string Action)> Calls { get; } = [];
        internal string? DeniedAction { get; set; }
        internal Exception? Failure { get; set; }
        internal CancellationToken Token { get; private set; }
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken cancellationToken)
        {
            Calls.Add((resourceKey, objectId, actionKey));
            Token = cancellationToken;
            if (Failure is not null) throw Failure;
            return Task.FromResult(actionKey != DeniedAction);
        }
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate,
            string actionKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class IdentityDirectory : IDataScopeIdentityProvider
    {
        public IReadOnlyList<DataScopeSubjectType> SubjectTypes => [new("user", "用户", true)];
        public IReadOnlyList<DataScopeContextValue> ContextValues => [];
        internal List<(string Type, string? Query, int Page, IReadOnlyList<string> CurrentIds)> Searches { get; } = [];
        internal DataScopeSubjectPage Page { get; set; } = new([], false, []);
        internal Exception? Failure { get; set; }
        public Task<IDataScopeIdentityFacts> ResolveAsync(string accountId, CancellationToken token) => throw new NotSupportedException();
        public Task<DataScopeSubjectPage> SearchAsync(string type, string? query, int page, IReadOnlyList<string> currentIds, CancellationToken token)
        {
            Searches.Add((type, query, page, currentIds));
            if (Failure is not null) throw Failure;
            return Task.FromResult(Page);
        }
    }
}
