using System.Data.Common;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Modules.Audit.Services;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Diagnostics;
using System.Text.Json.Serialization;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.HostApi.Authorization;
using ICP.Platform.HostApi.Persistence;
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.Audit;
using ICP.Platform.Modules.Audit.Persistence.Descriptors;
using ICP.Platform.Modules.Authentication;
using ICP.Platform.Modules.Authentication.Persistence.Descriptors;
using ICP.Platform.Modules.Authorization;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.Identity;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore.Transactions;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Presentation;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.HostApi.Controllers;
using ICP.Platform.HostApi.Antiforgery;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace ICP.Platform.HostApi.Tests;

public sealed class BusinessAuditSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "人员更新只登记实际规范差异并保留空值和原姓名")]
    public async Task PersonUpdate_RecordsActualChangedFieldsAndIgnoresUnchangedValues()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var client = await fixture.LoginAdministratorAsync();
        var company = await CreateAsync(client, "/api/organization/companies", new { ECODE = "EDIT", ENAME = "编辑公司", SortOrder = "0100" });
        var a = await CreateAsync(client, "/api/organization/departments", new { ECODE = "EDIT-A", ENAME = "部门A", CompanyId = Id(company), SortOrder = "0100" });
        var b = await CreateAsync(client, "/api/organization/departments", new { ECODE = "EDIT-B", ENAME = "部门B", CompanyId = Id(company), SortOrder = "0100" });
        var person = await CreateAsync(client, "/api/organization/persons", new { PersonNo = "EDIT-1", ENAME = "旧姓名", CompanyId = Id(company), DepartmentId = Id(a), BirthDate = "2000-02-29", EmploymentStatus = "Active", IsEnabled = true });
        var edit = new { ENAME = "新姓名", CompanyId = Id(company), DepartmentId = Id(b), BirthDate = (string?)null, EmploymentStatus = "Active", EmploymentStartedAt = "2026-01-01T01:02:03Z" };
        await UpdateAsync(client, Id(person), edit);
        await UpdateAsync(client, Id(person), edit);
        // 等价时间表达规范化为相同 UTC 时间；四字段变化不能生成业务记录。
        await UpdateAsync(client, Id(person), new { EmploymentStartedAt = "2026-01-01T09:02:03" });
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/audit/business-changes?TargetId={Id(person)}&EventType=person.updated");
        var row = Assert.Single(page.GetProperty("items").EnumerateArray());
        var fields = row.GetProperty("Changes").EnumerateArray().ToDictionary(c => c.GetProperty("field").GetString()!);
        Assert.Equal(new[] { "BirthDate", "DepartmentId", "ENAME", "EmploymentStartedAt" }, fields.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("旧姓名", fields["ENAME"].GetProperty("before").GetProperty("value").GetString());
        Assert.Equal("旧姓名", fields["ENAME"].GetProperty("beforeDisplay").GetString());
        Assert.Equal("新姓名", fields["ENAME"].GetProperty("after").GetProperty("value").GetString());
        Assert.Equal(Id(a), fields["DepartmentId"].GetProperty("before").GetProperty("value").GetString());
        Assert.Equal("部门B", fields["DepartmentId"].GetProperty("afterDisplay").GetString());
        Assert.Equal("2000-02-29", fields["BirthDate"].GetProperty("before").GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, fields["BirthDate"].GetProperty("after").GetProperty("value").ValueKind);
        Assert.Equal("2026-01-01T01:02:03.0000000Z", fields["EmploymentStartedAt"].GetProperty("after").GetProperty("value").GetString());
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        using var renamed = await client.PutAsync("/api/organization/departments/" + Id(b), JsonContent.Create(new { ENAME = "当前部门B" }, options: new JsonSerializerOptions()));
        Assert.True(renamed.IsSuccessStatusCode, await renamed.Content.ReadAsStringAsync());
        var currentNames = await client.GetFromJsonAsync<JsonElement>($"/api/audit/business-changes/{row.GetProperty("EID").GetString()}");
        var departmentChange = Assert.Single(currentNames.GetProperty("fields").GetProperty("Changes").EnumerateArray(), c => c.GetProperty("field").GetString() == "DepartmentId");
        Assert.Equal("当前部门B", departmentChange.GetProperty("afterDisplay").GetString());
        fixture.Fault.Mode = "names";
        var degraded = await client.GetFromJsonAsync<JsonElement>($"/api/audit/business-changes/{row.GetProperty("EID").GetString()}");
        var degradedDepartment = Assert.Single(degraded.GetProperty("fields").GetProperty("Changes").EnumerateArray(), c => c.GetProperty("field").GetString() == "DepartmentId");
        Assert.Equal(Id(a), degradedDepartment.GetProperty("beforeDisplay").GetString());
        Assert.Equal(Id(b), degradedDepartment.GetProperty("afterDisplay").GetString());
        fixture.Fault.Mode = "";
    }

    internal static async Task UpdateAsync(HttpClient client, string id, object value)
    {
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        using var response = await client.PutAsync("/api/organization/persons/" + id, JsonContent.Create(value, options: new JsonSerializerOptions()));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "人员更新登记及持久化失败共同回滚，拒绝输入不留成功记录")]
    public async Task PersonUpdate_FailuresAndRejectedFieldsLeaveBothStoresUnchanged()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var client = await fixture.LoginAdministratorAsync();
        var person = await CreateAsync(client, "/api/organization/persons", new { PersonNo = "FAIL-UPDATE", ENAME = "原姓名", EmploymentStatus = "Active", IsEnabled = true });
        foreach (var mode in new[] { "register", "write", "save", "commit" })
        {
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            fixture.Fault.Mode = mode;
            using var response = await client.PutAsync("/api/organization/persons/" + Id(person), JsonContent.Create(new { ENAME = "不应提交" }, options: new JsonSerializerOptions()));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            fixture.Fault.Mode = "";
            var actual = await client.GetFromJsonAsync<JsonElement>("/api/organization/persons/" + Id(person));
            Assert.Equal("原姓名", actual.GetProperty("fields").GetProperty("ENAME").GetString());
            var records = await client.GetFromJsonAsync<JsonElement>($"/api/audit/business-changes?TargetId={Id(person)}");
            Assert.Equal(1, records.GetProperty("total").GetInt32());
        }
        foreach (var invalid in new object[] { new { UpdatedBy = "client-actor" }, new { CompanyId = "missing-company" }, new { BirthDate = "2999-01-01" }, new { EmploymentStartedAt = "2026-09-10T00:00:00Z", EmploymentEndedAt = "2026-09-09T00:00:00Z" } })
        {
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            using var response = await client.PutAsync("/api/organization/persons/" + Id(person), JsonContent.Create(invalid, options: new JsonSerializerOptions()));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.NotEmpty(problem.GetProperty("fieldErrors").EnumerateArray());
        }
        // 启停不属于普通编辑字段；客户端塞入的字段不会扩大事件白名单。
        await UpdateAsync(client, Id(person), new { PersonNo = "OTHER", IsEnabled = false, Arbitrary = "ignored" });
        var unchanged = await client.GetFromJsonAsync<JsonElement>("/api/organization/persons/" + Id(person));
        Assert.True(unchanged.GetProperty("fields").GetProperty("IsEnabled").GetBoolean());
        Assert.Equal("FAIL-UPDATE", unchanged.GetProperty("fields").GetProperty("PersonNo").GetString());
        var final = await client.GetFromJsonAsync<JsonElement>($"/api/audit/business-changes?TargetId={Id(person)}");
        Assert.Equal(1, final.GetProperty("total").GetInt32());
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "并发人员编辑记录真实替换序列而非事务外快照")]
    public async Task PersonUpdate_ConcurrentRequestsRecordTheActualReplacementSequence()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var client = await fixture.LoginAdministratorAsync();
        var person = await CreateAsync(client, "/api/organization/persons", new { PersonNo = "RACE-UPDATE", ENAME = "起点", EmploymentStatus = "Active", IsEnabled = true });
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Fault.AfterRegister = async (change, token) =>
        {
            if (change.EventType == "person.updated" && change.Fields.Any(f => f.After.Value == "第一位"))
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        };
        var first = client.PutAsync("/api/organization/persons/" + Id(person), JsonContent.Create(new { ENAME = "第一位" }, options: new JsonSerializerOptions()));
        Task<HttpResponseMessage>? second = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = client.PutAsync("/api/organization/persons/" + Id(person), JsonContent.Create(new { ENAME = "第二位" }, options: new JsonSerializerOptions()));
            // 第一笔仍未保存；第二请求已到数据库边界，再给出真实 SQL 执行窗口。
            await fixture.Fault.ConcurrentCommand.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(200);
        }
        finally { release.TrySetResult(); }
        using var firstResult = await first;
        using var secondResult = await second!;
        Assert.True(firstResult.IsSuccessStatusCode, await firstResult.Content.ReadAsStringAsync());
        Assert.True(secondResult.IsSuccessStatusCode, await secondResult.Content.ReadAsStringAsync());
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/audit/business-changes?TargetId={Id(person)}&EventType=person.updated");
        var changes = page.GetProperty("items").EnumerateArray().Select(r => Assert.Single(r.GetProperty("Changes").EnumerateArray())).ToArray();
        Assert.Equal(2, changes.Length);
        var firstChange = Assert.Single(changes, c => c.GetProperty("after").GetProperty("value").GetString() == "第一位");
        var secondChange = Assert.Single(changes, c => c.GetProperty("after").GetProperty("value").GetString() == "第二位");
        Assert.Equal("起点", firstChange.GetProperty("before").GetProperty("value").GetString());
        Assert.Equal("第一位", secondChange.GetProperty("before").GetProperty("value").GetString());
        var final = await client.GetFromJsonAsync<JsonElement>("/api/organization/persons/" + Id(person));
        Assert.Equal("第二位", final.GetProperty("fields").GetProperty("ENAME").GetString());
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实登录人员创建与 Audit 同 SQL 事务提交")]
    public async Task PersonCreation_CommitsTypedRecordAndAdministratorCanReadIt()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var client = await fixture.LoginAdministratorAsync();
        var company = await CreateAsync(client, "/api/organization/companies", new { ECODE = "AUDIT", ENAME = "审计公司", SortOrder = "0100" });
        var department = await CreateAsync(client, "/api/organization/departments", new { ECODE = "AUDIT-A", ENAME = "部门A", CompanyId = Id(company), SortOrder = "0100" });
        var created = await CreateAsync(client, "/api/organization/persons", new
        {
            PersonNo = "AUDIT-001", ENAME = "初始姓名", CompanyId = Id(company), DepartmentId = Id(department),
            BirthDate = "2000-02-29", EmploymentStatus = "Active", EmploymentStartedAt = "2026-01-01T01:02:03Z", IsEnabled = true
        });
        var page = await client.GetFromJsonAsync<JsonElement>("/api/audit/business-changes?Resource=organization.person&TargetId=" + Id(created));
        var row = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("person.created", row.GetProperty("EventType").GetString());
        Assert.Equal(fixture.ActorId, row.GetProperty("ActorId").GetString());
        Assert.EndsWith("Z", row.GetProperty("OccurredAt").GetString());
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("TraceId").GetString()));
        var detail = await client.GetFromJsonAsync<JsonElement>("/api/audit/business-changes/" + row.GetProperty("EID").GetString());
        var changes = detail.GetProperty("fields").GetProperty("Changes").EnumerateArray().ToDictionary(c => c.GetProperty("field").GetString()!);
        Assert.Equal(10, changes.Count);
        Assert.Equal("2000-02-29", changes["BirthDate"].GetProperty("after").GetProperty("value").GetString());
        Assert.Equal("2026-01-01T01:02:03.0000000Z", changes["EmploymentStartedAt"].GetProperty("after").GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, changes["EmploymentEndedAt"].GetProperty("after").GetProperty("value").ValueKind);
        Assert.Equal("AUDIT-001", changes["PersonNo"].GetProperty("after").GetProperty("value").GetString());
        Assert.Equal("true", changes["IsEnabled"].GetProperty("after").GetProperty("value").GetString());
    }
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "登记、写入、保存和确定未提交故障共同回滚")]
    public async Task PersonCreation_WhenParticipantOrStoreFails_RollsBackPersonAndRecord()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var client = await fixture.LoginAdministratorAsync();
        foreach (var mode in new[] { "register", "write", "save", "commit" })
        {
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            fixture.Fault.Mode = mode;
            using var failure = await client.PostAsync("/api/organization/persons", JsonContent.Create(new
                { PersonNo = "ROLLBACK-" + mode, ENAME = "故障人员", EmploymentStatus = "Active", IsEnabled = true }, options: new JsonSerializerOptions()));
            Assert.True(failure.StatusCode == HttpStatusCode.InternalServerError, mode);
            fixture.Fault.Mode = "";
            var people = await client.GetFromJsonAsync<JsonElement>("/api/organization/persons?keyword=ROLLBACK");
            Assert.Equal(0, people.GetProperty("total").GetInt32());
            var records = await client.GetFromJsonAsync<JsonElement>("/api/audit/business-changes");
            Assert.Equal(0, records.GetProperty("total").GetInt32());
        }
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        using var invalid = await client.PostAsync("/api/organization/persons", JsonContent.Create(new { PersonNo = "INVALID", ENAME = "校验失败", CompanyId = "absent", EmploymentStatus = "Active", IsEnabled = true }, options: new JsonSerializerOptions()));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/audit/business-changes")).GetProperty("total").GetInt32());
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "管理员查询存储过滤稳定分页，普通账号不能用 View 绕过")]
    public async Task Query_FiltersInSqlAndKeepsAdministratorAdmissionAndFailures()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        using var accountResponse = await admin.PostAsJsonAsync("/api/identity/accounts", new { ECODE = "writer", ENAME = "人员操作者" });
        Assert.Equal(HttpStatusCode.Created, accountResponse.StatusCode);
        var account = await accountResponse.Content.ReadFromJsonAsync<JsonElement>();
        var actor = account.GetProperty("account").GetProperty("eid").GetString()!;
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(admin);
        using var roleResponse = await admin.PostAsJsonAsync("/api/authorization/roles", new { Code = "AuditWriter", Name = "记录验证职责" });
        var role = (await roleResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        using var permissionResponse = await admin.PutAsJsonAsync($"/api/authorization/roles/{role}/permissions", new { PermissionCodes = new[] { "Organization.Person.Create", "Organization.Person.View", "Audit.BusinessChange.View" } });
        Assert.Equal(HttpStatusCode.NoContent, permissionResponse.StatusCode);
        using var assignment = await admin.PutAsJsonAsync($"/api/authorization/users/{actor}/roles", new { RoleCodes = new[] { "AuditWriter" } });
        Assert.Equal(HttpStatusCode.NoContent, assignment.StatusCode);
        using var writer = await fixture.LoginAsync("writer", account.GetProperty("temporaryCredential").GetString()!);
        var first = await CreateAsync(admin, "/api/organization/persons", new { PersonNo = "QUERY-A", ENAME = "管理员创建", EmploymentStatus = "Active", IsEnabled = true });
        var second = await CreateAsync(writer, "/api/organization/persons", new { PersonNo = "QUERY-B", ENAME = "维护者创建", EmploymentStatus = "Active", IsEnabled = true });
        using var deniedUpdate = await writer.PutAsync("/api/organization/persons/" + Id(second), JsonContent.Create(new { ENAME = "无权编辑" }, options: new JsonSerializerOptions()));
        Assert.Equal(HttpStatusCode.Forbidden, deniedUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await writer.GetAsync("/api/audit/business-changes")).StatusCode);
        var filtered = await admin.GetFromJsonAsync<JsonElement>($"/api/audit/business-changes?Resource=organization.person&ActorId={actor}&EventType=person.created&TargetId={Id(second)}&From=2020-01-01T00:00:00Z&To=2040-01-01T00:00:00Z");
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        var secondRecord = Assert.Single(filtered.GetProperty("items").EnumerateArray());
        Assert.Equal(actor, secondRecord.GetProperty("ActorId").GetString());
        Assert.Equal("人员操作者", secondRecord.GetProperty("ActorId_DisplayValue").GetString());
        Assert.Equal("维护者创建", secondRecord.GetProperty("TargetId_DisplayValue").GetString());
        Assert.Equal(secondRecord.GetProperty("ActorId_DisplayValue").GetString(), secondRecord.GetProperty("CreatedBy_DisplayValue").GetString());
        foreach (var range in new[] { "From=invalid", "From=2026-01-01T00:00:00", "From=2040-01-01T00:00:00Z&To=2020-01-01T00:00:00Z" })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/audit/business-changes?" + range)).StatusCode);
        fixture.Fault.Commands.Clear();
        var page1 = await admin.GetFromJsonAsync<JsonElement>("/api/audit/business-changes?Resource=organization.person&PageSize=1&pageIndex=1");
        var page2 = await admin.GetFromJsonAsync<JsonElement>("/api/audit/business-changes?Resource=organization.person&PageSize=1&pageIndex=2");
        Assert.Equal(2, page1.GetProperty("total").GetInt32());
        var row1 = Assert.Single(page1.GetProperty("items").EnumerateArray());
        var row2 = Assert.Single(page2.GetProperty("items").EnumerateArray());
        Assert.Equal(row1.GetProperty("OccurredAt").GetString(), row2.GetProperty("OccurredAt").GetString());
        Assert.True(string.CompareOrdinal(row1.GetProperty("EID").GetString(), row2.GetProperty("EID").GetString()) > 0);
        Assert.Contains(fixture.Fault.Commands, sql => sql.Contains("COUNT(*)") && sql.Contains("WHERE"));
        Assert.Contains(fixture.Fault.Commands, sql => sql.Contains("ORDER BY") && sql.Contains("[OccurredAt] DESC") && sql.Contains("[EID] DESC") && sql.Contains("OFFSET") && sql.Contains("FETCH NEXT"));
        fixture.Fault.Mode = "query";
        Assert.Equal(HttpStatusCode.InternalServerError, (await admin.GetAsync("/api/audit/business-changes")).StatusCode);
        fixture.Fault.Mode = "names";
        var degraded = await admin.GetFromJsonAsync<JsonElement>($"/api/audit/business-changes?TargetId={Id(first)}");
        Assert.Equal(Id(first), Assert.Single(degraded.GetProperty("items").EnumerateArray()).GetProperty("TargetId").GetString());
        fixture.Fault.Mode = "";
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/organization/persons/{Id(second)}")).StatusCode);
        var retained = await admin.GetFromJsonAsync<JsonElement>($"/api/audit/business-changes/{secondRecord.GetProperty("EID").GetString()}");
        Assert.Equal(Id(second), retained.GetProperty("fields").GetProperty("TargetId").GetString());
        Assert.Equal(JsonValueKind.Null, retained.GetProperty("fields").GetProperty("TargetId_DisplayValue").ValueKind);
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
        {
            using var request = new HttpRequestMessage(method, "/api/audit/business-changes" + (method == HttpMethod.Post ? "" : "/" + secondRecord.GetProperty("EID").GetString()));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.SendAsync(request)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/audit/business-changes/export")).StatusCode);
    }

    internal static string Id(JsonElement detail) => detail.GetProperty("fields").GetProperty("EID").GetString()!;
    internal static async Task<JsonElement> CreateAsync(HttpClient client, string route, object value)
    {
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        using var response = await client.PostAsync(route, JsonContent.Create(value, options: new JsonSerializerOptions()));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}

