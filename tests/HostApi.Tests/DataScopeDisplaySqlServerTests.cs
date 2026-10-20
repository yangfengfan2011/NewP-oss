using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeDisplaySqlServerTests(ITestOutputHelper output)
{
    // 此资产的调用点明确指定list/read；资源取已核对的真实读行为，绝不按URL或缺请求头猜测。
    private static Task<JsonElement> DeclaredReadAsync(BusinessAuditHostFixture fixture, HttpClient client,
        ICP.Platform.Kernel.ModulePlanning.FeaturePoint point, string operation, string path)
    {
        Assert.EndsWith(".read", point.BehaviorKey);
        return OwnedResourceReadHttpRequests.ReadAsync(fixture, client, point, point.BehaviorKey[..^5], operation, path);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "正式 GET 不把目标读取故障当作名称或摘要不可用")]
    public async Task OrganizationDisplay_TargetAuthorizationFailurePropagatesFromHttp()
    {
        var control = new DisplayReadFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.AddSingleton(control);
            services.AddScoped<IDataScopeReadableIds>(provider => new FaultingReadableIds(
                (IDataScopeReadableIds)provider.GetRequiredService<IDataScopeReadPlanProvider>(),
                provider.GetRequiredService<DisplayReadFault>()));
        });
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "DisplayFault237", "显示故障",
            "display-fault-237", "显示故障用户", http.Organization("persons"));
        using var ordinary = actor.Client;
        var company = BusinessAuditSqlServerHttpApiTests.Id(await http.CreateButtonAsync(admin,
            http.Organization("companies", "create", "list.pageActions"), "/api/organization/companies",
            new { ECODE = "DISPLAY-237-FAULT", ENAME = "故障公司", SortOrder = "0100" }));
        var department = BusinessAuditSqlServerHttpApiTests.Id(await http.CreateButtonAsync(admin,
            http.Organization("departments", "create", "list.pageActions"), "/api/organization/departments",
            new { CompanyId = company, ECODE = "DISPLAY-237-FAULT", ENAME = "故障部门", SortOrder = "0100" }));
        var person = BusinessAuditSqlServerHttpApiTests.Id(await http.CreateButtonAsync(admin,
            http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
            new { PersonNo = "DISPLAY-237-FAULT", ENAME = "故障人员", CompanyId = company, DepartmentId = department,
                EmploymentStatus = "Active", IsEnabled = true }));

        control.ResourceKey = "organization.company";
        using (var failed = await FeatureHttpTestRequests.SendAsync(ordinary, http.Organization("persons"),
            HttpMethod.Get, $"/api/organization/persons/{person}", dataAccess: OwnedResourceReadHttpRequests.Declaration(fixture, http.Organization("persons"), "organization.person", "read")))
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        control.ResourceKey = "identity.account";
        using (var failed = await FeatureHttpTestRequests.SendAsync(ordinary, http.Organization("persons"),
            HttpMethod.Get, $"/api/organization/persons/{person}", dataAccess: OwnedResourceReadHttpRequests.Declaration(fixture, http.Organization("persons"), "organization.person", "read")))
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        control.ResourceKey = null;
        var recovered = await DeclaredReadAsync(fixture, ordinary, http.Organization("persons"), "read",
            $"/api/organization/persons/{person}");
        Assert.Equal("故障公司", recovered.GetProperty("fields").GetProperty("CompanyId_DisplayValue").GetString());
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "四资源正式列表详情的引用、审计与账号摘要遵守最终读取")]
    public async Task OrganizationDisplay_UsesFinalTargetReadWithoutLeakingRelatedDetails()
    {
        var observer = new DisplaySqlObserver();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(observer)));
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var readCompany = http.Organization("companies");
        var readDepartment = http.Organization("departments");
        var readPosition = http.Organization("positions");
        var readPerson = http.Organization("persons");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Display237", "引用显示",
            "display-237", "引用显示用户", readCompany, readDepartment, readPosition, readPerson);
        using var ordinary = actor.Client;

        var hiddenCompany = await CreateAsync("companies", new { ECODE = "DISPLAY-237-H", ENAME = "私有公司", SortOrder = "0100" });
        var visibleCompany = await CreateAsync("companies", new { ECODE = "DISPLAY-237-V", ENAME = "公开公司", SortOrder = "0200" });
        var hiddenParent = await CreateAsync("departments", new
        { CompanyId = hiddenCompany, ECODE = "DISPLAY-237-P", ENAME = "私有父部门", SortOrder = "0100" });
        var child = await CreateAsync("departments", new
        { CompanyId = hiddenCompany, ParentDepartmentId = hiddenParent, ECODE = "DISPLAY-237-C",
            ENAME = "可读子部门", SortOrder = "0100" });
        var hiddenPosition = await CreateAsync("positions", new { ENAME = "私有岗位" });
        var visiblePosition = await CreateAsync("positions", new { ENAME = "公开岗位" });
        var person = await CreateAsync("persons", new
        { PersonNo = "DISPLAY-237", ENAME = "引用人员", CompanyId = hiddenCompany,
            DepartmentId = child, PositionId = hiddenPosition, EmploymentStatus = "Active", IsEnabled = true });
        var account = (await http.CreateButtonAsync(admin, http.Identity("create"), "/api/identity/accounts",
            new { ECODE = "display-237-bound", ENAME = "私有绑定账号" })).GetProperty("account");
        var accountId = account.GetProperty("eid").GetString()!;
        using (var binding = await http.SendButtonAsync(admin,
            http.Organization("persons", "linkUser", "detail.custom"), HttpMethod.Put,
            $"/api/organization/persons/{person}/user-binding",
            new { accountEid = accountId, expectedRevision = account.GetProperty("revision").GetInt64(), reason = "显示验证" }))
            Assert.Equal(HttpStatusCode.OK, binding.StatusCode);

        var unmasked = await ReadFieldsAsync(readPerson, "persons", person);
        Assert.Equal("私有公司", unmasked.GetProperty("CompanyId_DisplayValue").GetString());
        Assert.Equal("可读子部门", unmasked.GetProperty("DepartmentName").GetString());
        Assert.Equal("私有岗位", unmasked.GetProperty("PositionName").GetString());
        var auditActor = unmasked.GetProperty("CreatedBy").GetString()!;

        await DenyAsync("DisplayCompany237", "organization.company", hiddenCompany);
        await DenyAsync("DisplayParent237", "organization.department", hiddenParent);
        await DenyAsync("DisplayPosition237", "organization.position", hiddenPosition);
        await DenyAsync("DisplayAccount237", "identity.account", accountId);
        await DenyAsync("DisplayAudit237", "identity.account", auditActor);

        var fields = await ReadFieldsAsync(readPerson, "persons", person);
        Assert.Equal(hiddenCompany, fields.GetProperty("CompanyId").GetString());
        Assert.Equal(child, fields.GetProperty("DepartmentId").GetString());
        Assert.Equal(hiddenPosition, fields.GetProperty("PositionId").GetString());
        Assert.Equal("***", fields.GetProperty("CompanyId_DisplayValue").GetString());
        Assert.Equal("可读子部门", fields.GetProperty("DepartmentName").GetString());
        Assert.Equal("***", fields.GetProperty("PositionName").GetString());
        Assert.Equal(auditActor, fields.GetProperty("CreatedBy").GetString());
        Assert.Equal("***", fields.GetProperty("CreatedBy_DisplayValue").GetString());
        var personDetail = await DeclaredReadAsync(fixture, ordinary, readPerson, "read", $"/api/organization/persons/{person}");
        var summary = personDetail.GetProperty("summaries").GetProperty("userBinding").GetProperty("data");
        Assert.Equal(accountId, summary.GetProperty("accountEid").GetString());
        Assert.Equal("显示验证", summary.GetProperty("reason").GetString());
        Assert.False(summary.GetProperty("isAccountReadable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, summary.GetProperty("account").ValueKind);
        Assert.DoesNotContain("私有绑定账号", personDetail.ToString());
        Assert.DoesNotContain("display-237-bound", personDetail.ToString());
        Assert.DoesNotContain("私有公司", personDetail.ToString());
        Assert.DoesNotContain("私有岗位", personDetail.ToString());

        var department = await ReadFieldsAsync(readDepartment, "departments", child);
        Assert.Equal(hiddenCompany, department.GetProperty("CompanyId").GetString());
        Assert.Equal(hiddenParent, department.GetProperty("ParentDepartmentId").GetString());
        Assert.Equal("***", department.GetProperty("CompanyId_DisplayValue").GetString());
        Assert.Equal("***", department.GetProperty("ParentDepartmentId_DisplayValue").GetString());
        Assert.Equal("***", department.GetProperty("CreatedBy_DisplayValue").GetString());
        var departmentDetail = await DeclaredReadAsync(fixture, ordinary, readDepartment, "read",
            $"/api/organization/departments/{child}");
        Assert.DoesNotContain("私有公司", departmentDetail.ToString());
        Assert.DoesNotContain("私有父部门", departmentDetail.ToString());
        for (var index = 0; index < 16; index++)
            await CreateAsync("persons", new { PersonNo = $"DISPLAY-237-B-{index:00}", ENAME = "批量引用人员",
                CompanyId = hiddenCompany, DepartmentId = child, PositionId = hiddenPosition,
                EmploymentStatus = "Active", IsEnabled = true });
        observer.Enabled = true;
        int singleRowRoundTrips;
        JsonElement representative;
        try
        {
            var single = await DeclaredReadAsync(fixture, ordinary, readPerson, "list",
                "/api/organization/persons?pageSize=1");
            Assert.Single(single.GetProperty("items").EnumerateArray());
            singleRowRoundTrips = observer.Commands.Count;
            observer.Commands.Clear();
            representative = await DeclaredReadAsync(fixture, ordinary, readPerson, "list",
                "/api/organization/persons?pageSize=100");
        }
        finally { observer.Enabled = false; }
        Assert.InRange(representative.GetProperty("items").GetArrayLength(), 17, 100);
        Assert.Equal(singleRowRoundTrips, observer.Commands.Count);
        foreach (var target in new[] { "Company", "Department", "Position" })
        {
            var sql = Assert.Single(observer.Commands, command => command.Contains("FROM [dbo].[" + target + "]"));
            Assert.Contains("[EID]", sql);
            Assert.DoesNotContain("[ENAME]", sql);
        }
        output.WriteLine($"Display batch: rows=1/{representative.GetProperty("items").GetArrayLength()}, " +
            $"targetQueries=3, roundTrips={singleRowRoundTrips}/{observer.Commands.Count}");
        foreach (var (tab, point, id) in new[]
        {
            ("persons", readPerson, person), ("departments", readDepartment, child),
            ("companies", readCompany, visibleCompany), ("positions", readPosition, visiblePosition)
        })
        {
            var detail = await ReadFieldsAsync(point, tab, id);
            Assert.Equal("***", detail.GetProperty("CreatedBy_DisplayValue").GetString());
            var page = await DeclaredReadAsync(fixture, ordinary, point, "list", $"/api/organization/{tab}?pageSize=100");
            Assert.Contains(page.GetProperty("items").EnumerateArray(), row => row.GetProperty("EID").GetString() == id);
            Assert.DoesNotContain(page.GetProperty("items").EnumerateArray(), row =>
                row.GetProperty("EID").GetString() == (tab == "companies" ? hiddenCompany : tab == "positions" ? hiddenPosition : hiddenParent));
        }

        await AllowObjectAsync("organization.company", hiddenCompany);
        await AllowObjectAsync("organization.department", hiddenParent);
        fields = await ReadFieldsAsync(readPerson, "persons", person);
        Assert.Equal("私有公司", fields.GetProperty("CompanyId_DisplayValue").GetString());
        department = await ReadFieldsAsync(readDepartment, "departments", child);
        Assert.Equal("私有父部门", department.GetProperty("ParentDepartmentId_DisplayValue").GetString());
        Assert.Equal("***", fields.GetProperty("PositionName").GetString());
        var adminVisible = (await DeclaredReadAsync(fixture, admin, readPerson, "read",
            $"/api/organization/persons/{person}")).GetProperty("fields");
        Assert.Equal("私有岗位", adminVisible.GetProperty("PositionName").GetString());

        await fixture.RestartWithActionAsync("read", false);
        using var readDisabled = fixture.CreateClient();
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(readDisabled);
        using (var login = await readDisabled.PostAsJsonAsync("/api/auth/login",
            new { ECODE = "display-237", Password = "Audit-test-secure-938!" }))
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var restored = (await DeclaredReadAsync(fixture, readDisabled, readPerson, "read",
            $"/api/organization/persons/{person}")).GetProperty("fields");
        Assert.Equal("私有岗位", restored.GetProperty("PositionName").GetString());
        Assert.NotEqual("***", restored.GetProperty("CreatedBy_DisplayValue").GetString());

        async Task<string> CreateAsync(string tab, object payload) => BusinessAuditSqlServerHttpApiTests.Id(
            await http.CreateButtonAsync(admin,
                http.Organization(tab, "create", "list.pageActions"), $"/api/organization/{tab}", payload));

        async Task<JsonElement> ReadFieldsAsync(ICP.Platform.Kernel.ModulePlanning.FeaturePoint point, string tab, string id) =>
            (await DeclaredReadAsync(fixture, ordinary, point, "read", $"/api/organization/{tab}/{id}")).GetProperty("fields");

        async Task DenyAsync(string ruleId, string resourceKey, string id)
        {
            var rule = new DataScopeRuleDto(ruleId, ruleId, null, ruleId, true, resourceKey,
                new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
                [new("Subject_" + ruleId, "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
            using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin,
                fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save"),
                HttpMethod.Put, "/api/authorization/data-scope/rules/" + ruleId, rule);
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }

        async Task AllowObjectAsync(string resourceKey, string id)
        {
            var grant = new DataScopeObjectDto(resourceKey, id,
                [new("DisplayAllow237_" + id, "user", actor.AccountId, 0, new Dictionary<string, string> { ["read"] = "allow" })]);
            using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin,
                http.Organization(resourceKey == "organization.company" ? "companies" : "departments", "authorize", "list.rowActions"),
                HttpMethod.Put, $"/api/authorization/data-scope/objects/{resourceKey}/{id}", grant);
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }
    }

    private sealed class DisplaySqlObserver : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public List<string> Commands { get; } = [];

        /// <inheritdoc/>
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled) Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DisplayReadFault
    {
        public string? ResourceKey { get; set; }
    }

    private sealed class FaultingReadableIds(IDataScopeReadableIds inner, DisplayReadFault control) : IDataScopeReadableIds
    {
        /// <inheritdoc/>
        public Task<IReadOnlySet<string>> GetReadableIdsAsync(string resourceKey, IReadOnlyCollection<string> objectIds,
            CancellationToken cancellationToken = default) =>
            resourceKey == control.ResourceKey
                ? Task.FromException<IReadOnlySet<string>>(new DataScopeReadExecutionException(resourceKey, new IOException("injected")))
                : inner.GetReadableIdsAsync(resourceKey, objectIds, cancellationToken);
    }
}
