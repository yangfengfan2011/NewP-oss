using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data.Common;
using System.Diagnostics;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeConditionsSqlServerTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "非nullable布尔字段的普通比较及显式空值比较保持有效SQL读取")]
    public async Task PersonRead_NonNullableBooleanSupportsComparisonAndNullOperators()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Organization("persons");
        using var ordinary = (await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "BoolScope", "布尔条件", "scope-bool", "布尔读取者", read)).Client;
        var person = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin,
            http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
            new { PersonNo = "SCOPE-BOOL", ENAME = "布尔条件目标", EmploymentStatus = "FullTime", IsEnabled = true }));
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.Equal(typeof(bool), db.Model.FindEntityType(db.ResolveModelKey("Organization/Person"))!
                .FindProperty("IsEnabled")!.ClrType);
            Assert.Equal(typeof(DateTime), db.Model.FindEntityType(db.ResolveModelKey("Organization/Person"))!
                .FindProperty("CreatedAt")!.ClrType);
        }
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var empty = JsonSerializer.SerializeToElement((object?)null);
        var rule = new DataScopeRuleDto("Scope_Bool", "Scope_Bool", null, "非空布尔条件", true, "organization.person",
            new("attribute", "IsEnabled", "equal", JsonSerializer.SerializeToElement(true)),
            [new("Subject_Bool", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        await SaveAsync(rule.Condition);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, "/api/organization/persons?page=1&pageSize=1"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await StatusAsync(HttpStatusCode.NotFound);
        await SaveAsync(new("attribute", "IsEnabled", "isNull", empty));
        await StatusAsync(HttpStatusCode.OK);
        await SaveAsync(new("attribute", "IsEnabled", "notNull", empty));
        await StatusAsync(HttpStatusCode.NotFound);
        await SaveAsync(new("attribute", "CreatedAt", "isNull", empty));
        await StatusAsync(HttpStatusCode.OK);
        await SaveAsync(new("attribute", "CreatedAt", "notNull", empty));
        await StatusAsync(HttpStatusCode.NotFound);

        async Task SaveAsync(DataScopeConditionDto condition)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put,
                "/api/authorization/data-scope/rules/Scope_Bool", rule with { Condition = condition });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        async Task StatusAsync(HttpStatusCode expected)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get,
                $"/api/organization/persons/{person}");
            Assert.Equal(expected, response.StatusCode);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "空值、集合、日期和错误配置在正式保存及单对象读取上保持约定语义")]
    public async Task PersonRead_OperatorsAndInvalidSavePreserveConfiguration()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Organization("persons");
        using var ordinary = (await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Operator", "操作符", "scope-operator", "操作符读取者", read)).Client;
        var create = http.Organization("persons", "create", "list.pageActions");
        var nullable = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin, create, "/api/organization/persons",
            new { PersonNo = "OP-NULL", ENAME = "操作符空值", EmploymentStatus = "FullTime", IsEnabled = true }));
        var older = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin, create, "/api/organization/persons",
            new { PersonNo = "OP-OLD", ENAME = "操作符旧值", BirthDate = "2000-01-01", EmploymentStatus = "FullTime", IsEnabled = true }));
        var newer = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin, create, "/api/organization/persons",
            new { PersonNo = "OP-NEW", ENAME = "操作符新值", BirthDate = "2001-01-01", EmploymentStatus = "FullTime", IsEnabled = true }));
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var entry = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.read");
        var empty = JsonSerializer.SerializeToElement((object?)null);
        DataScopeConditionDto Leaf(string field, string operation, object? value) =>
            new("attribute", field, operation, JsonSerializer.SerializeToElement(value));
        var rule = new DataScopeRuleDto("Scope_Operators", "Scope_Operators", null, "操作符", true, "organization.person",
            Leaf("BirthDate", "notEqual", "2000-01-01"),
            [new("Subject_Operators", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        await SaveAsync(rule.Condition, HttpStatusCode.NoContent);
        await StatusAsync(nullable, HttpStatusCode.OK);
        await StatusAsync(older, HttpStatusCode.OK);
        await StatusAsync(newer, HttpStatusCode.NotFound);
        await SaveAsync(Leaf("BirthDate", "notIn", Array.Empty<string>()), HttpStatusCode.NoContent);
        await StatusAsync(nullable, HttpStatusCode.OK);
        await StatusAsync(older, HttpStatusCode.NotFound);
        await StatusAsync(newer, HttpStatusCode.NotFound);
        await SaveAsync(Leaf("BirthDate", "in", Array.Empty<string>()), HttpStatusCode.NoContent);
        await StatusAsync(older, HttpStatusCode.OK);
        await SaveAsync(Leaf("BirthDate", "in", new[] { "2000-01-01" }), HttpStatusCode.NoContent);
        await StatusAsync(nullable, HttpStatusCode.OK);
        await StatusAsync(older, HttpStatusCode.NotFound);
        await StatusAsync(newer, HttpStatusCode.OK);
        await SaveAsync(new("attribute", "BirthDate", "isNull", empty), HttpStatusCode.NoContent);
        await StatusAsync(nullable, HttpStatusCode.NotFound);
        await StatusAsync(older, HttpStatusCode.OK);
        await SaveAsync(new("attribute", "BirthDate", "notNull", empty), HttpStatusCode.NoContent);
        await StatusAsync(nullable, HttpStatusCode.OK);
        await StatusAsync(older, HttpStatusCode.NotFound);
        await SaveAsync(Leaf("BirthDate", "greaterThan", "2000-01-01"), HttpStatusCode.NoContent);
        await StatusAsync(older, HttpStatusCode.OK);
        await StatusAsync(newer, HttpStatusCode.NotFound);
        await SaveAsync(Leaf("PersonNo", "contains", ""), HttpStatusCode.NoContent);
        await StatusAsync(nullable, HttpStatusCode.NotFound);
        await SaveAsync(Leaf("ENAME", "equal", ""), HttpStatusCode.NoContent);
        await StatusAsync(nullable, HttpStatusCode.OK);
        await SaveAsync(Leaf("PersonNo", "contains", ""), HttpStatusCode.NoContent);
        var numberRule = rule with { Id = "Scope_Number", RootId = "Scope_Number", ResourceKey = "identity.account",
            Condition = Leaf("Revision", "greaterThan", "9007199254740993"), Subjects = [] };
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/Scope_Number", numberRule)) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var numbers = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/authorization/data-scope/rules");
        Assert.Equal(9007199254740993L, numbers.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "Scope_Number")
            .GetProperty("condition").GetProperty("value").GetInt64());
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/Scope_Number", numberRule with { Condition = Leaf("Revision", "greaterThan", "1.5") }))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/Scope_Number", numberRule with { Condition = Leaf("Status", "equal", "Enabled") }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/Scope_Number", numberRule with { Condition = Leaf("Status", "contains", "En") }))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/Scope_Number", numberRule with { Condition =
                new("business", "", "", empty, Key: "organization.person.departmentEnabled") }))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var stored = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/authorization/data-scope/rules");
        var previous = stored[0].GetProperty("condition").GetRawText();
        foreach (var invalid in new[] {
            Leaf("Missing", "equal", "x"), Leaf("EmploymentStatus", "equal", "NotAnEmploymentStatus"),
            Leaf("IsEnabled", "contains", "true"), Leaf("BirthDate", "equal", null), Leaf("BirthDate", "greaterThan", "2000-02-30"),
            Leaf("PersonNo", "unsupported", "x"), new DataScopeConditionDto("business", "", "", empty, Key: "unknown") })
            await SaveAsync(invalid, HttpStatusCode.BadRequest);
        var nested = new DataScopeConditionDto("group", "", "and", empty, Children: [Leaf("PersonNo", "equal", "OP-NULL"),
            Leaf("EmploymentStatus", "equal", "NotAnEmploymentStatus")]);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/Scope_Operators",
            rule with { Condition = nested }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("条件 1.2", problem.GetProperty("title").GetString());
        }
        stored = await FeatureHttpTestRequests.ReadAsync(admin, entry, "/api/authorization/data-scope/rules");
        Assert.Equal(previous, stored[0].GetProperty("condition").GetRawText());

        async Task SaveAsync(DataScopeConditionDto condition, HttpStatusCode expected)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put,
                "/api/authorization/data-scope/rules/Scope_Operators", rule with { Condition = condition });
            Assert.True(response.StatusCode == expected, await response.Content.ReadAsStringAsync());
        }
        async Task StatusAsync(string personId, HttpStatusCode expected)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{personId}");
            Assert.Equal(expected, response.StatusCode);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实组织业务事实可完整判断，但缺失读取贡献不会变成恒真拒绝")]
    public async Task PersonBusinessCondition_FullJudgmentAndReadProjectionAreSeparate()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddScoped<IDataScopeIdentityProvider, UnusedReadFacts>());
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Organization("persons");
        using var ordinary = (await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Business", "业务条件", "scope-business", "业务读取者", read)).Client;
        var company = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin,
            http.Organization("companies", "create", "list.pageActions"), "/api/organization/companies",
            new { ECODE = "SCOPE-COMPANY", ENAME = "条件公司", SortOrder = "0010" }));
        var department = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin,
            http.Organization("departments", "create", "list.pageActions"), "/api/organization/departments",
            new { ECODE = "SCOPE-DEPT", ENAME = "条件部门", CompanyId = company, SortOrder = "0010" }));
        var person = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin,
            http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
            new { PersonNo = "SCOPE-BUSINESS", ENAME = "业务条件人员", CompanyId = company, DepartmentId = department,
                EmploymentStatus = "FullTime", IsEnabled = true }));
        var other = BusinessAuditSqlServerHttpApiTests.Id(await FeatureHttpTestRequests.CreateAsync(admin,
            http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
            new { PersonNo = "SCOPE-OTHER", ENAME = "其他业务人员", CompanyId = company, DepartmentId = department,
                EmploymentStatus = "FullTime", IsEnabled = true }));
        var empty = JsonSerializer.SerializeToElement((object?)null);
        var business = new DataScopeConditionDto("business", "", "", empty, Key: "organization.person.departmentEnabled");
        using (var scope = fixture.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<DataScopeCatalog>();
            var identities = scope.ServiceProvider.GetRequiredService<DataScopeIdentities>();
            var facts = await identities.ResolveAsync("unused", [], [], default);
            Assert.True(await catalog.MatchesAsync("organization.person", person, business, facts, default));
            Assert.Null(catalog.BuildReadCondition("organization.person", business,
                System.Linq.Expressions.Expression.Parameter(typeof(Dictionary<string, object?>), "row"), facts));
        }
        var rule = new DataScopeRuleDto("Scope_Business", "Scope_Business", null, "业务读取贡献", true, "organization.person", business,
            [new("Subject_Business", "unused.read.facts", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/Scope_Business", rule))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{person}"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var absentGroup = new DataScopeConditionDto("group", "", "and", empty, Children:
            [new("group", "", "or", empty, Children: [business, business])]);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/Scope_Business",
            rule with { Condition = absentGroup })) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{person}"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var mixed = new DataScopeConditionDto("group", "", "or", empty, Children:
            [new("attribute", "PersonNo", "equal", JsonSerializer.SerializeToElement("SCOPE-BUSINESS")), business]);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/Scope_Business",
            rule with { Condition = mixed, Subjects = [rule.Subjects[0] with { Type = "all" }] })) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{person}"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{other}"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var nested = new DataScopeConditionDto("group", "", "and", empty, Children:
            [new("attribute", "PersonNo", "startsWith", JsonSerializer.SerializeToElement("SCOPE-")),
                new("group", "", "or", empty, Children: [business,
                    new("attribute", "PersonNo", "equal", JsonSerializer.SerializeToElement("SCOPE-BUSINESS"))])]);
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/Scope_Business",
            rule with { Condition = nested, Subjects = [rule.Subjects[0] with { Type = "all" }] })) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{person}"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, $"/api/organization/persons/{other}"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class UnusedReadFacts : IDataScopeIdentityProvider
    {
        public IReadOnlyList<DataScopeSubjectType> SubjectTypes { get; } = [new("unused.read.facts", "未使用读取事实", false)];
        public IReadOnlyList<DataScopeContextValue> ContextValues => [];
        public Task<IDataScopeIdentityFacts> ResolveAsync(string accountId, CancellationToken token) =>
            throw new InvalidOperationException("UNUSED_READ_FACTS_CALLED");
        public Task<DataScopeSubjectPage> SearchAsync(string type, string? query, int page,
            IReadOnlyList<string> currentIds, CancellationToken token) => Task.FromResult(new DataScopeSubjectPage([], false, []));
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "组合条件在真实 SQL Server 的计数、分页和详情使用同一读取投影")]
    public async Task PersonRead_CompoundConditionFiltersBeforePaging()
    {
        var commands = new PersonCommands();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(commands)));
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Organization("persons");
        using var ordinary = (await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Condition", "组合条件", "scope-condition", "组合读取者", read)).Client;
        var create = http.Organization("persons", "create", "list.pageActions");
        var alpha = await FeatureHttpTestRequests.CreateAsync(admin, create, "/api/organization/persons",
            new { PersonNo = "COND-ALPHA", ENAME = "Scope Alpha", EmploymentStatus = "Active", IsEnabled = true, BirthDate = "2000-01-01" });
        var beta = await FeatureHttpTestRequests.CreateAsync(admin, create, "/api/organization/persons",
            new { PersonNo = "COND-BETA", ENAME = "Scope Beta", EmploymentStatus = "Active", IsEnabled = true });
        var excluded = await FeatureHttpTestRequests.CreateAsync(admin, create, "/api/organization/persons",
            new { PersonNo = "COND-EXCLUDED", ENAME = "Scope Alpha", EmploymentStatus = "Active", IsEnabled = true });
        using (var scope = fixture.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<DataScopeCatalog>();
            var facts = await scope.ServiceProvider.GetRequiredService<DataScopeIdentities>().ResolveAsync("unused", [], [], default);
            var text = new DataScopeConditionDto("attribute", "ENAME", "contains", JsonSerializer.SerializeToElement("alpha"));
            Assert.True(await catalog.MatchesAsync("organization.person", BusinessAuditSqlServerHttpApiTests.Id(alpha), text, facts, default));
            Assert.True(await catalog.MatchesAsync("organization.person", BusinessAuditSqlServerHttpApiTests.Id(excluded), text, facts, default));
            Assert.False(await catalog.MatchesAsync("organization.person", BusinessAuditSqlServerHttpApiTests.Id(beta), text, facts, default));
        }
        var before = await FeatureHttpTestRequests.ReadAsync(ordinary, read, "/api/organization/persons?page=1&pageSize=1");
        var empty = JsonSerializer.SerializeToElement((object?)null);
        var condition = new DataScopeConditionDto("group", "", "and", empty, Children:
        [
            new("group", "", "or", empty, Children:
            [
                new("attribute", "ENAME", "contains", JsonSerializer.SerializeToElement("alpha")),
                new("attribute", "BirthDate", "isNull", empty)
            ]),
            new("attribute", "PersonNo", "notEqual", JsonSerializer.SerializeToElement("COND-EXCLUDED"))
        ]);
        var rule = new DataScopeRuleDto("Scope_Compound", "Scope_Compound", null, "组合读取", true, "organization.person", condition,
            [new("Subject_Compound", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/Scope_Compound", rule))
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        commands.Enabled = true;
        var watch = Stopwatch.StartNew();
        var after = await FeatureHttpTestRequests.ReadAsync(ordinary, read, "/api/organization/persons?page=1&pageSize=1");
        watch.Stop();
        commands.Enabled = false;
        var sql = commands.Sql.Where(command => command.Contains("[Person]", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, sql.Length);
        Assert.All(sql, command => {
            Assert.Contains("WHERE", command);
            Assert.Contains("[ENAME]", command);
            Assert.Contains("[BirthDate]", command);
            Assert.Contains("[PersonNo]", command);
        });
        Assert.Single(sql, command => command.StartsWith("SELECT COUNT(", StringComparison.OrdinalIgnoreCase));
        Assert.Single(sql, command => command.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        output.WriteLine(JsonSerializer.Serialize(new { sqlServerPersonQueries = sql.Length, elapsedMilliseconds = watch.Elapsed.TotalMilliseconds,
            countSql = sql.Single(command => command.StartsWith("SELECT COUNT(", StringComparison.OrdinalIgnoreCase)),
            pageSql = sql.Single(command => command.Contains("OFFSET", StringComparison.OrdinalIgnoreCase)) }));
        Assert.Equal(before.GetProperty("total").GetInt32() - 3, after.GetProperty("total").GetInt32());
        Assert.Single(after.GetProperty("items").EnumerateArray());
        foreach (var person in new[] { alpha, beta })
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get,
                $"/api/organization/persons/{BusinessAuditSqlServerHttpApiTests.Id(person)}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get,
            $"/api/organization/persons/{BusinessAuditSqlServerHttpApiTests.Id(excluded)}"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class PersonCommands : DbCommandInterceptor
    {
        internal bool Enabled { get; set; }
        internal List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled) Sql.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