internal sealed class BusinessAuditHostFixture : IAsyncDisposable
{
    private readonly string _connection;
    private readonly string _admin;
    private readonly string _catalog;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "newp-audit-" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    internal StoreFault Fault { get; } = new();
    private string _temporaryCredential = "";
    internal string ActorId { get; private set; } = "";
    private BusinessAuditHostFixture(string admin, string catalog)
    {
        _admin = admin; _catalog = catalog;
        _connection = new SqlConnectionStringBuilder(admin) { InitialCatalog = catalog }.ConnectionString;
    }
    internal static async Task<BusinessAuditHostFixture> CreateAsync()
    {
        var admin = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEWP_ORGANIZATION_SQLSERVER_ADMIN_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true") { InitialCatalog = "master" }.ConnectionString;
        var fixture = new BusinessAuditHostFixture(admin, "NewPBusinessAudit_" + Guid.NewGuid().ToString("N"));
        await fixture.ExecuteAdminAsync($"CREATE DATABASE [{fixture._catalog}]");
        try
        {
            await fixture.StartAsync();
            using var scope = fixture._app.Services.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IIdentityLocalAdministratorMaintenanceService>().BootstrapAsync(new(
                "admin", "审计管理员", "审计集成准备", new(IdentityMaintenanceActors.LocalMaintenance, IdentitySecurityActorKind.LocalMaintenance, Guid.NewGuid().ToString("N"))));
            fixture.ActorId = result.Account.AccountEid;
            fixture._temporaryCredential = result.TemporaryCredential;
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }
    private async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Host:KeyRingPath"] = Path.Combine(_root, "keys"),
            ["Authentication:Host:CookieName"] = "ICP.Platform.BusinessAuditTest",
            ["Authentication:Host:AllowInsecureDevelopmentCookie"] = "true",
            ["Identity:Credentials:UseSharedDevelopmentCredential"] = "false",
            ["Logging:LogLevel:Default"] = "Warning"
        });
        IModule[] modules = [new IdentityModule(), new AuthenticationModule(), new AuthorizationModule(), new AuditModule(), new OrganizationModule()];
        foreach (var module in modules) module.ConfigureServices(builder.Services);
        builder.Services.AddPlatformAuthorization();
        builder.Services.AddAuthorization(options => options.AddPolicy("AdminOnly", p => p.RequireRole("LocalAdmin")));
        builder.Services.AddSingleton<TimeProvider>(new FrozenClock());
        builder.Services.AddScoped<BusinessAuditRecorder>();
        builder.Services.AddScoped<IBusinessAuditRecorder>(sp => new FaultRecorder(sp.GetRequiredService<BusinessAuditRecorder>(), Fault));
        builder.Services.AddHostAuthentication(builder.Configuration, builder.Environment);
        builder.Services.AddHostAntiforgery(builder.Environment);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IEntityAuditStampProvider, HttpContextEntityAuditStampProvider>();
        builder.Services.AddScoped<IPlatformTransactionExecutor, EfPlatformTransactionExecutor>();
        builder.Services.AddScoped<ITemporalDisplayFormatter, TemporalDisplayFormatter>();
        builder.Services.AddScoped<ITemporalDisplayProjectionExecutor, TemporalDisplayProjectionExecutor>();
        builder.Services.AddSingleton<IRequestPresentationContextAccessor, TestPresentation>();
        builder.Services.AddScoped<ITemporalInputNormalizer>(sp => new TemporalInputNormalizer(sp.GetRequiredService<IRequestPresentationContextAccessor>(), "Asia/Shanghai", TimeProvider.System));
        var models = IdentityModelDefinitions.All.Select(m => m with { OwnerModuleId = "Identity" })
            .Concat(AuthenticationModelDefinitions.All.Select(m => m with { OwnerModuleId = "Authentication" }))
            .Concat(AuthorizationModelDefinitions.All.Select(m => m with { OwnerModuleId = "Authorization" }))
            .Concat(OrganizationModelDefinitions.All.Select(m => m with { OwnerModuleId = "Organization" }))
            .Concat(new AuditModelMetadataProvider().GetDefinitions().Select(m => m with { OwnerModuleId = "Audit" })).ToArray();
        var plan = new CompiledPersistencePlan("business-audit-test", "snapshot", "hash", default, "digest", [], models, []);
        builder.Services.AddSingleton(plan);
        builder.Services.AddDbContext<EfPlatformDbContext>((sp, options) => options.UseSqlServer(_connection)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().AddInterceptors(Fault, new SaveFault(Fault), new CommitFault(Fault)));
        builder.Services.AddControllers(options => { options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true; options.Filters.Add<AuthenticationSessionCookieResultFilter>(); })
            .AddApplicationPart(typeof(AuthController).Assembly).AddApplicationPart(typeof(IdentityModule).Assembly).AddApplicationPart(typeof(AuditModule).Assembly).AddApplicationPart(typeof(AuthorizationModule).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        _app = builder.Build();
        _app.Use(async (context, next) => { using var activity = new Activity("business-audit-http").Start(); await next(); });
        _app.UseRouting();
        _app.UseRateLimiter();
        _app.UseAuthentication();
        _app.UseHostAntiforgery();
        _app.UseMiddleware<RestrictedAuthenticationSessionMiddleware>();
        _app.UseAuthorization();
        _app.MapControllers();
        _app.MapStandardCrudResources();
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.EnsureCreatedAsync();
            var permissions = modules.OfType<IPermissionProvider>().SelectMany(p => p.GetPermissions()).ToArray();
            await scope.ServiceProvider.GetRequiredService<AuthorizationCatalogInitializer>().InitializeAsync(permissions);
        }
        await _app.StartAsync();
    }
    internal sealed class StoreFault : DbCommandInterceptor
    {
        internal string Mode { get; set; } = "";
        internal Func<BusinessAuditChange, CancellationToken, Task>? AfterRegister { get; set; }
        internal volatile bool RegistrationWaiting;
        internal TaskCompletionSource ConcurrentCommand { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (RegistrationWaiting && command.CommandText.Contains("[Person]", StringComparison.Ordinal)) ConcurrentCommand.TrySetResult();
            if (command.CommandText.Contains("[BusinessChange]", StringComparison.Ordinal))
            {
                Commands.Add(command.CommandText);
                if (Mode == "query" && command.CommandText.Contains("SELECT", StringComparison.Ordinal)) throw new InvalidOperationException("injected query failure");
            }
            if (Mode == "names" && (command.CommandText.Contains("[Person]", StringComparison.Ordinal) || command.CommandText.Contains("[Department]", StringComparison.Ordinal))) throw new InvalidOperationException("injected name directory failure");
            if (Mode == "write" && command.CommandText.Contains("INSERT INTO", StringComparison.Ordinal) && command.CommandText.Contains("[BusinessChange]", StringComparison.Ordinal)) throw new InvalidOperationException("injected audit write failure");
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (RegistrationWaiting && command.CommandText.Contains("[Person]", StringComparison.Ordinal)) ConcurrentCommand.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
    private sealed class FrozenClock : TimeProvider
    {
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
    }
    private sealed class SaveFault(StoreFault fault) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (fault.Mode == "save") throw new InvalidOperationException("injected save failure");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class CommitFault(StoreFault fault) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (fault.Mode == "commit") throw new InvalidOperationException("injected known-uncommitted failure");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class FaultRecorder(BusinessAuditRecorder recorder, StoreFault fault) : IBusinessAuditRecorder
    {
        public async ValueTask RegisterAsync(BusinessAuditChange change, CancellationToken cancellationToken = default)
        {
            await recorder.RegisterAsync(change, cancellationToken);
            if (fault.Mode == "register") throw new InvalidOperationException("injected registration failure");
            if (fault.AfterRegister is not null)
            {
                fault.RegistrationWaiting = true;
                await fault.AfterRegister(change, cancellationToken);
            }
        }
    }
    private sealed class TestPresentation : IRequestPresentationContextAccessor
    {
        public RequestPresentationContext? Current => new("zh-CN", "Asia/Shanghai");
    }
    private sealed class Cookies(HttpMessageHandler handler) : DelegatingHandler(handler)
    {
        private readonly CookieContainer _cookies = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.TryAddWithoutValidation("Cookie", _cookies.GetCookieHeader(request.RequestUri!));
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                foreach (var value in values) _cookies.SetCookies(request.RequestUri!, value);
            return response;
        }
    }
    internal Task<HttpClient> LoginAdministratorAsync() => LoginAsync("admin", _temporaryCredential);
    internal async Task<HttpClient> LoginAsync(string code, string password)
    {
        var client = new HttpClient(new Cookies(_app.GetTestServer().CreateHandler())) { BaseAddress = new Uri("http://localhost") };
        await RefreshAntiforgeryAsync(client);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { ECODE = code, Password = password });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var current = await client.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        await RefreshAntiforgeryAsync(client);
        response = await client.PostAsJsonAsync("/api/identity/me/password-change", new { CurrentPassword = password, NewPassword = "Audit-test-secure-938!", ExpectedRevision = current.GetProperty("revision").GetInt64() });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return client;
    }
    internal static async Task RefreshAntiforgeryAsync(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<AntiforgeryTokenResponse>(HostAntiforgeryDefaults.TokenPath);
        client.DefaultRequestHeaders.Remove(HostAntiforgeryDefaults.HeaderName);
        client.DefaultRequestHeaders.Add(HostAntiforgeryDefaults.HeaderName, token!.RequestToken);
    }
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ICP-Platform.sln"))) directory = directory.Parent;
        return directory!.FullName;
    }
    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new SqlConnection(_admin); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
    }
    public async ValueTask DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
        SqlConnection.ClearAllPools();
        await ExecuteAdminAsync($"ALTER DATABASE [{_catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_catalog}]");
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
