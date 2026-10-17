using ICP.Platform.Authorization.Contracts;
using System.Net;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeDirectSubjectSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "部门选择器false/null为同身份，包含下级true为另一范围；技术ID重建无变化")]
    public async Task DepartmentSelector_NormalizesFalseAndNull_ButKeepsDescendantRangeIndependent()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        using var initialized = await FeatureHttpTestRequests.SendAsync(admin,
            fixture.FeatureCatalog.First(point => point.BehaviorKey == "organization.company.initialize"), HttpMethod.Post, "/api/organization/initialization");
        Assert.Equal(HttpStatusCode.OK, initialized.StatusCode);
        var department = (await initialized.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("departmentId").GetString()!;
        var person = await FeatureHttpTestRequests.CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "SELECTOR295", ENAME = "部门选择器", EmploymentStatus = "Active", IsEnabled = true });
        var id = BusinessAuditSqlServerHttpApiTests.Id(person);
        var manage = fixture.FeatureCatalog.First(point => point.BehaviorKey == "organization.person.authorize");
        var path = "/api/authorization/data-scope/objects/organization.person/" + id;
        var selector = new DataScopeDirectSubjectDto("Selector295Direct", "department", department, 1,
            new Dictionary<string, string> { ["read"] = "allow" });
        using (var duplicate = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path,
            new DataScopeObjectDto("organization.person", id, [selector, selector with { Id = "Selector295False", MatchOptions = new(false) }])))
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        var configuration = new DataScopeObjectDto("organization.person", id,
            [selector, selector with { Id = "Selector295Descendants", MatchOptions = new(true), Priority = 2,
                Actions = new Dictionary<string, string> { ["read"] = "deny" } }]);
        using (var valid = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, configuration))
            Assert.True(valid.StatusCode == HttpStatusCode.NoContent, await valid.Content.ReadAsStringAsync());
        using (var unchanged = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path,
            configuration with { Subjects = [configuration.Subjects[1] with { Id = "Selector295NewDescendants" },
                configuration.Subjects[0] with { Id = "Selector295NewDirect", MatchOptions = new(false) }] }))
            Assert.Equal(HttpStatusCode.NoContent, unchanged.StatusCode);
        var stored = await FeatureHttpTestRequests.ReadAsync(admin, manage, path);
        Assert.Equal(2, stored.GetProperty("subjects").GetArrayLength());
        Assert.Contains(stored.GetProperty("subjects").EnumerateArray(), subject => subject.GetProperty("id").GetString() == "Selector295Direct");
        Assert.Contains(stored.GetProperty("subjects").EnumerateArray(), subject => subject.GetProperty("id").GetString() == "Selector295Descendants");
        var audit = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(),
            "/api/audit/business-changes?TargetId=" + Uri.EscapeDataString("organization.person/" + id) + "&EventType=dataScope.object.changed");
        Assert.Equal(1, audit.GetProperty("items").GetArrayLength());
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "直接项规范化、重复主体、期限往返及审计失败/已Flush取消保持原配置")]
    public async Task DirectReplacement_NormalizesExpiryAndNoChange_RejectsDuplicateSubjects_AndRollsBackFaults()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var person = await FeatureHttpTestRequests.CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "DIRECT295", ENAME = "直接项期限", EmploymentStatus = "Active", IsEnabled = true });
        var id = BusinessAuditSqlServerHttpApiTests.Id(person);
        var manage = fixture.FeatureCatalog.First(point => point.BehaviorKey == "organization.person.authorize");
        var path = "/api/authorization/data-scope/objects/organization.person/" + id;
        var baseline = new DataScopeObjectDto("organization.person", id,
            [new("Direct295All", "all", null, 1, new Dictionary<string, string> { ["read"] = "allow" })]);
        await SaveAsync(baseline, HttpStatusCode.NoContent);
        await SaveAsync(new { resourceKey = baseline.ResourceKey, objectId = id, subjects = new[]
        {
            new { id = "Direct295All", type = "all", subjectId = (string?)null, priority = 1,
                actions = new Dictionary<string, string> { ["edit"] = "unset", ["read"] = "allow" },
                matchOptions = new { includeDescendants = false }, expiresAt = (string?)null }
        } }, HttpStatusCode.NoContent);
        await SaveAsync(baseline with { Subjects = [baseline.Subjects[0] with { Id = "Direct295ClientRegenerated" }] }, HttpStatusCode.NoContent);
        Assert.Equal(1, await AuditCountAsync());
        var local = baseline with { Subjects = [baseline.Subjects[0] with { ExpiresAt = "2026-10-17T20:00:00" }] };
        await SaveAsync(local, HttpStatusCode.NoContent);
        var utc = local with { Subjects = [local.Subjects[0] with { ExpiresAt = "2026-10-17T12:00:00Z" }] };
        await SaveAsync(utc, HttpStatusCode.NoContent);
        await AssertOriginalAsync();
        await SaveAsync(utc with { Subjects = [utc.Subjects[0], utc.Subjects[0] with { Id = "Direct295Duplicate" }] }, HttpStatusCode.BadRequest);
        foreach (var invalid in new[] { "2026-10-17T12:00:00+00:00", "2026-10-17T12:00:00.100Z" })
            await SaveAsync(utc with { Subjects = [utc.Subjects[0] with { ExpiresAt = invalid }] }, HttpStatusCode.BadRequest);
        var changed = utc with { Subjects = [utc.Subjects[0] with { ExpiresAt = "2026-10-17T12:01:00Z" }] };
        foreach (var mode in new[] { "register", "save", "commit" })
        {
            fixture.Fault.Mode = mode;
            try { await SaveAsync(changed, HttpStatusCode.InternalServerError); }
            finally { fixture.Fault.Mode = ""; }
            await AssertOriginalAsync();
        }
        using (var scope = fixture.Services.CreateScope())
        using (var cancellation = new CancellationTokenSource())
        {
            using var activity = new Activity("data-scope-direct-cancellation").Start();
            var flushed = false;
            var grants = scope.ServiceProvider.GetRequiredService<DataScopeObjectGrants>();
            var transactions = scope.ServiceProvider.GetRequiredService<IPlatformTransactionExecutor>();
            fixture.Fault.AfterRegister = async (_, token) =>
            {
                await transactions.FlushAsync(token);
                flushed = true;
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            };
            try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => grants.SaveAsync(changed, cancellation.Token)); }
            finally { fixture.Fault.AfterRegister = null; }
            Assert.True(flushed);
        }
        await AssertOriginalAsync();

        async Task SaveAsync(object input, HttpStatusCode expected)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, input);
            Assert.True(response.StatusCode == expected, $"Expected {expected}, actual {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
        async Task<int> AuditCountAsync()
        {
            var audit = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(),
                "/api/audit/business-changes?TargetId=" + Uri.EscapeDataString("organization.person/" + id) + "&EventType=dataScope.object.changed");
            return audit.GetProperty("items").GetArrayLength();
        }
        async Task AssertOriginalAsync()
        {
            var stored = await FeatureHttpTestRequests.ReadAsync(admin, manage, path);
            var subject = Assert.Single(stored.GetProperty("subjects").EnumerateArray());
            Assert.Equal("Direct295All", subject.GetProperty("id").GetString());
            Assert.Equal("2026-10-17T12:00:00Z", subject.GetProperty("expiresAt").GetString());
            Assert.Equal(2, await AuditCountAsync());
        }
    }
}
