using System.Security.Claims;
using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Controllers;
using Microsoft.AspNetCore.Mvc;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public sealed class DataScopeObjectManagementTests
{
    [Fact]
    public async Task GetChecksManagementEligibilityBeforeReadingConfiguration()
    {
        using var fixture = new Fixture();
        fixture.Features.Decision = PermissionDecision.Denied;

        var result = await fixture.GetAsync("authorization.role", "Role_target", default);

        Assert.Equal(DataScopeObjectStatus.Forbidden, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(1, fixture.Features.Calls);
        Assert.Empty(fixture.Decisions.Calls);
    }

    // 保存的数据资格依赖真实ACL锁，完整写入反例见HostApi的DataScopeObjectManagementSqlServerTests。
    public static IEnumerable<object[]> DeniedOperations()
    {
        foreach (var operation in new[] { "get", "save", "catalog", "subjects", "explain" })
            foreach (var denial in new[] { "feature", "read", "authorize" })
                if (operation != "save" || denial == "feature") yield return [operation, denial];
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
            Assert.Equal("Role_target", call.Object);
        });
        Assert.Empty(fixture.Directory.Searches);
    }

    [Theory]
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

    [Fact]
    public async Task SubjectsReturnsOwnerPageAfterQualificationAndPreservesInput()
    {
        using var fixture = new Fixture();
        fixture.Directory.Page = new([new("candidate", "候选")], true, [new("current", "已有")]);
        var result = await fixture.SubjectsAsync("authorization.role", "Role_target", "user", "search", 2, ["current"], default);

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
        var invalid = await fixture.SubjectsAsync("authorization.role", "Role_target", "user", null, 0, [], default);
        Assert.Equal(DataScopeObjectStatus.InvalidInput, invalid.Status);
        Assert.Equal("主体目录页码无效。", invalid.ValidationMessage);
        var failure = new RolePermissionValidationException("directory unavailable");
        fixture.Directory.Failure = failure;

        var result = await fixture.SubjectsAsync("authorization.role", "Role_target", "user", null, 1, [], default);
        Assert.Equal(DataScopeObjectStatus.InvalidInput, result.Status);
        Assert.Equal(failure.Message, result.ValidationMessage);
        Assert.Same(failure, await Assert.ThrowsAsync<RolePermissionValidationException>(() =>
            fixture.ExplainAsync("authorization.role", "Role_target", "operator", default)));
    }

    [Theory]
    [InlineData(null, "operator")]
    [InlineData("missing", "missing")]
    public async Task ExplainDefaultsToOperatorAndMissingAccountStopsBeforeDecision(string? accountId, string expected)
    {
        using var fixture = new Fixture();
        var result = await fixture.ExplainAsync("authorization.role", "Role_target", accountId, default);

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
            var result = await fixture.ExplainAsync("authorization.role", "Role_target", account, default);
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
        Assert.Equal(DataScopeObjectEligibility.Allowed, await fixture.CheckAsync("authorization.role", "Role_target", default));
        var result = await fixture.CatalogAsync("authorization.role", "Role_target", default);

        Assert.Equal(DataScopeObjectStatus.Succeeded, result.Status);
        Assert.Equal(new[] { "creator", "user" }, result.Value!.SubjectTypes.Select(type => type.Key).Order(StringComparer.Ordinal));
        Assert.Empty(fixture.Directory.Searches);
        Assert.Equal(4, fixture.Decisions.Calls.Count);
    }

    private static async Task<DataScopeObjectResult> Invoke(Fixture fixture, string operation, CancellationToken token = default) => operation switch
    {
        "get" => await fixture.GetAsync("authorization.role", "Role_target", token),
        "save" => await fixture.SaveAsync("authorization.role", "Role_target", new("other.resource", "other-target", []), token),
        "catalog" => await fixture.CatalogAsync("authorization.role", "Role_target", token),
        "subjects" => await fixture.SubjectsAsync("authorization.role", "Role_target", "missing", null, 0, [], token),
        "explain" => await fixture.ExplainAsync("authorization.role", "Role_target", " ", token),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private sealed class Fixture : IDisposable
    {
        // 仅登记实际目标模型；不登记直接项存储模型，越过拒绝结果读取配置时真实 EF 依赖仍立即失败。
        // 部分模型必须按定义签名缓存，否则会与同进程内登记全部模型的上下文互相复用。
        private readonly EfPlatformDbContext db = new(new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options,
            [AuthorizationModelDefinitions.All.Single(model => model.Name == "Role")]);
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
            db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Role")).Add(new()
            {
                ["EID"] = "Role_target", ["ECODE"] = "target", ["ENAME"] = "Target", ["Description"] = null,
                ["IsEnabled"] = true, ["IsBuiltIn"] = false, ["CreatedAt"] = DateTime.UnixEpoch,
                ["CreatedBy"] = "operator", ["UpdatedAt"] = null, ["UpdatedBy"] = null
            });
            db.SaveChanges();
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
            Management = new(new ObjectActions(Decisions), catalog, plan, Features, Decisions, grants, identities, new HttpContextAccessor { HttpContext = Http }, null!, null!, null!);
        }

        // 本类验证当前业务处理器的顺序与错误契约；HTTP按钮身份由真实HostApi用例覆盖。
        private async Task<DataScopeObjectResult<T>> ReadAsync<T>(DataScopeObjectReadInput input, CancellationToken token)
        {
            if (await Management.CheckFeatureAsync(input.ResourceKey, token) != DataScopeObjectEligibility.Allowed)
                return new(DataScopeObjectStatus.Forbidden);
            var handler = (IDataScopeObjectReadHandler)Management;
            try
            {
                var result = await handler.WithinBoundaryAsync(input, "authorize", (state, cancellation) =>
                {
                    var target = Assert.IsType<ButtonExecutionTarget>(handler.Target(state));
                    Assert.Equal(input.ResourceKey, target.ResourceKey);
                    Assert.Equal(input.ObjectId, target.ObjectId);
                    return handler.ExecuteAsync(state, cancellation);
                }, token);
                var status = result.Status switch
                {
                    DataScopeAuthorizationStatus.Succeeded => DataScopeObjectStatus.Succeeded,
                    DataScopeAuthorizationStatus.Forbidden => DataScopeObjectStatus.Forbidden,
                    DataScopeAuthorizationStatus.NotFound => DataScopeObjectStatus.NotFound,
                    DataScopeAuthorizationStatus.InvalidInput => DataScopeObjectStatus.InvalidInput,
                    _ => throw new ArgumentOutOfRangeException(nameof(result))
                };
                return new(status, result.Value is null ? default : (T)result.Value, result.Error?.Message);
            }
            catch (ActionDataAccessException error) when (error.Status is 403 or 404)
            {
                return new(error.Status == 404 ? DataScopeObjectStatus.NotFound : DataScopeObjectStatus.Forbidden);
            }
        }

        internal Task<DataScopeObjectResult<object>> GetAsync(string resource, string id, CancellationToken token) =>
            ReadAsync<object>(new(resource, id, "configuration"), token);
        internal Task<DataScopeObjectResult<DataScopeObjectCatalog>> CatalogAsync(string resource, string id, CancellationToken token) =>
            ReadAsync<DataScopeObjectCatalog>(new(resource, id, "catalog"), token);
        internal Task<DataScopeObjectResult<DataScopeSubjectPage>> SubjectsAsync(string resource, string id, string type,
            string? query, int page, IReadOnlyList<string> currentIds, CancellationToken token) =>
            ReadAsync<DataScopeSubjectPage>(new(resource, id, "subjects", type, query, page, currentIds), token);
        internal Task<DataScopeObjectResult<DataScopeExplanationDto>> ExplainAsync(string resource, string id,
            string? account, CancellationToken token) =>
            ReadAsync<DataScopeExplanationDto>(new(resource, id, "explanation", AccountId: account), token);
        internal async Task<DataScopeObjectEligibility> CheckAsync(string resource, string id, CancellationToken token) =>
            (await ReadAsync<object>(new(resource, id, "eligibility"), token)).Status switch
            {
                DataScopeObjectStatus.Succeeded => DataScopeObjectEligibility.Allowed,
                DataScopeObjectStatus.NotFound => DataScopeObjectEligibility.NotFound,
                _ => DataScopeObjectEligibility.Forbidden
            };
        internal async Task<DataScopeObjectResult> SaveAsync(string resource, string id, DataScopeObjectDto input, CancellationToken token)
        {
            var controller = new DataScopeObjectsController(Management, new NeverExecute());
            Assert.IsType<ForbidResult>(await controller.Save(resource, id, input, token));
            return new(DataScopeObjectStatus.Forbidden);
        }

        public void Dispose() => db.Dispose();
    }

    private sealed class NeverExecute : IButtonBusinessExecutor
    {
        public Task<TResult> ExecuteAsync<TInput, TState, TResult>(IButtonBusinessHandler<TInput, TState, TResult> handler,
            TInput input, CancellationToken cancellationToken = default) => throw new InvalidOperationException("拒绝后不得进入按钮执行器。");
    }

    private sealed class ObjectActions(ObjectDecisions decisions) : IObjectActionAccess
    {
        public async Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("Role", objectName);
            foreach (var action in actions)
                if (!await decisions.IsAllowedAsync("authorization.role", objectId, action, cancellationToken))
                    return action == "read" ? DataExecutionDecision.NotFound : DataExecutionDecision.Denied;
            return DataExecutionDecision.Allowed;
        }
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
