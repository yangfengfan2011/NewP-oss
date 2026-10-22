using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopePersonMutationSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "编辑动作关闭仍传播启用的读取依赖故障")]
    public async Task PersonMutation_DisabledEditDoesNotHideReadDecisionFailure()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(
            services => services.AddScoped<IDataScopeIdentityProvider, ThrowingMutationFacts>(),
            new Dictionary<string, bool> { ["edit"] = false });
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Organization("persons");
        var edit = http.Organization("persons", "edit", "detail.pageActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PersonReadFault", "读取故障",
            "person-read-fault", "读取故障用户", read, edit);
        using var ordinary = actor.Client;
        var person = await http.CreateButtonAsync(admin,
            http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
            new { PersonNo = "READ-FAULT", ENAME = "读取故障人员", EmploymentStatus = "Active", IsEnabled = true });
        var id = BusinessAuditSqlServerHttpApiTests.Id(person);
        var rule = new DataScopeRuleDto("PersonReadFaultRule", "PersonReadFaultRule", null, "读取故障", true,
            "organization.person", new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
            [new("PersonReadFaultSubject", "test.mutation.failure", null, 0,
                new Dictionary<string, string> { ["read"] = "deny" })], []);
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        using (var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/PersonReadFaultRule", rule))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, edit, HttpMethod.Put,
            $"/api/organization/persons/{id}", new { ENAME = "不得误报成功" }, dataAccess: http.Declaration("organization.person.update", edit)))
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "写入判权事实源故障保持异常且不得创建人员")]
    public async Task PersonMutation_DecisionFailureIsNeitherDenialNorCommit()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddScoped<IDataScopeIdentityProvider, ThrowingMutationFacts>());
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Organization("persons");
        var create = http.Organization("persons", "create", "list.pageActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PersonFault", "人员故障",
            "person-fault", "人员故障用户", read, create);
        using var ordinary = actor.Client;
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var rule = new DataScopeRuleDto("PersonFaultRule", "PersonFaultRule", null, "故障主体", true,
            "organization.person", new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement("故障人员")),
            [new("PersonFaultSubject", "test.mutation.failure", null, 0,
                new Dictionary<string, string> { ["create"] = "deny" })], []);
        using (var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/PersonFaultRule", rule))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var payload = new { PersonNo = "WRITE-FAULT", ENAME = "故障人员", EmploymentStatus = "Active", IsEnabled = true };
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post,
            "/api/organization/persons", payload, dataAccess: http.Declaration("organization.person.create", create)))
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var created = await new FeatureHttpTestRequests(fixture).CreateButtonAsync(admin, create, "/api/organization/persons", payload);
        Assert.NotNull(created);
    }

    private sealed class ThrowingMutationFacts : IDataScopeIdentityProvider
    {
        public IReadOnlyList<DataScopeSubjectType> SubjectTypes { get; } =
            [new("test.mutation.failure", "故障主体", false)];
        public IReadOnlyList<DataScopeContextValue> ContextValues => [];
        public Task<IDataScopeIdentityFacts> ResolveAsync(string accountId, CancellationToken token) =>
            throw new InvalidOperationException("DATA_SCOPE_TEST_DECISION_FAILURE");
        public Task<DataScopeSubjectPage> SearchAsync(string type, string? query, int page,
            IReadOnlyList<string> currentIds, CancellationToken token) =>
            Task.FromResult(new DataScopeSubjectPage([], false, []));
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "已获准的在途人员更新可提交，后续请求消费新授权")]
    public async Task PersonMutation_AuthorizationChangeDoesNotReplayOrCancelInFlightWrite()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Organization("persons");
        var edit = http.Organization("persons", "edit", "detail.pageActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PersonInFlight", "在途人员",
            "person-in-flight", "在途人员用户", read, edit);
        using var ordinary = actor.Client;
        var person = await http.CreateButtonAsync(admin,
            http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
            new { PersonNo = "WRITE-IN-FLIGHT", ENAME = "判权前", EmploymentStatus = "Active", IsEnabled = true });
        var id = BusinessAuditSqlServerHttpApiTests.Id(person);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Fault.AfterRegister = async (change, token) =>
        {
            if (change.EventType == "person.updated" && change.Fields.Any(field => field.After.Value == "判权后"))
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };
        var first = FeatureHttpTestRequests.SendAsync(ordinary, edit, HttpMethod.Put,
            $"/api/organization/persons/{id}", new { ENAME = "判权后" }, dataAccess: http.Declaration("organization.person.update", edit));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
            var rule = new DataScopeRuleDto("PersonInFlightRule", "PersonInFlightRule", null, "在途撤权", true,
                "organization.person", new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
                [new("PersonInFlightSubject", "all", null, 0,
                    new Dictionary<string, string> { ["edit"] = "deny" })], []);
            using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Put,
                "/api/authorization/data-scope/rules/PersonInFlightRule", rule);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        finally { release.TrySetResult(); }
        using (var response = await first)
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, edit, HttpMethod.Put,
            $"/api/organization/persons/{id}", new { ENAME = "后来请求" }, dataAccess: http.Declaration("organization.person.update", edit)))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get,
            $"/api/organization/persons/{id}", dataAccess: http.Declaration("organization.person.read", read)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("判权后", await response.Content.ReadAsStringAsync());
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "人员动作开关和读取依赖在正式重装配后独立生效")]
    public async Task PersonMutation_ActionSwitchPreservesIndependentReadDependency()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Organization("persons");
        var create = http.Organization("persons", "create", "list.pageActions");
        var edit = http.Organization("persons", "edit", "detail.pageActions");
        var delete = http.Organization("persons", "delete", "list.rowActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PersonSwitchScope", "人员开关范围",
            "person-switch-scope", "人员开关范围用户", read, create, edit, delete);
        actor.Client.Dispose();
        var person = await new FeatureHttpTestRequests(fixture).CreateButtonAsync(admin, create, "/api/organization/persons",
            new { PersonNo = "WRITE-SWITCH", ENAME = "开关目标", EmploymentStatus = "Active", IsEnabled = true });
        var id = BusinessAuditSqlServerHttpApiTests.Id(person);
        using var accountResponse = await http.SendButtonAsync(admin, http.Identity("create"),
            HttpMethod.Post, "/api/identity/accounts", new { ECODE = "person-switch-bound", ENAME = "绑定守卫账号" });
        Assert.Equal(HttpStatusCode.Created, accountResponse.StatusCode);
        var account = (await accountResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("account");
        var bound = await new FeatureHttpTestRequests(fixture).CreateButtonAsync(admin, create, "/api/organization/persons",
            new { PersonNo = "WRITE-SWITCH-BOUND", ENAME = "绑定守卫人员", EmploymentStatus = "Active", IsEnabled = true });
        var boundId = BusinessAuditSqlServerHttpApiTests.Id(bound);
        using (var response = await http.SendButtonAsync(admin,
            http.Organization("persons", "linkUser", "detail.custom"), HttpMethod.Put,
            $"/api/organization/persons/{boundId}/user-binding", new
            {
                accountEid = account.GetProperty("eid").GetString(),
                expectedRevision = account.GetProperty("revision").GetInt64(),
                reason = "验证删除守卫"
            }))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var rule = new DataScopeRuleDto("PersonSwitchRule", "PersonSwitchRule", null, "开关规则", true,
            "organization.person", new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
            [new("PersonSwitchSubject", "all", null, 0, new Dictionary<string, string>
                { ["read"] = "deny", ["edit"] = "deny", ["delete"] = "deny" })], []);
        using (var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/PersonSwitchRule", rule))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await fixture.RestartWithActionAsync("edit", false);
        using (var ordinary = await LoginCurrentAsync())
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, edit, HttpMethod.Put,
            $"/api/organization/persons/{id}", new { ENAME = "仍不可编辑" }, dataAccess: http.Declaration("organization.person.update", edit)))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await fixture.RestartWithActionsAsync(new Dictionary<string, bool> { ["read"] = false, ["edit"] = false });
        using (var ordinary = await LoginCurrentAsync())
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, edit, HttpMethod.Put,
            $"/api/organization/persons/{id}", new { ENAME = "读取关闭可编辑" }, dataAccess: http.Declaration("organization.person.update", edit)))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await fixture.RestartWithActionAsync("read", false);
        using (var ordinary = await LoginCurrentAsync())
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, edit, HttpMethod.Put,
            $"/api/organization/persons/{id}", new { ENAME = "编辑重新启用" }, dataAccess: http.Declaration("organization.person.update", edit)))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // 新根遵循正式同级名称唯一策略，保留前一根的读取与编辑规则。
        var createRule = rule with { Id = "PersonCreateSwitchRule", RootId = "PersonCreateSwitchRule", Name = "创建开关规则",
            Condition = new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement("开关创建")),
            Subjects = [rule.Subjects[0] with { Id = "PersonCreateSwitchSubject",
                Actions = new Dictionary<string, string> { ["create"] = "deny" } }] };
        using (var administrator = await LoginCurrentAsync("admin"))
        using (var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(administrator, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/PersonCreateSwitchRule", createRule))
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var createPayload = new { PersonNo = "WRITE-CREATE-SWITCH", ENAME = "开关创建", EmploymentStatus = "Active", IsEnabled = true };
        using (var ordinary = await LoginCurrentAsync())
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post,
            "/api/organization/persons", createPayload, dataAccess: http.Declaration("organization.person.create", create)))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await fixture.RestartWithActionAsync("create", false);
        using (var ordinary = await LoginCurrentAsync())
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post,
            "/api/organization/persons", createPayload, dataAccess: http.Declaration("organization.person.create", create)))
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await fixture.RestartWithActionAsync("delete", false);
        using (var ordinary = await LoginCurrentAsync())
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, delete, HttpMethod.Delete,
            $"/api/organization/persons/{id}", dataAccess: http.Declaration("organization.person.delete", delete)))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await fixture.RestartWithActionAsync("read", false);
        using (var ordinary = await LoginCurrentAsync())
        using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, delete, HttpMethod.Delete,
            $"/api/organization/persons/{id}", dataAccess: http.Declaration("organization.person.delete", delete)))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await fixture.RestartWithActionsAsync(new Dictionary<string, bool> { ["read"] = false, ["delete"] = false });
        using (var ordinary = await LoginCurrentAsync())
        {
            using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, delete, HttpMethod.Delete,
                $"/api/organization/persons/{boundId}", dataAccess: http.Declaration("organization.person.delete", delete)))
            {
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                var error = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("Organization.Person.DeleteBlockedByUserBinding", error.GetProperty("code").GetString());
            }
            using (var response = await FeatureHttpTestRequests.SendAsync(ordinary, delete, HttpMethod.Delete,
                $"/api/organization/persons/{id}", dataAccess: http.Declaration("organization.person.delete", delete)))
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        async Task<HttpClient> LoginCurrentAsync(string code = "person-switch-scope")
        {
            var client = fixture.CreateClient();
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            using var response = await client.PostAsJsonAsync("/api/auth/login",
                new { ECODE = code, Password = "Audit-test-secure-938!" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            return client;
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "人员普通写入按候选和动作前事实裁决，写后不可读仍为成功")]
    public async Task PersonMutation_CandidateAndPreEditScopeControlTheWrite()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Organization("persons");
        var create = http.Organization("persons", "create", "list.pageActions");
        var edit = http.Organization("persons", "edit", "detail.pageActions");
        var delete = http.Organization("persons", "delete", "list.rowActions");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PersonWriteScope", "人员写入范围",
            "person-write-scope", "人员写入范围用户", read, create, edit, delete);
        using var ordinary = actor.Client;
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        var rule = new DataScopeRuleDto("PersonWriteScopeRule", "PersonWriteScopeRule", null, "写入候选",
            true, "organization.person", new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement("禁止创建")),
            [new("PersonWriteScopeSubject", "all", null, 0, new Dictionary<string, string> { ["create"] = "deny" })], []);
        using (var response = await http.SendButtonAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/PersonWriteScopeRule", rule))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var payload = new { PersonNo = "WRITE-SCOPE-DENIED", ENAME = "禁止创建", EmploymentStatus = "Active", IsEnabled = true };
        using (var response = await SendAsync(ordinary, create, HttpMethod.Post,
            "/api/organization/persons", payload))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var visible = await CreateAsync(ordinary, create, "/api/organization/persons",
            new { PersonNo = "WRITE-SCOPE-VISIBLE", ENAME = "可见人员", EmploymentStatus = "Active", IsEnabled = true });
        var id = BusinessAuditSqlServerHttpApiTests.Id(visible);
        var hidden = rule with { Condition = new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement("移出范围")),
            Subjects = [rule.Subjects[0] with { Actions = new Dictionary<string, string> { ["read"] = "deny" } }] };
        using (var response = await http.SendButtonAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/PersonWriteScopeRule", hidden))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await SendAsync(ordinary, create, HttpMethod.Post,
            "/api/organization/persons", new { PersonNo = "WRITE-SCOPE-HIDDEN", ENAME = "移出范围", EmploymentStatus = "Active", IsEnabled = true }))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.Equal("{\"succeeded\":true}", await response.Content.ReadAsStringAsync());
        }
        string hiddenId;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            hiddenId = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Person"))
                .AsNoTracking().Where(row => EF.Property<string>(row, "PersonNo") == "WRITE-SCOPE-HIDDEN")
                .Select(row => EF.Property<string>(row, "EID")).SingleAsync();
            var grants = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("DataScopeObjectSubject"))
                .AsNoTracking().CountAsync(row => EF.Property<string>(row, "ResourceKey") == "organization.person"
                    && EF.Property<string>(row, "ObjectId") == hiddenId);
            Assert.Equal(0, grants);
        }
        using (var response = await SendAsync(ordinary, read, HttpMethod.Get,
            $"/api/organization/persons/{hiddenId}"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await SendAsync(ordinary, edit, HttpMethod.Put,
            $"/api/organization/persons/{id}", new { ENAME = "移出范围" }))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("移出范围", await response.Content.ReadAsStringAsync());
        }
        using (var response = await SendAsync(ordinary, read, HttpMethod.Get,
            $"/api/organization/persons/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await SendAsync(ordinary, edit, HttpMethod.Put,
            $"/api/organization/persons/{id}", new { ENAME = "再编辑" }))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await SendAsync(ordinary, delete, HttpMethod.Delete,
            $"/api/organization/persons/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var deletable = await CreateAsync(ordinary, create, "/api/organization/persons",
            new { PersonNo = "WRITE-SCOPE-DELETE", ENAME = "删除候选", EmploymentStatus = "Active", IsEnabled = true });
        var deleteId = BusinessAuditSqlServerHttpApiTests.Id(deletable);
        var deleteDenied = rule with { Condition = new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement("删除候选")),
            Subjects = [rule.Subjects[0] with { Actions = new Dictionary<string, string> { ["delete"] = "deny" } }] };
        using (var response = await http.SendButtonAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/PersonWriteScopeRule", deleteDenied))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await SendAsync(ordinary, delete, HttpMethod.Delete,
            $"/api/organization/persons/{deleteId}"))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await http.SendButtonAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/PersonWriteScopeRule", deleteDenied with { Subjects = [] }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await SendAsync(ordinary, delete, HttpMethod.Delete,
            $"/api/organization/persons/{deleteId}"))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var company = await CreateAsync(admin,
            http.Organization("companies", "create", "list.pageActions"), "/api/organization/companies",
            new { ECODE = "WRITE-SCOPE", ENAME = "写入范围公司", SortOrder = "0100" });
        var companyId = BusinessAuditSqlServerHttpApiTests.Id(company);
        var department = await CreateAsync(admin,
            http.Organization("departments", "create", "list.pageActions"), "/api/organization/departments",
            new { ECODE = "WRITE-SCOPE-DEP", ENAME = "写入范围部门", CompanyId = companyId, SortOrder = "0100" });
        var businessPerson = await CreateAsync(admin, create, "/api/organization/persons",
            new { PersonNo = "WRITE-SCOPE-BUSINESS", ENAME = "业务条件人员", CompanyId = companyId,
                DepartmentId = BusinessAuditSqlServerHttpApiTests.Id(department), EmploymentStatus = "Active", IsEnabled = true });
        var businessId = BusinessAuditSqlServerHttpApiTests.Id(businessPerson);
        var businessRule = rule with { Condition = new("business", "", "", JsonSerializer.SerializeToElement((object?)null),
                Key: "organization.person.departmentEnabled"),
            Subjects = [rule.Subjects[0] with { Actions = new Dictionary<string, string> { ["edit"] = "deny" } }] };
        using (var response = await http.SendButtonAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/PersonWriteScopeRule", businessRule))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await SendAsync(ordinary, read, HttpMethod.Get,
            $"/api/organization/persons/{businessId}"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await SendAsync(ordinary, edit, HttpMethod.Put,
            $"/api/organization/persons/{businessId}", new { ENAME = "不得修改" }))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await http.SendButtonAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/PersonWriteScopeRule", businessRule with
            { Subjects = [rule.Subjects[0] with { Actions = new Dictionary<string, string> { ["create"] = "deny" } }] }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await SendAsync(ordinary, create, HttpMethod.Post,
            "/api/organization/persons", new { PersonNo = "WRITE-SCOPE-VALID-BUSINESS", ENAME = "业务条件候选",
                CompanyId = companyId, DepartmentId = BusinessAuditSqlServerHttpApiTests.Id(department),
                EmploymentStatus = "Active", IsEnabled = true }))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var auditRule = rule with { Condition = new("attribute", "CreatedBy", "equal",
            JsonSerializer.SerializeToElement(actor.AccountId)),
            Subjects = [rule.Subjects[0] with { Actions = new Dictionary<string, string> { ["create"] = "deny" } }] };
        using (var response = await http.SendButtonAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/PersonWriteScopeRule", auditRule))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await SendAsync(ordinary, create, HttpMethod.Post,
            "/api/organization/persons", new { PersonNo = "WRITE-SCOPE-AUDIT", ENAME = "服务端审计字段候选",
                EmploymentStatus = "Active", IsEnabled = true }))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await http.SendButtonAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/PersonWriteScopeRule", rule with
            { Condition = new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement("管理员仍可维护")) }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await SendAsync(admin, create, HttpMethod.Post,
            "/api/organization/persons", new { PersonNo = "WRITE-SCOPE-ADMIN", ENAME = "管理员仍可维护",
                EmploymentStatus = "Active", IsEnabled = true }))
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using (var response = await SendAsync(admin, create, HttpMethod.Post,
            "/api/organization/persons", new { PersonNo = "WRITE-SCOPE-INVALID", ENAME = "业务约束仍生效",
                CompanyId = companyId, EmploymentStatus = "Active", IsEnabled = true }))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await http.SendButtonAsync(admin, save, HttpMethod.Put,
            "/api/authorization/data-scope/rules/PersonWriteScopeRule", rule with
            { Condition = new("attribute", "ENAME", "equal", JsonSerializer.SerializeToElement("扩展候选")) }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var extension = fixture.Services.GetRequiredService<ObjectOperationExtensionProbeControl>();
        extension.Mode = ObjectOperationExtensionProbeMode.Supplement;
        extension.SupplementedName = "扩展候选";
        try
        {
            using var response = await SendAsync(ordinary, create, HttpMethod.Post,
                "/api/organization/persons", new { PersonNo = "WRITE-SCOPE-EXTENSION", EmploymentStatus = "Active", IsEnabled = true });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally { extension.Mode = ObjectOperationExtensionProbeMode.None; }

        // 这个既有业务语义资产消费实际正式绑定；缺失和串用反例仍在独立测试中直接发原始请求。
        ActionDataAccessRequirement? BindingFor(FeaturePoint point)
        {
            var execution = point.Position == create.Position ? "organization.person.create"
                : point.Position == edit.Position ? "organization.person.update"
                : point.Position == delete.Position ? "organization.person.delete"
                : point.Position == read.Position ? "organization.person.read"
                : point.Position == http.Organization("companies", "create", "list.pageActions").Position ? "organization.company.create"
                : point.Position == http.Organization("departments", "create", "list.pageActions").Position ? "organization.department.create"
                : point.Position == save.Position ? "authorization.dataScope.save" : throw new InvalidOperationException("测试未登记该执行来源。");
            return execution is null ? null : http.Declaration(execution, point);
        }
        Task<HttpResponseMessage> SendAsync(HttpClient client, FeaturePoint point, HttpMethod method, string path, object? body = null) =>
            FeatureHttpTestRequests.SendAsync(client, point, method, path, body, BindingFor(point));
        Task<JsonElement> CreateAsync(HttpClient client, FeaturePoint point, string path, object body) =>
            FeatureHttpTestRequests.CreateAsync(client, point, path, body, BindingFor(point));
    }
}
