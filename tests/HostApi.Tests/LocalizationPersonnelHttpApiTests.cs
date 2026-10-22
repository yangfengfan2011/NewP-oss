using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Presentation;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static ICP.Platform.HostApi.Tests.FeatureHttpTestRequests;

namespace ICP.Platform.HostApi.Tests;

public sealed class LocalizationPersonnelHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实人员字段失败保留参数，正式显示投影消费活动词典及请求locale")]
    public async Task PersonnelErrorsAndSystemActorUseTheActiveAuthoringCatalog()
    {
        // 使用平台正式 system stamp producer，仍通过真实端点持久化和读取人员。
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.RemoveAll<IEntityAuditStampProvider>();
            services.AddSingleton<IEntityAuditStampProvider, SystemEntityAuditStampProvider>();
        }, useRequestPresentation: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var create = http.Organization("persons", "create", "list.pageActions");
        JsonElement? previousProblem = null;
        foreach (var locale in new[] { "zh-CN", "en-US" })
        {
            admin.DefaultRequestHeaders.Remove("X-ICP-Locale");
            admin.DefaultRequestHeaders.Add("X-ICP-Locale", locale);
            using var invalid = await SendAsync(admin, create, HttpMethod.Post, "/api/organization/persons", new
            {
                PersonNo = "LOCALIZATION-INVALID", ENAME = "保留用户数据", BirthDate = "2999-01-01", EmploymentStatus = "FullTime", IsEnabled = true
            });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("application/json", invalid.Content.Headers.ContentType?.MediaType);
            var problem = await invalid.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Organization.Person.BirthDateInFuture", problem.GetProperty("code").GetString());
            Assert.Equal("organization.person.birthDateInFuture", problem.GetProperty("reasonKey").GetString());
            Assert.Equal("出生日期不能晚于当前日期。", problem.GetProperty("message").GetString());
            var field = Assert.Single(problem.GetProperty("fieldErrors").EnumerateArray());
            Assert.Equal("BirthDate", field.GetProperty("field").GetString());
            Assert.Equal(problem.GetProperty("code").GetString(), field.GetProperty("code").GetString());
            Assert.Equal("Organization.Person.BirthDateInFuture", field.GetProperty("reasonKey").GetString());
            Assert.Equal(problem.GetProperty("args").GetProperty("date").GetString(), field.GetProperty("args").GetProperty("date").GetString());
            if (previousProblem.HasValue) Assert.Equal(previousProblem.Value.GetProperty("args").GetRawText(), problem.GetProperty("args").GetRawText());
            previousProblem = problem;
        }
        using var created = await SendAsync(admin, create, HttpMethod.Post, "/api/organization/persons", new
        {
            PersonNo = "LOCALIZATION-VALID", ENAME = "保留用户数据", EmploymentStatus = "FullTime", IsEnabled = true
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = BusinessAuditSqlServerHttpApiTests.Id(await created.Content.ReadFromJsonAsync<JsonElement>());
        var catalog = fixture.Services.GetRequiredService<IRuntimeTextCatalog>();
        foreach (var locale in new[] { "zh-CN", "en-US" })
        {
            admin.DefaultRequestHeaders.Remove("X-ICP-Locale");
            admin.DefaultRequestHeaders.Add("X-ICP-Locale", locale);
            using var read = await SendAsync(admin, http.Organization("persons"), HttpMethod.Get, "/api/organization/persons/" + id);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var fields = (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fields");
            Assert.Equal("system", fields.GetProperty("CreatedBy").GetString());
            Assert.Equal(catalog.GetText("Platform.SystemActor", locale), fields.GetProperty("CreatedBy_DisplayValue").GetString());
            Assert.Equal("保留用户数据", fields.GetProperty("ENAME").GetString());
        }
    }
}
