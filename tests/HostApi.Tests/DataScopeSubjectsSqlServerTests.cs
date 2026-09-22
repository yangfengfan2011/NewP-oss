using System.Net;
using System.Text.Json;
using System.Net.Http.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Persistence.EfCore;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeSubjectsSqlServerTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "最小主体目录在SQL中搜索分页，当前值仅窄投影")]
    public async Task SubjectDirectory_PagesAndProjectsAtDatabase()
    {
        var commands = new DirectoryCommands();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(commands)));
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.read");
        for (var i = 0; i < 25; i++)
            await FeatureHttpTestRequests.CreateAsync(admin, http.Organization("companies", "create", "list.pageActions"), "/api/organization/companies",
                new { ECODE = $"DIR-{i:00}", ENAME = $"主体目录-{i:00}", SortOrder = "0010" });
        commands.Queries.Clear();
        var watch = Stopwatch.StartNew();
        var first = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/authorization/data-scope/subjects/company?query=主体目录&page=1");
        var second = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/authorization/data-scope/subjects/company?query=主体目录&page=2");
        watch.Stop();
        Assert.Equal(20, first.GetProperty("items").GetArrayLength());
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        Assert.Equal(5, second.GetProperty("items").GetArrayLength());
        Assert.False(second.GetProperty("hasMore").GetBoolean());
        Assert.Empty(first.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetString())
            .Intersect(second.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetString())));
        var pages = commands.Queries.Where(sql => sql.Contains("OFFSET", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, pages.Length);
        Assert.All(pages, sql => { Assert.Contains("WHERE", sql); Assert.Contains("FETCH NEXT", sql); Assert.DoesNotContain("[CreatedAt]", sql); Assert.DoesNotContain("[IsEnabled]", sql); });
        output.WriteLine(JsonSerializer.Serialize(new { directoryRows = 25, returned = new[] { 20, 5 }, pageFetchLimit = 21,
            companyCommands = commands.Queries.Count, elapsedMs = watch.Elapsed.TotalMilliseconds, sql = commands.Queries }));
        var known = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray())
            .Select(row => row.GetProperty("id").GetString()!).ToArray();
        var requested = known.Concat(Enumerable.Range(0, 101).Select(i => $"absent-{i}")).ToArray();
        commands.Queries.Clear();
        var many = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/authorization/data-scope/subjects/company?query=no-match&"
            + string.Join("&", requested.Select(id => "currentIds=" + Uri.EscapeDataString(id))));
        Assert.Equal(25, many.GetProperty("currentValues").GetArrayLength());
        Assert.Equal(3, commands.Queries.Count); // 一次候选页与两批已配置身份，不把100变成配置容量上限。
    }

    private sealed class DirectoryCommands : DbCommandInterceptor
    {
        internal List<string> Queries { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM [dbo].[Company]", StringComparison.Ordinal)) Queries.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "主体事实缺失不命中，执行故障不伪装为默认允许")]
    public async Task OrganizationFacts_MissingAndFailureRemainDistinct()
    {
        var query = new ControlledOrganizationQuery();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            var registration = services.Single(item => item.ServiceType == typeof(IDataScopeIdentityProvider)
                && item.ImplementationType == typeof(OrganizationDataScopeIdentities));
            services.Remove(registration);
            services.AddScoped<IDataScopeIdentityProvider>(provider => new OrganizationDataScopeIdentities(
                query.Attach(provider.GetRequiredService<IOrganizationQuery>()), provider.GetRequiredService<EfPlatformDbContext>()));
        });
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var people = http.Organization("persons");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Facts", "事实", "facts-reader", "事实读取者", people);
        using var ordinary = actor.Client;
        var org = (await fixture.Services.GetRequiredService<IOrganizationQuery>().ReadAsync()).FindByAccount(actor.AccountId)!;
        var target = (string)org.Person["EID"]!;
        var rule = new DataScopeRuleDto("Scope_Facts", "Scope_Facts", null, "当前事实", true, "organization.person",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(target)),
            [new("Facts_Subject", "company", (string)org.Company!["EID"]!, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/Scope_Facts", rule))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        foreach (var (mode, expected) in new[] { ("current", HttpStatusCode.NotFound), ("missing", HttpStatusCode.OK), ("failure", HttpStatusCode.InternalServerError) })
        {
            query.Mode = mode;
            query.Reads = 0;
            using var read = await FeatureHttpTestRequests.SendAsync(ordinary, people, HttpMethod.Get, $"/api/organization/persons/{target}");
            Assert.Equal(expected, read.StatusCode);
            Assert.Equal(1, query.Reads);
        }
        using (var read = await FeatureHttpTestRequests.SendAsync(admin, people, HttpMethod.Get, $"/api/organization/persons/{target}"))
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        rule = rule with { Condition = new("attribute", "EID", "equal", JsonSerializer.SerializeToElement<object?>(null), "current.person"),
            Subjects = [rule.Subjects[0] with { Type = "all", SubjectId = null }] };
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/Scope_Facts", rule))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        foreach (var (mode, expected) in new[] { ("current", HttpStatusCode.NotFound), ("missing", HttpStatusCode.OK), ("failure", HttpStatusCode.InternalServerError) })
        {
            query.Mode = mode;
            query.Reads = 0;
            using var read = await FeatureHttpTestRequests.SendAsync(ordinary, people, HttpMethod.Get, $"/api/organization/persons/{target}");
            Assert.Equal(expected, read.StatusCode);
            Assert.Equal(1, query.Reads);
        }
    }

    private sealed class ControlledOrganizationQuery : IOrganizationQuery
    {
        private IOrganizationQuery inner = null!;
        internal string Mode { get; set; } = "current";
        internal int Reads { get; set; }
        internal ControlledOrganizationQuery Attach(IOrganizationQuery value) { inner = value; return this; }
        public async ValueTask<IOrganizationReadView> ReadAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            if (Mode == "failure") throw new InvalidOperationException("TEST_ORGANIZATION_FACTS_UNAVAILABLE");
            var view = await inner.ReadAsync(cancellationToken);
            return Mode == "missing" ? new MissingRelationshipView(view) : view;
        }
    }

    private sealed class MissingRelationshipView(IOrganizationReadView inner) : IOrganizationReadView
    {
        public CurrentOrganization? FindByAccount(string accountId) => null;
        public IReadOnlyDictionary<string, object?>? Find(OrganizationRecordKind kind, string id) => inner.Find(kind, id);
        public IReadOnlyDictionary<string, object?>? FindCompanyByCode(string code) => inner.FindCompanyByCode(code);
        public IReadOnlyDictionary<string, OrganizationName> ResolveNames(OrganizationRecordKind kind, IReadOnlyCollection<string> ids) => inner.ResolveNames(kind, ids);
    }
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "公司部门当前关系、身份域与最小管理目录经真实模块和SQL消费")]
    public async Task OrganizationAndContext_UsePublishedRelationshipsAndMinimalDirectory()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.read");
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var people = http.Organization("persons");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "OrganizationScope", "组织范围", "organization-scope", "组织范围管理员", people, entry, save);
        using var ordinary = actor.Client;
        var org = (await fixture.Services.GetRequiredService<IOrganizationQuery>().ReadAsync()).FindByAccount(actor.AccountId)!;
        var person = (string)org.Person["EID"]!;
        var company = (string)org.Company!["EID"]!;
        var department = (string)org.Department!["EID"]!;
        var child = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin,
            http.Organization("departments", "create", "list.pageActions"), "/api/organization/departments",
            new { ECODE = "SCOPE-CHILD", ENAME = "下级部门", CompanyId = company, ParentDepartmentId = department, SortOrder = "0010" }));
        var other = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin,
            http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
            new { PersonNo = "SCOPE-OTHER", ENAME = "同公司其他人员", CompanyId = company, DepartmentId = child, EmploymentStatus = "Active", IsEnabled = true }));
        var rule = new DataScopeRuleDto("Scope_Organization", "Scope_Organization", null, "组织主体", true, "organization.person",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(other)),
            [new("Org_All", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" }),
             new("Org_Allow", "company", company, 10, new Dictionary<string, string> { ["read"] = "allow" })], []);
        await SaveAndRead(HttpStatusCode.OK);
        rule = rule with { Subjects = [rule.Subjects[0], rule.Subjects[1] with { Type = "department", SubjectId = department }] };
        await SaveAndRead(HttpStatusCode.OK);
        using (var moved = await FeatureHttpTestRequests.SendAsync(admin, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/persons/{person}", new { CompanyId = company, DepartmentId = child }))
            Assert.True(moved.IsSuccessStatusCode, await moved.Content.ReadAsStringAsync());
        // 同一个 Cookie 在 owner 发布后匹配新的直属部门，默认不匹配祖先。
        await ReadOther(HttpStatusCode.NotFound);
        rule = rule with { Subjects = [rule.Subjects[0], rule.Subjects[1] with { MatchOptions = new(true) }] };
        await SaveAndRead(HttpStatusCode.OK);
        rule = rule with { Subjects = [rule.Subjects[0], rule.Subjects[1] with { Priority = 0 }] };
        await SaveAndRead(HttpStatusCode.NotFound);
        rule = rule with { Subjects = [rule.Subjects[0], rule.Subjects[1] with { Type = "role", SubjectId = actor.RoleId, Priority = 10, MatchOptions = null }] };
        await SaveAndRead(HttpStatusCode.OK);
        using (var removed = await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put,
            $"/api/authorization/users/{actor.AccountId}/roles", new { RoleCodes = Array.Empty<string>() })) Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using (var scope = fixture.Services.CreateScope())
        {
            var provider = scope.ServiceProvider.GetServices<IDataScopeIdentityProvider>().Single(item => item.SubjectTypes.Any(type => type.Key == "role"));
            Assert.False((await provider.ResolveAsync(actor.AccountId, default)).Matches("role", actor.RoleId, new()));
        }
        using (var restored = await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put,
            $"/api/authorization/users/{actor.AccountId}/roles", new { RoleCodes = new[] { "OrganizationScope" } })) Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
        // 目录管理权限不授予公司业务读取，已选值名称仍从同一最小目录返回。
        using (var denied = await FeatureHttpTestRequests.SendAsync(ordinary, http.Organization("companies"), HttpMethod.Get, $"/api/organization/companies/{company}"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await http.GrantAsync(admin, actor.RoleId, people, entry, save, http.Organization("companies"));
        var companyRule = new DataScopeRuleDto("Scope_CompanyDirectory", "Scope_CompanyDirectory", null, "公司业务不可读", true, "organization.company",
            new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(company)),
            [new("CompanyDirectory_All", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        using (var saved = await FeatureHttpTestRequests.SendAsync(ordinary, save, HttpMethod.Put, "/api/authorization/data-scope/rules/Scope_CompanyDirectory", companyRule))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using (var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, http.Organization("companies"), HttpMethod.Get, $"/api/organization/companies/{company}"))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        // 具备业务功能却被数据范围隐藏的公司仍可在独立管理目录识别。
        foreach (var (type, id) in new[] { ("company", company), ("department", department), ("user", actor.AccountId), ("role", actor.RoleId) })
        {
            var directory = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, $"/api/authorization/data-scope/subjects/{type}?query=no-match&currentIds={id}");
            Assert.Empty(directory.GetProperty("items").EnumerateArray());
            var current = Assert.Single(directory.GetProperty("currentValues").EnumerateArray());
            Assert.Equal(id, current.GetProperty("id").GetString());
            Assert.Equal(new[] { "id", "name", "parentId" }, current.EnumerateObject().Select(property => property.Name).ToArray());
        }
        var outsider = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ScopeOutsider", "无管理资格", "scope-outsider", "无管理用户", people);
        using (outsider.Client)
        using (var denied = await FeatureHttpTestRequests.SendAsync(outsider.Client, entry, HttpMethod.Get, "/api/authorization/data-scope/subjects/user"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        // 四个有类型身份值从真实保存进入 SQL；无关联人员不会误命中当前组织。
        foreach (var (field, key, target) in new[] { ("AccountEID", "current.account", person), ("EID", "current.person", person),
                     ("CompanyId", "current.company", other), ("DepartmentId", "current.department", other) })
        {
            rule = rule with { Condition = new("attribute", field, "equal", JsonSerializer.SerializeToElement<object?>(null), key), Subjects = [rule.Subjects[0]] };
            using var saved = await FeatureHttpTestRequests.SendAsync(ordinary, save, HttpMethod.Put, "/api/authorization/data-scope/rules/Scope_Organization", rule);
            Assert.True(saved.StatusCode == HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());
            using var read = await FeatureHttpTestRequests.SendAsync(ordinary, people, HttpMethod.Get, $"/api/organization/persons/{target}");
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
            var list = await FeatureHttpTestRequests.ReadAsync(ordinary, people, "/api/organization/persons?page=1&pageSize=100");
            Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), row => row.GetProperty("EID").GetString() == target);
        }
        rule = rule with { Condition = rule.Condition with { ContextKey = "current.account" } };
        using var invalid = await FeatureHttpTestRequests.SendAsync(ordinary, save, HttpMethod.Put, "/api/authorization/data-scope/rules/Scope_Organization", rule);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("DataScope.InvalidConfiguration", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        async Task ReadOther(HttpStatusCode expected)
        {
            using var read = await FeatureHttpTestRequests.SendAsync(ordinary, people, HttpMethod.Get, $"/api/organization/persons/{other}");
            Assert.Equal(expected, read.StatusCode);
        }
        async Task SaveAndRead(HttpStatusCode expected)
        {
            using var saved = await FeatureHttpTestRequests.SendAsync(ordinary, save, HttpMethod.Put, "/api/authorization/data-scope/rules/Scope_Organization", rule);
            Assert.True(saved.StatusCode == HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());
            await ReadOther(expected);
        }
    }
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实主体优先级经正式保存与人员详情消费")]
    public async Task UserOpinion_CompetesOnlyWhenMatchedAndExplicit()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.read");
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var people = http.Organization("persons");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Subjects", "主体", "subjects-reader", "主体读取者", people, entry, save);
        using var ordinary = actor.Client;
        var target = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin,
            http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
            new { PersonNo = "SUBJECT-TARGET", ENAME = "Subject target", EmploymentStatus = "Active", IsEnabled = true }));
        var rule = new DataScopeRuleDto("Scope_Subjects", "Scope_Subjects", null, "主体优先级", true, "organization.person",
            new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement("Subject target")),
            [new("Subject_All", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" }),
             new("Subject_User", "user", actor.AccountId, 10, new Dictionary<string, string> { ["read"] = "allow" })], []);
        foreach (var opinion in new[] { "allow", "unset", "deny" })
        {
            rule = rule with { Subjects = [rule.Subjects[0], rule.Subjects[1] with { Actions = new Dictionary<string, string> { ["read"] = opinion } }] };
            using var saved = await FeatureHttpTestRequests.SendAsync(ordinary, save, HttpMethod.Put, "/api/authorization/data-scope/rules/Scope_Subjects", rule);
            Assert.True(saved.StatusCode == HttpStatusCode.NoContent, await saved.Content.ReadAsStringAsync());
            using var read = await FeatureHttpTestRequests.SendAsync(ordinary, people, HttpMethod.Get, $"/api/organization/persons/{target}");
            Assert.Equal(opinion == "allow" ? HttpStatusCode.OK : HttpStatusCode.NotFound, read.StatusCode);
        }
    }
}
