using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Transactions;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.FeatureHttpTestRequests;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationQuerySqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "稳定组织查询、正式人员摘要和display的缓存/权限/数据库边界")]
    public async Task BusinessQueries_UseOneImmutableCurrentViewAndManagementKeepsDatabaseQueries()
    {
        var counter = new OrganizationSqlObserver();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.AddSingleton<OrganizationCache, PausedRecoveryCache>();
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(counter));
        });
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var company = await Create("companies", new { ECODE = "QUERY", ENAME = "查询公司", SortOrder = "01" });
        var root = await Create("departments", new { ECODE = "ROOT", ENAME = "根部门", CompanyId = company, SortOrder = "01" });
        var middle = await Create("departments", new { ECODE = "MID", ENAME = "中间部门", CompanyId = company, ParentDepartmentId = root, SortOrder = "01" });
        var leaf = await Create("departments", new { ECODE = "LEAF", ENAME = "当前部门", CompanyId = company, ParentDepartmentId = middle, SortOrder = "01" });
        var position = await Create("positions", new { ENAME = "岗位", Description = "内部完整备注" });
        var person = await Create("persons", new { PersonNo = "QUERY", ENAME = "查询人员", BirthDate = "1990-02-03", EmploymentStatus = "Active", IsEnabled = true, CompanyId = company, DepartmentId = leaf, PositionId = position });
        var account = (await CreateAsync(admin, http.Identity("create"), "/api/identity/accounts", new { ECODE = "query-bound", ENAME = "账号名称" })).GetProperty("account");
        var accountId = account.GetProperty("eid").GetString()!;
        using (var bound = await SendAsync(admin, http.Organization("persons", "linkUser", "detail.custom"), HttpMethod.Put,
            $"/api/organization/persons/{person}/user-binding", new { accountEid = accountId, expectedRevision = account.GetProperty("revision").GetInt64(), reason = "查询绑定" }))
            Assert.Equal(HttpStatusCode.OK, bound.StatusCode);
        var query = fixture.Services.GetRequiredService<IOrganizationQuery>();
        counter.Commands.Clear();
        var view = await query.ReadAsync();
        var current = view.FindByAccount(accountId)!;
        Assert.Equal(new[] { middle, root }, current.Ancestors.Select(row => row["EID"]));
        Assert.Equal(new DateOnly(1990, 2, 3), current.Person["BirthDate"]);
        Assert.Equal("内部完整备注", view.Find(OrganizationRecordKind.Position, position)!["Description"]);
        Assert.Equal(company, current.Company!["EID"]);
        Assert.Equal(leaf, current.Department!["EID"]);
        Assert.Equal("岗位", Assert.Single(view.ResolveNames(OrganizationRecordKind.Position, [position, position, "missing"])).Value.Name);
        Assert.Null(view.FindByAccount("missing"));
        Assert.Empty(counter.Commands);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)current.Person)["ENAME"] = "污染");

        // 正式HTTP消费只需要人员读取位置；没有公司、部门或岗位管理权限也能取得既有display投影。
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "query-reader", "reader", "query-reader", "reader", http.Organization("persons"));
        using var reader = actor.Client;
        counter.Commands.Clear();
        var listing = await ReadAsync(reader, http.Organization("persons"), "/api/organization/persons?pageSize=1&sortBy=PersonNo&sortDirection=asc");
        var row = Assert.Single(listing.GetProperty("items").EnumerateArray());
        Assert.Equal("查询公司", row.GetProperty("CompanyId_DisplayValue").GetString());
        Assert.Equal("当前部门", row.GetProperty("DepartmentName").GetString());
        Assert.Equal("岗位", row.GetProperty("PositionName").GetString());
        Assert.DoesNotContain(counter.Commands, sql => sql.Contains("[Company]") || sql.Contains("[Department]") || sql.Contains("[Position]"));
        Assert.Contains(counter.Commands, sql => sql.Contains("COUNT(*)"));
        Assert.Contains(counter.Commands, sql => sql.Contains("OFFSET") && sql.Contains("FETCH NEXT"));
        counter.Commands.Clear();
        var detail = await ReadAsync(reader, http.Organization("persons"), $"/api/organization/persons/{person}");
        var binding = detail.GetProperty("summaries").GetProperty("userBinding");
        Assert.Equal("ready", binding.GetProperty("status").GetString());
        Assert.Equal(accountId, binding.GetProperty("data").GetProperty("accountEid").GetString());
        Assert.Equal("账号名称", binding.GetProperty("data").GetProperty("account").GetProperty("ename").GetString());
        Assert.Single(counter.Commands);
        Assert.Contains("[Person]", counter.Commands[0]);
        var descriptor = fixture.Services.GetRequiredService<IStandardCrudResourceRegistry>().FindByResourceKey("organization.person")!;
        Assert.All(detail.GetProperty("fields").EnumerateObject(), field => Assert.Contains(field.Name, descriptor.FieldSet.DetailFields.Concat(descriptor.DisplayValueFields.Select(f => f.Field)).Concat(descriptor.DisplayProjectionPlan?.Fields.Select(f => f.CompanionField) ?? [])));
        using (var forbidden = await SendAsync(reader, http.Organization("companies"), HttpMethod.Get, "/api/organization/companies"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.Equal("application/problem+json", forbidden.Content.Headers.ContentType?.MediaType);
            var denied = await forbidden.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("AUTH_FORBIDDEN", denied.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(denied.GetProperty("traceId").GetString()));
        }
        using (var moved = await SendAsync(admin, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/persons/{person}", new { CompanyId = company, DepartmentId = root }))
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(leaf, current.Department["EID"]);
        Assert.Equal(root, (await query.ReadAsync()).FindByAccount(accountId)!.Department!["EID"]);

        // 稳定契约逐字段保留四类真实持久化记录，包括审计和对外不需要的内部字段。
        await using (var completeScope = fixture.Services.CreateAsyncScope())
        {
            var db = completeScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var complete = await query.ReadAsync();
            foreach (var (kind, id) in new[] { (OrganizationRecordKind.Company, company), (OrganizationRecordKind.Department, leaf),
                (OrganizationRecordKind.Position, position), (OrganizationRecordKind.Person, person) })
            {
                var stored = await db.Set<Dictionary<string, object?>>($"Organization/{kind}").AsNoTracking()
                    .SingleAsync(row => EF.Property<string>(row, "EID") == id);
                var record = complete.Find(kind, id)!;
                Assert.Equal(stored.Keys.Order(), record.Keys.Order());
                foreach (var field in stored) Assert.Equal(field.Value, record[field.Key]);
            }
        }

        // 候选沿可信主体的正式入口执行固定 SQL；完整业务事实已由上面的快照逐字段断言。
        counter.Commands.Clear();
        using (var candidates = await SendAsync(admin, http.Organization("persons"), HttpMethod.Get,
            "/api/organization/person-lookups/companies?page=1&pageSize=1"))
        {
            Assert.Equal(HttpStatusCode.OK, candidates.StatusCode);
            var page = await candidates.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Single(page.GetProperty("items").EnumerateArray());
        }
        Assert.NotEmpty(counter.Commands);

        // 固定管理SQL读取不豁免端点可用性；暂停恢复使503断言不与后台成功恢复竞争。
        ((PausedRecoveryCache)fixture.Services.GetRequiredService<OrganizationCache>()).HoldRecovery = true;
        await using (var write = await fixture.Services.GetRequiredService<OrganizationCache>().BeginAsync(default)) write.MarkUnavailable();
        await Assert.ThrowsAsync<PlatformCacheUnavailableException>(() => query.ReadAsync().AsTask());
        using var unavailable = await SendAsync(reader, http.Organization("persons"), HttpMethod.Get, "/api/organization/persons");
        await AssertUnavailable(unavailable);
        counter.Commands.Clear();
        using (var management = await SendAsync(admin, http.Organization("companies"), HttpMethod.Get, $"/api/organization/companies/{company}"))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, management.StatusCode);
        using (var unavailableCandidates = await SendAsync(admin, http.Organization("persons"), HttpMethod.Get,
            "/api/organization/person-lookups/companies?page=1&pageSize=1"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailableCandidates.StatusCode);
            var problem = await unavailableCandidates.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Authorization.DecisionUnavailable", problem.GetProperty("code").GetString());
        }
        await using var scope = fixture.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var previousContext = accessor.HttpContext;
        // 已登录的 fixture 管理员使用真实 bootstrap AccountEid；只在本作用域恢复内部候选调用所需的可信主体。
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, fixture.ActorId)], "FixtureAdministrator"))
        };
        try
        {
            Assert.Single((await scope.ServiceProvider.GetRequiredService<ICompanyDirectory>()
                .LookupAsync(new(Keyword: null, Page: 1, PageSize: 1))).Items);
        }
        finally
        {
            accessor.HttpContext = previousContext;
        }
        Assert.NotEmpty(counter.Commands);
        Assert.Contains(counter.Commands, sql => sql.Contains("[Company]") && sql.Contains("OFFSET"));

        async Task<string> Create(string resource, object values) => BusinessAuditSqlServerHttpApiTests.Id(
            await CreateAsync(admin, http.Organization(resource, "create", "list.pageActions"), "/api/organization/" + resource, values));
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "摘要缓存读取故障不得被普通附属摘要降级吞掉")]
    public async Task Detail_WhenCacheBecomesUnavailableAfterDisplay_ReturnsUnavailableInsteadOfEmptySummary()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.AddSingleton<OrganizationCache, PausedRecoveryCache>();
            services.AddSingleton<IOrganizationQuery, InvalidatingQuery>();
        });
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var person = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "SUMMARY", ENAME = "摘要人员", EmploymentStatus = "Active", IsEnabled = true }));
        ((InvalidatingQuery)fixture.Services.GetRequiredService<IOrganizationQuery>()).InvalidateAfterRead = true;
        using var response = await SendAsync(admin, http.Organization("persons"), HttpMethod.Get, $"/api/organization/persons/{person}");
        await AssertUnavailable(response);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "审计组织名称的关键缓存故障必须503，不能伪作名称缺失")]
    public async Task Audit_WhenOrganizationCacheUnavailable_ReturnsUnavailableForListAndDetail()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddSingleton<OrganizationCache, PausedRecoveryCache>());
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var person = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "AUDIT", ENAME = "审计人员", EmploymentStatus = "Active", IsEnabled = true }));
        var page = await ReadAsync(admin, http.Audit(), $"/api/audit/business-changes?TargetId={person}");
        var id = Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("EID").GetString();
        ((PausedRecoveryCache)fixture.Services.GetRequiredService<OrganizationCache>()).HoldRecovery = true;
        await using (var write = await fixture.Services.GetRequiredService<OrganizationCache>().BeginAsync(default)) write.MarkUnavailable();
        foreach (var route in new[] { $"/api/audit/business-changes?TargetId={person}", $"/api/audit/business-changes/{id}" })
        {
            using var response = await SendAsync(admin, http.Audit(), HttpMethod.Get, route);
            await AssertUnavailable(response);
        }
    }

    private static async Task AssertUnavailable(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(503, problem.GetProperty("status").GetInt32());
        Assert.Equal("Persistence.CacheUnavailable", problem.GetProperty("code").GetString());
        Assert.Equal("Persistence.CacheUnavailable", problem.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
    }

    private sealed class InvalidatingQuery(OrganizationCache cache) : IOrganizationQuery
    {
        public bool InvalidateAfterRead;
        public async ValueTask<IOrganizationReadView> ReadAsync(CancellationToken cancellationToken = default)
        {
            var result = await new OrganizationQuery(cache).ReadAsync(cancellationToken);
            if (InvalidateAfterRead)
            {
                InvalidateAfterRead = false;
                ((PausedRecoveryCache)cache).HoldRecovery = true;
                await using var write = await cache.BeginAsync(cancellationToken);
                write.MarkUnavailable();
            }
            return result;
        }
    }

    private sealed class PausedRecoveryCache(IServiceScopeFactory scopes) : OrganizationCache(scopes)
    {
        public bool HoldRecovery;
        protected override async ValueTask<OrganizationSnapshot> LoadAsync(CancellationToken token)
        {
            if (HoldRecovery) await Task.Delay(Timeout.Infinite, token);
            return await base.LoadAsync(token);
        }
    }

    private sealed class OrganizationSqlObserver : DbCommandInterceptor
    {
        internal List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (new[] { "[Company]", "[Department]", "[Position]", "[Person]" }.Any(command.CommandText.Contains)) Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
