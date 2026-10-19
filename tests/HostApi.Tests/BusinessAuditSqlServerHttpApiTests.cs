using System.Data.Common;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Modules.Audit.Services;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Diagnostics;
using System.Text.Json.Serialization;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.HostApi.Authentication;
using ICP.Platform.HostApi.Authorization;
using ICP.Platform.HostApi.Persistence;
using ICP.Platform.Kernel.Extensions;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Audit;
using ICP.Platform.Modules.Audit.Persistence.Descriptors;
using ICP.Platform.Modules.Authentication;
using ICP.Platform.Modules.Authentication.Persistence.Descriptors;
using ICP.Platform.Modules.Authorization;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.Identity;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Services;
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
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "正式模块无效扩展顺序在 Host 接受请求前失败关闭")]
    public async Task HostStartup_WhenFormalModuleContributionHasMissingOrderDependency_RejectsBeforeRequests()
    {
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => BusinessAuditHostFixture.CreateAsync(services =>
            services.AddObjectOperationExtension<InvalidOrderPersonCreateProbeExtension>(
                new ObjectOperationExtensionProbeModule(),
                InvalidOrderPersonCreateProbeExtension.Definition)));

        Assert.Contains("OBJECT_OPERATION_EXTENSION_ORDER_DEPENDENCY_NOT_FOUND", exception.ToString(), StringComparison.Ordinal);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "正式行为工厂返回不一致 Definition 时 Host 启动失败关闭")]
    public async Task HostStartup_WhenFormalBehaviorFactoryReturnsMismatchedDefinition_RejectsBeforeRequests()
    {
        var exception = await Record.ExceptionAsync(async () =>
        {
            await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
                services.AddObjectBehavior<MismatchedDefinitionObjectBehavior>(
                    new ObjectOperationExtensionProbeModule(),
                    MismatchedDefinitionObjectBehavior.DeclaredDefinition,
                    static (_, _) => new MismatchedDefinitionObjectBehavior()));
        });

        Assert.NotNull(exception);
        Assert.Contains("OBJECT_OPERATION_BEHAVIOR_DEFINITION_MISMATCH", exception.ToString(), StringComparison.Ordinal);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "两个正式Identity普通账号消费父规格维护与查询职责")]
    public async Task UserRoles_TwoRealAccountsConsumeConfiguredResponsibilitiesOnNewLogin()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var views = new[] { "companies", "departments", "positions", "persons" }.Select(tab => http.Organization(tab)).ToArray();
        foreach (var maintenance in new[] { true, false })
        {
            var code = maintenance ? "assignment-one" : "assignment-two";
            using var accountResponse = await FeatureHttpTestRequests.SendAsync(admin, new FeatureHttpTestRequests(fixture).Identity("create"), HttpMethod.Post, "/api/identity/accounts", new { ECODE = code, ENAME = code });
            var account = await accountResponse.Content.ReadFromJsonAsync<JsonElement>();
            var id = account.GetProperty("account").GetProperty("eid").GetString();
            using var roleResponse = await FeatureHttpTestRequests.SendAsync(admin, http.Role("create"), HttpMethod.Post, "/api/authorization/roles", new { Code = code, Name = code });
            var roleId = (await roleResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
            var points = maintenance ? views.Concat(new[] { http.Organization("persons", "create", "list.pageActions"), http.Organization("persons", "edit", "list.rowActions") }).ToArray() : views;
            await http.GrantAsync(admin, roleId!, points);
            Assert.Equal(HttpStatusCode.NoContent, (await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put, $"/api/authorization/users/{id}/roles", new { RoleCodes = new[] { code } })).StatusCode);
            var candidates = await FeatureHttpTestRequests.ReadAsync(admin, http.Role("userRoles"), $"/api/authorization/users/candidates?query={code}");
            Assert.Equal(id, Assert.Single(candidates.EnumerateArray()).GetProperty("id").GetString());
            await HttpTestActors.PrepareOrganizationAsync(admin, http, account.GetProperty("account"));
            using var user = await fixture.LoginAsync(code, account.GetProperty("temporaryCredential").GetString()!);
            var current = await user.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
            Assert.Equal(points.SelectMany(point => point.EntryDependencies.Append(point.Position)).Distinct().Order().ToArray(), current.GetProperty("featurePositions").EnumerateArray().Select(p => p.GetString()).Order().ToArray());
            Assert.Equal(HttpStatusCode.OK, (await FeatureHttpTestRequests.SendAsync(user, http.Organization("persons"), HttpMethod.Get, "/api/organization/persons")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await FeatureHttpTestRequests.SendAsync(user, http.Role("userRoles"), HttpMethod.Get, "/api/authorization/users/candidates")).StatusCode);
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(admin);
        }
    }
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "普通账号关系共同提交、完整回滚及真实登录消费")]
    public async Task UserRoles_AtomicReplacementProtectsMembersAndResolvesHistory()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        using var accountResponse = await FeatureHttpTestRequests.SendAsync(admin, new FeatureHttpTestRequests(fixture).Identity("create"), HttpMethod.Post, "/api/identity/accounts", new { ECODE = "assigned-user", ENAME = "分配账号" });
        var account = await accountResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = account.GetProperty("account").GetProperty("eid").GetString();
        using var roleResponse = await FeatureHttpTestRequests.SendAsync(admin, http.Role("create"), HttpMethod.Post, "/api/authorization/roles", new { Code = "Assigned", Name = "分配职责" });
        var roleId = (await roleResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        using var otherResponse = await FeatureHttpTestRequests.SendAsync(admin, http.Role("create"), HttpMethod.Post, "/api/authorization/roles", new { Code = "Other", Name = "另一职责" });
        var otherId = (await otherResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        var route = $"/api/authorization/users/{userId}/roles";
        await http.GrantAsync(admin, roleId!, http.Organization("persons"));
        using var saved = await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put, route, new { RoleCodes = new[] { "Assigned", "Other" } });
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        foreach (var codes in new[] { new[] { "Assigned", "Other" }, new[] { "Other", "Assigned", "Assigned" } })
            Assert.Equal(HttpStatusCode.NoContent, (await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put, route, new { RoleCodes = codes })).StatusCode);
        foreach (var mode in new[] { "register", "write", "save", "commit", "unknown", "protected" })
        {
            fixture.Fault.Mode = mode;
            using var failed = await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put, route, new { RoleCodes = mode == "unknown" ? new[] { "Other", "Missing" } : mode == "protected" ? new[] { "Other", "LocalAdmin" } : Array.Empty<string>() });
            Assert.Equal(mode is "unknown" or "protected" ? HttpStatusCode.BadRequest : HttpStatusCode.InternalServerError, failed.StatusCode);
            if (mode is "unknown" or "protected") Assert.Equal(400, (await failed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetInt32());
            fixture.Fault.Mode = "";
            var current = await FeatureHttpTestRequests.ReadAsync(admin, http.Role("userRoles"), route);
            Assert.Equal(new[] { "Assigned", "Other" }, current.GetProperty("roleCodes").EnumerateArray().Select(c => c.GetString()).Order().ToArray());
            var history = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), $"/api/audit/business-changes?TargetId={userId}&Resource=identity.account&EventType=user.roles.changed");
            var record = Assert.Single(history.GetProperty("items").EnumerateArray());
            Assert.Equal("分配账号", record.GetProperty("TargetId_DisplayValue").GetString());
            Assert.Equal(2, record.GetProperty("Changes").GetArrayLength());
            Assert.Contains(record.GetProperty("Changes").EnumerateArray(), c => c.GetProperty("after").GetProperty("value").GetString() == roleId && c.GetProperty("afterDisplay").GetString() == "分配职责");
        }
        await HttpTestActors.PrepareOrganizationAsync(admin, http, account.GetProperty("account"));
        using var user = await fixture.LoginAsync("assigned-user", account.GetProperty("temporaryCredential").GetString()!);
        var projection = await user.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        Assert.Contains(projection.GetProperty("featurePositions").EnumerateArray(), p => p.GetString() == http.Organization("persons").Position);
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(admin);
        using var rename = await FeatureHttpTestRequests.SendAsync(admin, http.Role("edit"), HttpMethod.Put, $"/api/authorization/roles/{roleId}", new { Name = "新职责名", IsEnabled = false });
        Assert.True(rename.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put, route, new { RoleCodes = new[] { "Assigned", "Other" } })).StatusCode);
        var inactive = await user.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        Assert.DoesNotContain(inactive.GetProperty("featurePositions").EnumerateArray(), p => p.GetString() == http.Organization("persons").Position);
        var historyAfterRename = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), $"/api/audit/business-changes?TargetId={userId}");
        var unchangedRecord = Assert.Single(historyAfterRename.GetProperty("items").EnumerateArray());
        var detail = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), $"/api/audit/business-changes/{unchangedRecord.GetProperty("EID").GetString()}");
        Assert.Contains(detail.GetProperty("fields").GetProperty("Changes").EnumerateArray(), c => c.GetProperty("afterDisplay").GetString() == "新职责名");
        var administrator = await admin.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        var administratorId = administrator.GetProperty("id").GetString();
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put, $"/api/authorization/users/{administratorId}/roles", new { RoleCodes = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Get, $"/api/authorization/users/{administratorId}/roles")).StatusCode);
        using var disabled = await FeatureHttpTestRequests.SendAsync(admin, new FeatureHttpTestRequests(fixture).Identity("disable"), HttpMethod.Put, $"/api/identity/accounts/{userId}/status/disable", new { TargetStatus = "Disabled", ExpectedRevision = projection.GetProperty("revision").GetInt64(), Reason = "测试停用" });
        Assert.True(disabled.IsSuccessStatusCode, await disabled.Content.ReadAsStringAsync());
        var disabledAccount = await disabled.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.NoContent, (await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put, route, new { RoleCodes = new[] { "Assigned" } })).StatusCode);
        using var protectedDeletion = await FeatureHttpTestRequests.SendAsync(admin, http.Identity("delete"), HttpMethod.Post,
            $"/api/identity/accounts/{userId}/delete", new { ExpectedRevision = disabledAccount.GetProperty("revision").GetInt64(), Reason = "永久绑定保护" });
        Assert.Equal(HttpStatusCode.Conflict, protectedDeletion.StatusCode);
        // 已登录账号必须永久绑定；删除后的历史解析由另一个从未绑定、从未登录的账号证明。
        var deletable = await FeatureHttpTestRequests.CreateAsync(admin, http.Identity("create"), "/api/identity/accounts", new { ECODE = "history-delete", ENAME = "删除历史账号" });
        userId = deletable.GetProperty("account").GetProperty("eid").GetString();
        route = $"/api/authorization/users/{userId}/roles";
        foreach (var roles in new[] { new[] { "Assigned", "Other" }, new[] { "Assigned" } })
            Assert.Equal(HttpStatusCode.NoContent, (await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put, route, new { RoleCodes = roles })).StatusCode);
        using var deleted = await FeatureHttpTestRequests.SendAsync(admin, http.Identity("delete"), HttpMethod.Post, $"/api/identity/accounts/{userId}/delete", new { ExpectedRevision = deletable.GetProperty("account").GetProperty("revision").GetInt64(), Reason = "测试删除" });
        Assert.True(deleted.IsSuccessStatusCode, await deleted.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put, route, new { RoleCodes = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Get, route)).StatusCode);
        var retained = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), $"/api/audit/business-changes?TargetId={userId}");
        Assert.Equal(2, retained.GetProperty("total").GetInt32());
        Assert.All(retained.GetProperty("items").EnumerateArray(), r => Assert.Equal(JsonValueKind.Null, r.GetProperty("TargetId_DisplayValue").ValueKind));
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "并发用户角色保存记录真实前驱，空关系集合也被保护")]
    public async Task UserRoles_ConcurrentReplacementsRecordActualPreviousSet()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        using var accountResponse = await FeatureHttpTestRequests.SendAsync(admin, new FeatureHttpTestRequests(fixture).Identity("create"), HttpMethod.Post, "/api/identity/accounts", new { ECODE = "race-user", ENAME = "并发账号" });
        var account = await accountResponse.Content.ReadFromJsonAsync<JsonElement>();
        var userId = account.GetProperty("account").GetProperty("eid").GetString();
        foreach (var code in new[] { "First", "Second" }) Assert.True((await FeatureHttpTestRequests.SendAsync(admin, http.Role("create"), HttpMethod.Post, "/api/authorization/roles", new { Code = code, Name = code })).IsSuccessStatusCode);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Fault.AfterRegister = async (change, token) => {
            if (change.EventType == "user.roles.changed" && change.Fields.Count == 1) { entered.TrySetResult(); await release.Task.WaitAsync(token); }
        };
        var route = $"/api/authorization/users/{userId}/roles";
        var first = FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put, route, new { RoleCodes = new[] { "First" } });
        Task<HttpResponseMessage>? second = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put, route, new { RoleCodes = new[] { "Second" } });
            await fixture.Fault.ConcurrentCommand.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(second.IsCompleted);
        }
        finally { release.TrySetResult(); }
        Assert.Equal(HttpStatusCode.NoContent, (await first).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await second!).StatusCode);
        var records = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), $"/api/audit/business-changes?TargetId={userId}");
        Assert.Equal(2, records.GetProperty("total").GetInt32());
        var replacement = records.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("Changes").GetArrayLength() == 2);
        Assert.Contains(replacement.GetProperty("Changes").EnumerateArray(), c => c.GetProperty("beforeDisplay").GetString() == "First" && c.GetProperty("after").GetProperty("value").ValueKind == JsonValueKind.Null);
        var current = await FeatureHttpTestRequests.ReadAsync(admin, http.Role("userRoles"), route);
        Assert.Equal("Second", Assert.Single(current.GetProperty("roleCodes").EnumerateArray()).GetString());
    }
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "普通用户角色校验失败不得应用部分集合")]
    public async Task UserRoles_UnknownAccountRejectsCompleteRequest()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        using var response = await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put, "/api/authorization/users/missing-account/roles", new { RoleCodes = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "当前管理员角色资格独立于Identity展示与保护标记")]
    public async Task UserRoles_InternalAdministratorMemberCannotBeRemovedByOrdinaryReplacement()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        using var response = await FeatureHttpTestRequests.SendAsync(admin, new FeatureHttpTestRequests(fixture).Identity("create"), HttpMethod.Post, "/api/identity/accounts", new { ECODE = "internal-member", ENAME = "普通显示账号" });
        var account = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(account.GetProperty("account").GetProperty("isLocalAdministrator").GetBoolean());
        var id = account.GetProperty("account").GetProperty("eid").GetString()!;
        await fixture.AssignInternalAdministratorAsync(id);
        foreach (var codes in new[] { Array.Empty<string>(), new[] { "LocalAdmin" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put, $"/api/authorization/users/{id}/roles", new { RoleCodes = codes })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Get, $"/api/authorization/users/{id}/roles")).StatusCode);
        await HttpTestActors.PrepareOrganizationAsync(admin, http, account.GetProperty("account"));
        using var member = await fixture.LoginAsync("internal-member", account.GetProperty("temporaryCredential").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await FeatureHttpTestRequests.SendAsync(member, http.Role(), HttpMethod.Get, "/api/authorization/roles")).StatusCode);
        var records = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), $"/api/audit/business-changes?TargetId={id}");
        Assert.Empty(records.GetProperty("items").EnumerateArray());
    }
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "旧Permission目录、分配和任意用户判权API退出，认证响应只投影功能位置")]
    public async Task RetiredPermissionApis_AreUnavailableToAdministrator()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        foreach (var path in new[] { "/api/authorization/permissions", "/api/authorization/permissions/grouped",
            "/api/authorization/roles/Role_missing/permissions", "/api/authorization/users/User_missing/permissions" })
        {
            using var response = await admin.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using (var response = await admin.PutAsJsonAsync("/api/authorization/roles/Role_missing/permissions", new { PermissionCodes = new[] { "Legacy.Any" } }))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using (var response = await admin.PostAsJsonAsync("/api/authorization/users/User_missing/check-permission", new { PermissionCode = "Legacy.Any" }))
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var current = await admin.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        Assert.False(current.TryGetProperty("permissions", out _));
        Assert.NotEmpty(current.GetProperty("featurePositions").EnumerateArray());
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "人员更新只登记实际规范差异并保留空值和原姓名")]
    public async Task PersonUpdate_RecordsActualChangedFieldsAndIgnoresUnchangedValues()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var client = await fixture.LoginAdministratorAsync();
        var company = await FeatureHttpTestRequests.CreateAsync(client, http.Organization("companies", "create", "list.pageActions"), "/api/organization/companies", new { ECODE = "EDIT", ENAME = "编辑公司", SortOrder = "0100" });
        var a = await FeatureHttpTestRequests.CreateAsync(client, http.Organization("departments", "create", "list.pageActions"), "/api/organization/departments", new { ECODE = "EDIT-A", ENAME = "部门A", CompanyId = Id(company), SortOrder = "0100" });
        var b = await FeatureHttpTestRequests.CreateAsync(client, http.Organization("departments", "create", "list.pageActions"), "/api/organization/departments", new { ECODE = "EDIT-B", ENAME = "部门B", CompanyId = Id(company), SortOrder = "0100" });
        var person = await FeatureHttpTestRequests.CreateAsync(client, http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons", new { PersonNo = "EDIT-1", ENAME = "旧姓名", CompanyId = Id(company), DepartmentId = Id(a), BirthDate = "2000-02-29", EmploymentStatus = "Active", IsEnabled = true });
        var edit = new { ENAME = "新姓名", CompanyId = Id(company), DepartmentId = Id(b), BirthDate = (string?)null, EmploymentStatus = "Active", EmploymentStartedAt = "2026-01-01T01:02:03Z" };
        await UpdateAsync(client, http.Organization("persons", "edit", "list.rowActions"), Id(person), edit);
        await UpdateAsync(client, http.Organization("persons", "edit", "list.rowActions"), Id(person), edit);
        // 等价时间表达规范化为相同 UTC 时间；四字段变化不能生成业务记录。
        await UpdateAsync(client, http.Organization("persons", "edit", "list.rowActions"), Id(person), new { EmploymentStartedAt = "2026-01-01T09:02:03" });
        var page = await FeatureHttpTestRequests.ReadAsync(client, http.Audit(), $"/api/audit/business-changes?TargetId={Id(person)}&EventType=person.updated");
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
        using var renamed = await FeatureHttpTestRequests.SendAsync(client, http.Organization("departments", "edit", "list.rowActions"), HttpMethod.Put, "/api/organization/departments/" + Id(b), new { ENAME = "当前部门B" });
        Assert.True(renamed.IsSuccessStatusCode, await renamed.Content.ReadAsStringAsync());
        var currentNames = await FeatureHttpTestRequests.ReadAsync(client, http.Audit(), $"/api/audit/business-changes/{row.GetProperty("EID").GetString()}");
        var departmentChange = Assert.Single(currentNames.GetProperty("fields").GetProperty("Changes").EnumerateArray(), c => c.GetProperty("field").GetString() == "DepartmentId");
        Assert.Equal("当前部门B", departmentChange.GetProperty("afterDisplay").GetString());
        fixture.Fault.Mode = "names";
        var degraded = await FeatureHttpTestRequests.ReadAsync(client, http.Audit(), $"/api/audit/business-changes/{row.GetProperty("EID").GetString()}");
        var degradedDepartment = Assert.Single(degraded.GetProperty("fields").GetProperty("Changes").EnumerateArray(), c => c.GetProperty("field").GetString() == "DepartmentId");
        Assert.Equal(Id(a), degradedDepartment.GetProperty("beforeDisplay").GetString());
        Assert.Equal(Id(b), degradedDepartment.GetProperty("afterDisplay").GetString());
        fixture.Fault.Mode = "";
    }

    internal static async Task UpdateAsync(HttpClient client, FeaturePoint point, string id, object value)
    {
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        using var response = await FeatureHttpTestRequests.SendAsync(client, point, HttpMethod.Put, "/api/organization/persons/" + id, value);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "人员更新登记及持久化失败共同回滚，拒绝输入不留成功记录")]
    public async Task PersonUpdate_FailuresAndRejectedFieldsLeaveBothStoresUnchanged()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var client = await fixture.LoginAdministratorAsync();
        var person = await FeatureHttpTestRequests.CreateAsync(client, http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons", new { PersonNo = "FAIL-UPDATE", ENAME = "原姓名", EmploymentStatus = "Active", IsEnabled = true });
        foreach (var mode in new[] { "register", "write", "save", "commit" })
        {
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            fixture.Fault.Mode = mode;
            using var response = await FeatureHttpTestRequests.SendAsync(client, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put, "/api/organization/persons/" + Id(person), new { ENAME = "不应提交" });
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            fixture.Fault.Mode = "";
            var actual = await FeatureHttpTestRequests.ReadAsync(client, http.Organization("persons"), "/api/organization/persons/" + Id(person));
            Assert.Equal("原姓名", actual.GetProperty("fields").GetProperty("ENAME").GetString());
            var records = await FeatureHttpTestRequests.ReadAsync(client, http.Audit(), $"/api/audit/business-changes?TargetId={Id(person)}");
            Assert.Equal(1, records.GetProperty("total").GetInt32());
        }
        foreach (var invalid in new object[] { new { UpdatedBy = "client-actor" }, new { CompanyId = "missing-company" }, new { BirthDate = "2999-01-01" }, new { EmploymentStartedAt = "2026-09-10T00:00:00Z", EmploymentEndedAt = "2026-09-09T00:00:00Z" } })
        {
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            using var response = await FeatureHttpTestRequests.SendAsync(client, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put, "/api/organization/persons/" + Id(person), invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.NotEmpty(problem.GetProperty("fieldErrors").EnumerateArray());
        }
        // 启停不属于普通编辑字段；客户端塞入的字段不会扩大事件白名单。
        await UpdateAsync(client, http.Organization("persons", "edit", "list.rowActions"), Id(person), new { PersonNo = "OTHER", IsEnabled = false, Arbitrary = "ignored" });
        var unchanged = await FeatureHttpTestRequests.ReadAsync(client, http.Organization("persons"), "/api/organization/persons/" + Id(person));
        Assert.True(unchanged.GetProperty("fields").GetProperty("IsEnabled").GetBoolean());
        Assert.Equal("FAIL-UPDATE", unchanged.GetProperty("fields").GetProperty("PersonNo").GetString());
        var final = await FeatureHttpTestRequests.ReadAsync(client, http.Audit(), $"/api/audit/business-changes?TargetId={Id(person)}");
        Assert.Equal(1, final.GetProperty("total").GetInt32());
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "并发人员编辑记录真实替换序列而非事务外快照")]
    public async Task PersonUpdate_ConcurrentRequestsRecordTheActualReplacementSequence()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var client = await fixture.LoginAdministratorAsync();
        var person = await FeatureHttpTestRequests.CreateAsync(client, http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons", new { PersonNo = "RACE-UPDATE", ENAME = "起点", EmploymentStatus = "Active", IsEnabled = true });
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
        var first = FeatureHttpTestRequests.SendAsync(client, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put, "/api/organization/persons/" + Id(person), new { ENAME = "第一位" });
        Task<HttpResponseMessage>? second = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = FeatureHttpTestRequests.SendAsync(client, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put, "/api/organization/persons/" + Id(person), new { ENAME = "第二位" });
            // 第一笔仍未保存；第二请求已到数据库边界，再给出真实 SQL 执行窗口。
            await fixture.Fault.ConcurrentCommand.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(200);
        }
        finally { release.TrySetResult(); }
        using var firstResult = await first;
        using var secondResult = await second!;
        Assert.True(firstResult.IsSuccessStatusCode, await firstResult.Content.ReadAsStringAsync());
        Assert.True(secondResult.IsSuccessStatusCode, await secondResult.Content.ReadAsStringAsync());
        var page = await FeatureHttpTestRequests.ReadAsync(client, http.Audit(), $"/api/audit/business-changes?TargetId={Id(person)}&EventType=person.updated");
        var changes = page.GetProperty("items").EnumerateArray().Select(r => Assert.Single(r.GetProperty("Changes").EnumerateArray())).ToArray();
        Assert.Equal(2, changes.Length);
        var firstChange = Assert.Single(changes, c => c.GetProperty("after").GetProperty("value").GetString() == "第一位");
        var secondChange = Assert.Single(changes, c => c.GetProperty("after").GetProperty("value").GetString() == "第二位");
        Assert.Equal("起点", firstChange.GetProperty("before").GetProperty("value").GetString());
        Assert.Equal("第一位", secondChange.GetProperty("before").GetProperty("value").GetString());
        var final = await FeatureHttpTestRequests.ReadAsync(client, http.Organization("persons"), "/api/organization/persons/" + Id(person));
        Assert.Equal("第二位", final.GetProperty("fields").GetProperty("ENAME").GetString());
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实登录人员创建与 Audit 同 SQL 事务提交")]
    public async Task PersonCreation_CommitsTypedRecordAndAdministratorCanReadIt()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var client = await fixture.LoginAdministratorAsync();
        var company = await FeatureHttpTestRequests.CreateAsync(client, http.Organization("companies", "create", "list.pageActions"), "/api/organization/companies", new { ECODE = "AUDIT", ENAME = "审计公司", SortOrder = "0100" });
        var department = await FeatureHttpTestRequests.CreateAsync(client, http.Organization("departments", "create", "list.pageActions"), "/api/organization/departments", new { ECODE = "AUDIT-A", ENAME = "部门A", CompanyId = Id(company), SortOrder = "0100" });
        var created = await FeatureHttpTestRequests.CreateAsync(client, http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons", new
        {
            PersonNo = "AUDIT-001", ENAME = "初始姓名", CompanyId = Id(company), DepartmentId = Id(department),
            BirthDate = "2000-02-29", EmploymentStatus = "Active", EmploymentStartedAt = "2026-01-01T01:02:03Z", IsEnabled = true
        });
        var personId = Id(created);
        var createdFields = created.GetProperty("fields");
        var createdAt = createdFields.GetProperty("CreatedAt").GetDateTime();
        Assert.Equal(fixture.ActorId, createdFields.GetProperty("CreatedBy").GetString());
        Assert.Equal(fixture.ActorId, createdFields.GetProperty("UpdatedBy").GetString());
        Assert.Equal(createdAt, createdFields.GetProperty("UpdatedAt").GetDateTime());
        Assert.EndsWith("Z", createdFields.GetProperty("CreatedAt").GetString());
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var stored = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Person"))
                .AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "EID") == personId);
            var cached = (await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync()).Persons[personId];
            foreach (var field in new[] { "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" })
            {
                Assert.Equal(stored[field], cached[field]);
            }
            Assert.Equal(createdAt, stored["CreatedAt"]);
            Assert.Equal(createdAt, stored["UpdatedAt"]);
            Assert.Equal(fixture.ActorId, stored["CreatedBy"]);
            Assert.Equal(fixture.ActorId, stored["UpdatedBy"]);
        }
        var page = await FeatureHttpTestRequests.ReadAsync(client, http.Audit(), "/api/audit/business-changes?Resource=organization.person&TargetId=" + Id(created));
        var row = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("person.created", row.GetProperty("EventType").GetString());
        Assert.Equal(fixture.ActorId, row.GetProperty("ActorId").GetString());
        Assert.EndsWith("Z", row.GetProperty("OccurredAt").GetString());
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("TraceId").GetString()));
        var detail = await FeatureHttpTestRequests.ReadAsync(client, http.Audit(), "/api/audit/business-changes/" + row.GetProperty("EID").GetString());
        var changes = detail.GetProperty("fields").GetProperty("Changes").EnumerateArray().ToDictionary(c => c.GetProperty("field").GetString()!);
        Assert.Equal(10, changes.Count);
        Assert.Equal("2000-02-29", changes["BirthDate"].GetProperty("after").GetProperty("value").GetString());
        Assert.Equal("2026-01-01T01:02:03.0000000Z", changes["EmploymentStartedAt"].GetProperty("after").GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, changes["EmploymentEndedAt"].GetProperty("after").GetProperty("value").ValueKind);
        Assert.Equal("AUDIT-001", changes["PersonNo"].GetProperty("after").GetProperty("value").GetString());
        Assert.Equal("true", changes["IsEnabled"].GetProperty("after").GetProperty("value").GetString());
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "人员正式服务端调用与 Standard CRUD 消费同一对象行为")]
    public async Task PersonCreation_DirectObjectOperationCommitsPersonAuditAndCache()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        using var activity = new Activity(nameof(PersonCreation_DirectObjectOperationCommitsPersonAuditAndCache)).Start();
        var result = await scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>().ExecuteAsync(
            "organization.person",
            ObjectOperationAction.Create,
            null,
            new Dictionary<string, object?>
            {
                ["PersonNo"] = "DIRECT-OBJECT",
                ["ENAME"] = "服务端创建",
                ["EmploymentStatus"] = "Active",
                ["IsEnabled"] = true
            });

        Assert.True(result.Succeeded, result.Error?.Message);
        var personId = Convert.ToString(result.Detail!.Fields["EID"])!;
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var stored = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Person"))
            .AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "EID") == personId);
        var cached = (await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync()).Persons[personId];
        Assert.Equal(stored.OrderBy(static pair => pair.Key), cached.OrderBy(static pair => pair.Key));
        var audit = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("BusinessChange"))
            .AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "TargetId") == personId);
        Assert.Equal("person.created", audit["EventType"]);
        Assert.Equal(SystemEntityAuditStampProvider.SystemActorId, stored["CreatedBy"]);
        Assert.Equal(stored["CreatedAt"], stored["UpdatedAt"]);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "人员生命周期与永久绑定由正式 HTTP 业务服务提交并发布缓存")]
    public async Task PersonBusinessServices_HttpPreservesBindingLifecycleAuditAndCache()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var client = await fixture.LoginAdministratorAsync();
        using var accountResponse = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Identity("create"),
            HttpMethod.Post,
            "/api/identity/accounts",
            new { ECODE = "person-special-action", ENAME = "人员专用动作账号" });
        Assert.Equal(HttpStatusCode.Created, accountResponse.StatusCode);
        var accountEnvelope = await accountResponse.Content.ReadFromJsonAsync<JsonElement>();
        var account = accountEnvelope.GetProperty("account");
        var person = await FeatureHttpTestRequests.CreateAsync(
            client,
            http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons",
            new { PersonNo = "SPECIAL-ACTION", ENAME = "人员专用动作", EmploymentStatus = "Active", IsEnabled = true });
        var personId = Id(person);
        var createdFields = person.GetProperty("fields");

        using var binding = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Organization("persons", "linkUser", "detail.custom"),
            HttpMethod.Put,
            $"/api/organization/persons/{personId}/user-binding",
            new
            {
                accountEid = account.GetProperty("eid").GetString(),
                expectedRevision = account.GetProperty("revision").GetInt64(),
                reason = "原始绑定原因"
            });
        Assert.Equal(HttpStatusCode.OK, binding.StatusCode);
        var bindingResult = await binding.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("原始绑定原因", bindingResult.GetProperty("reason").GetString());

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var storedAfterBinding = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Person"))
            .AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "EID") == personId);
        var cachedAfterBinding = (await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync()).Persons[personId];
        Assert.Equal("原始绑定原因", storedAfterBinding["AccountBindingReason"]);
        Assert.Equal(createdFields.GetProperty("CreatedBy").GetString(), storedAfterBinding["CreatedBy"]);
        Assert.Equal(createdFields.GetProperty("CreatedAt").GetDateTime(), storedAfterBinding["CreatedAt"]);
        foreach (var field in new[] { "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" })
        {
            Assert.Equal(storedAfterBinding[field], cachedAfterBinding[field]);
        }

        using var disabled = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Organization("persons", "disable", "list.rowActions"),
            HttpMethod.Put,
            $"/api/organization/persons/{personId}/status/disable",
            new { isEnabled = false, reason = "正式停用" });
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        var disabledResult = await disabled.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(disabledResult.GetProperty("isEnabled").GetBoolean());
        Assert.False((bool)(await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync()).Persons[personId]["IsEnabled"]!);

        using var enabled = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Organization("persons", "enable", "list.rowActions"),
            HttpMethod.Put,
            $"/api/organization/persons/{personId}/status/enable",
            new { isEnabled = true, reason = "正式恢复" });
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        var restored = await enabled.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(restored.GetProperty("isEnabled").GetBoolean());
        Assert.Equal("Disabled", restored.GetProperty("account").GetProperty("status").GetString());
        Assert.False(restored.GetProperty("account").GetProperty("personnelDisableHold").GetBoolean());

        using var rejectedAccountResponse = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Identity("create"),
            HttpMethod.Post,
            "/api/identity/accounts",
            new { ECODE = "person-disabled-bind", ENAME = "停用人员候选账号" });
        Assert.Equal(HttpStatusCode.Created, rejectedAccountResponse.StatusCode);
        var rejectedAccount = (await rejectedAccountResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("account");
        var disabledPerson = await FeatureHttpTestRequests.CreateAsync(
            client,
            http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons",
            new { PersonNo = "DISABLED-BIND", ENAME = "停用后拒绝绑定", EmploymentStatus = "Active", IsEnabled = true });
        var disabledPersonId = Id(disabledPerson);
        using var disabledPersonResponse = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Organization("persons", "disable", "list.rowActions"),
            HttpMethod.Put,
            $"/api/organization/persons/{disabledPersonId}/status/disable",
            new { isEnabled = false, reason = "验证正式拒绝契约" });
        Assert.Equal(HttpStatusCode.OK, disabledPersonResponse.StatusCode);

        using var rejectedBinding = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Organization("persons", "linkUser", "detail.custom"),
            HttpMethod.Put,
            $"/api/organization/persons/{disabledPersonId}/user-binding",
            new
            {
                accountEid = rejectedAccount.GetProperty("eid").GetString(),
                expectedRevision = rejectedAccount.GetProperty("revision").GetInt64(),
                reason = "停用人员不得绑定"
            });
        Assert.Equal(HttpStatusCode.Conflict, rejectedBinding.StatusCode);
        var rejection = await rejectedBinding.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(PersonUserBindingFailureCodes.PersonDisabled, rejection.GetProperty("code").GetString());
        var rejectedPersonStored = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Person"))
            .AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "EID") == disabledPersonId);
        var rejectedAccountStored = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("IdentityAccount"))
            .AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "EID") == rejectedAccount.GetProperty("eid").GetString());
        Assert.Null(rejectedPersonStored["AccountEID"]);
        Assert.False((bool)rejectedAccountStored["IsPersonnelBound"]!);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "人员服务端更新和别名删除共用对象行为、审计与实际EID缓存登记")]
    public async Task PersonUpdateAndDelete_DirectObjectOperationsUseRulesAuditAndCanonicalCacheIdentity()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        using var activity = new Activity(nameof(PersonUpdateAndDelete_DirectObjectOperationsUseRulesAuditAndCanonicalCacheIdentity)).Start();
        var operations = scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>();
        var control = fixture.Services.GetRequiredService<ObjectOperationExtensionProbeControl>();
        var created = await operations.ExecuteAsync(
            "organization.person",
            ObjectOperationAction.Create,
            null,
            new Dictionary<string, object?>
            {
                ["PersonNo"] = "DIRECT-UPDATE-DELETE",
                ["ENAME"] = "修改前",
                ["EmploymentStatus"] = "Active",
                ["IsEnabled"] = true
            });
        var personId = Convert.ToString(created.Detail!.Fields["EID"])!;
        var createdBy = created.Detail.Fields["CreatedBy"];
        var createdAt = (DateTime)created.Detail.Fields["CreatedAt"]!;
        var alias = personId.ToLowerInvariant();
        Assert.False(string.Equals(personId, alias, StringComparison.Ordinal));

        control.Mode = ObjectOperationExtensionProbeMode.InvalidFinalInput;
        var rejected = await operations.ExecuteAsync(
            "organization.person",
            ObjectOperationAction.Update,
            alias,
            new Dictionary<string, object?> { ["ENAME"] = "原本合法" });
        Assert.False(rejected.Succeeded);
        Assert.Equal("Organization.Person.NameRequired", rejected.Error?.Code);
        Assert.Equal("修改前", (await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync()).Persons[personId]["ENAME"]);

        control.Mode = ObjectOperationExtensionProbeMode.Supplement;
        control.SupplementedName = "修改后";
        var updated = await operations.ExecuteAsync(
            "organization.person",
            ObjectOperationAction.Update,
            alias,
            new Dictionary<string, object?> { ["ENAME"] = "调用方姓名" });

        Assert.True(updated.Succeeded, updated.Error?.Message);
        Assert.Equal(personId, updated.Detail!.Fields["EID"]);
        Assert.Equal("修改后", updated.Detail.Fields["ENAME"]);
        Assert.Equal(SystemEntityAuditStampProvider.SystemActorId, updated.Detail.Fields["UpdatedBy"]);
        Assert.Equal(createdBy, updated.Detail.Fields["CreatedBy"]);
        Assert.Equal(createdAt, updated.Detail.Fields["CreatedAt"]);
        Assert.True((DateTime)updated.Detail.Fields["UpdatedAt"]! >= createdAt);
        Assert.Equal(alias, control.TargetId);
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var storedAfterUpdate = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Person"))
            .AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "EID") == personId);
        var audit = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("BusinessChange"))
            .AsNoTracking()
            .Where(row => EF.Property<string>(row, "TargetId") == personId)
            .ToListAsync();
        Assert.Single(audit, row => Equals(row["EventType"], "person.updated"));
        var cached = (await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync()).Persons;
        Assert.Equal("修改后", cached[personId]["ENAME"]);
        Assert.False(cached.ContainsKey(alias));
        foreach (var field in new[] { "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" })
        {
            Assert.Equal(updated.Detail.Fields[field], storedAfterUpdate[field]);
            Assert.Equal(storedAfterUpdate[field], cached[personId][field]);
        }
        var conditionalDelete = scope.ServiceProvider
            .GetServices<IStandardCrudConditionalDeleteExecutor>()
            .Single(executor => executor.ResourceKey == "organization.person");

        control.Mode = ObjectOperationExtensionProbeMode.AfterFailure;
        await Assert.ThrowsAsync<InvalidOperationException>(() => conditionalDelete.DeleteAsync(
            PersonCrudDescriptorFactory.Create(),
            alias));
        Assert.True(await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Person"))
            .AsNoTracking()
            .AnyAsync(row => EF.Property<string>(row, "EID") == personId));
        Assert.True((await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync()).Persons.ContainsKey(personId));

        control.Mode = ObjectOperationExtensionProbeMode.None;
        var deleted = await conditionalDelete.DeleteAsync(PersonCrudDescriptorFactory.Create(), alias);

        Assert.True(deleted.Succeeded, deleted.Error?.Message);
        Assert.False(await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Person"))
            .AsNoTracking()
            .AnyAsync(row => EF.Property<string>(row, "EID") == personId));
        Assert.False((await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync()).Persons.ContainsKey(personId));
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "公司与岗位服务端完整 CRUD 共用对象行为、扩展、审计与实际 EID 缓存登记")]
    public async Task CompanyAndPositionCrud_DirectObjectOperationsUseExtensionsAuditAndCanonicalCacheIdentity()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        using var activity = new Activity(nameof(CompanyAndPositionCrud_DirectObjectOperationsUseExtensionsAuditAndCanonicalCacheIdentity)).Start();
        var operations = scope.ServiceProvider.GetRequiredService<IObjectOperationExecutor>();
        var control = fixture.Services.GetRequiredService<ObjectOperationExtensionProbeControl>();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var cache = fixture.Services.GetRequiredService<OrganizationCache>();

        foreach (var target in new[]
        {
            new ObjectOperationTarget(
                "organization.company",
                "Company",
                CompanyCrudDescriptorFactory.Create(),
                new Dictionary<string, object?> { ["ECODE"] = "DIRECT-COMPANY", ["SortOrder"] = "0100" },
                new Dictionary<string, object?> { ["SortOrder"] = "0200" },
                "Organization.Company.NameRequired"),
            new ObjectOperationTarget(
                "organization.position",
                "Position",
                PositionCrudDescriptorFactory.Create(),
                new Dictionary<string, object?> { ["Description"] = "创建说明" },
                new Dictionary<string, object?> { ["Description"] = "更新说明" },
                "Organization.Position.NameRequired")
        })
        {
            control.Mode = ObjectOperationExtensionProbeMode.Supplement;
            control.SupplementedName = $"{target.ModelName} 扩展创建";
            var created = await operations.ExecuteAsync(
                target.ObjectKey,
                ObjectOperationAction.Create,
                null,
                target.CreateValues);

            Assert.True(created.Succeeded, created.Error?.Message);
            var id = Convert.ToString(created.Detail!.Fields["EID"])!;
            var alias = id.ToLowerInvariant();
            Assert.False(string.Equals(id, alias, StringComparison.Ordinal));
            Assert.Equal(control.SupplementedName, created.Detail.Fields["ENAME"]);

            control.Mode = ObjectOperationExtensionProbeMode.InvalidFinalInput;
            var rejected = await operations.ExecuteAsync(
                target.ObjectKey,
                ObjectOperationAction.Update,
                alias,
                new Dictionary<string, object?> { ["ENAME"] = "原本合法" });
            Assert.False(rejected.Succeeded);
            Assert.Equal(target.RequiredNameError, rejected.Error?.Code);
            Assert.Equal(
                control.SupplementedName,
                (await ReadCachedRecord(cache, target.ModelName, id))["ENAME"]);

            control.Mode = ObjectOperationExtensionProbeMode.Supplement;
            control.SupplementedName = $"{target.ModelName} 扩展更新";
            var updated = await operations.ExecuteAsync(
                target.ObjectKey,
                ObjectOperationAction.Update,
                alias,
                target.UpdateValues);

            Assert.True(updated.Succeeded, updated.Error?.Message);
            Assert.Equal(id, updated.Detail!.Fields["EID"]);
            Assert.Equal(control.SupplementedName, updated.Detail.Fields["ENAME"]);
            Assert.Equal(alias, control.TargetId);
            var stored = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey(target.ModelName))
                .AsNoTracking()
                .SingleAsync(row => EF.Property<string>(row, "EID") == id);
            var cached = await ReadCachedRecord(cache, target.ModelName, id);
            foreach (var field in new[] { "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" })
            {
                Assert.Equal(updated.Detail.Fields[field], stored[field]);
                Assert.Equal(stored[field], cached[field]);
            }

            var conditionalDelete = scope.ServiceProvider
                .GetServices<IStandardCrudConditionalDeleteExecutor>()
                .Single(executor => executor.ResourceKey == target.ObjectKey);
            control.Mode = ObjectOperationExtensionProbeMode.AfterFailure;
            await Assert.ThrowsAsync<InvalidOperationException>(() => conditionalDelete.DeleteAsync(
                target.Descriptor,
                alias));
            Assert.True(await db.Set<Dictionary<string, object?>>(db.ResolveModelKey(target.ModelName))
                .AsNoTracking()
                .AnyAsync(row => EF.Property<string>(row, "EID") == id));
            Assert.Equal(id, (await ReadCachedRecord(cache, target.ModelName, id))["EID"]);

            control.Mode = ObjectOperationExtensionProbeMode.None;
            var deleted = await conditionalDelete.DeleteAsync(target.Descriptor, alias);
            Assert.True(deleted.Succeeded, deleted.Error?.Message);
            Assert.False(await db.Set<Dictionary<string, object?>>(db.ResolveModelKey(target.ModelName))
                .AsNoTracking()
                .AnyAsync(row => EF.Property<string>(row, "EID") == id));
            Assert.False(await ContainsCachedRecord(cache, target.ModelName, id));
        }

        static async Task<IReadOnlyDictionary<string, object?>> ReadCachedRecord(
            OrganizationCache cache,
            string modelName,
            string id)
        {
            var snapshot = await cache.ReadAsync();
            return modelName == "Company" ? snapshot.Companies[id] : snapshot.Positions[id];
        }

        static async Task<bool> ContainsCachedRecord(OrganizationCache cache, string modelName, string id)
        {
            var snapshot = await cache.ReadAsync();
            return modelName == "Company"
                ? snapshot.Companies.ContainsKey(id)
                : snapshot.Positions.ContainsKey(id);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实模块扩展与人员创建在同一 SQL 事务内成功或共同回滚")]
    public async Task PersonCreation_FormalModuleExtensionsCommitOrRollbackPersonAuditAndCacheTogether()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var client = await fixture.LoginAdministratorAsync();
        var control = fixture.Services.GetRequiredService<ObjectOperationExtensionProbeControl>();

        control.Mode = ObjectOperationExtensionProbeMode.Supplement;
        control.SupplementedName = "正式扩展补充姓名";
        var created = await FeatureHttpTestRequests.CreateAsync(
            client,
            http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons",
            new { PersonNo = "EXT-SUCCESS", EmploymentStatus = "Active", IsEnabled = true });
        var personId = Id(created);
        Assert.Equal("正式扩展补充姓名", created.GetProperty("fields").GetProperty("ENAME").GetString());
        Assert.Equal("正式扩展补充姓名", control.AfterInput!["ENAME"]);
        Assert.Equal("正式扩展补充姓名", control.AfterResult!["ENAME"]);
        Assert.True(control.AfterMutationBlocked);

        control.Mode = ObjectOperationExtensionProbeMode.InvalidFinalInput;
        using var invalid = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Organization("persons", "create", "list.pageActions"),
            HttpMethod.Post,
            "/api/organization/persons",
            new { PersonNo = "EXT-INVALID", ENAME = "原始有效姓名", EmploymentStatus = "Active", IsEnabled = true });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        control.Mode = ObjectOperationExtensionProbeMode.Reject;
        using var rejected = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Organization("persons", "create", "list.pageActions"),
            HttpMethod.Post,
            "/api/organization/persons",
            new { PersonNo = "EXT-REJECT", ENAME = "扩展拒绝", EmploymentStatus = "Active", IsEnabled = true });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        control.Mode = ObjectOperationExtensionProbeMode.ProtectedInput;
        using var protectedFailure = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Organization("persons", "create", "list.pageActions"),
            HttpMethod.Post,
            "/api/organization/persons",
            new { PersonNo = "EXT-PROTECTED", ENAME = "保护字段", EmploymentStatus = "Active", IsEnabled = true });
        Assert.Equal(HttpStatusCode.InternalServerError, protectedFailure.StatusCode);

        control.Mode = ObjectOperationExtensionProbeMode.AfterFailure;
        using var afterFailure = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Organization("persons", "create", "list.pageActions"),
            HttpMethod.Post,
            "/api/organization/persons",
            new { PersonNo = "EXT-AFTER", ENAME = "后置失败", EmploymentStatus = "Active", IsEnabled = true });
        Assert.Equal(HttpStatusCode.InternalServerError, afterFailure.StatusCode);

        control.Mode = ObjectOperationExtensionProbeMode.None;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var people = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Person"))
            .AsNoTracking()
            .Where(row => EF.Property<string>(row, "PersonNo").StartsWith("EXT-"))
            .ToArrayAsync();
        var stored = Assert.Single(people);
        Assert.Equal(personId, stored["EID"]);
        Assert.Equal("正式扩展补充姓名", stored["ENAME"]);
        var audits = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("BusinessChange"))
            .AsNoTracking()
            .Where(row => EF.Property<string>(row, "Resource") == "organization.person")
            .ToArrayAsync();
        var audit = Assert.Single(audits);
        Assert.Equal(personId, audit["TargetId"]);
        var cache = await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync();
        var cached = Assert.Single(cache.Persons.Values, person =>
            Convert.ToString(person["PersonNo"])!.StartsWith("EXT-", StringComparison.Ordinal));
        Assert.Equal(personId, cached["EID"]);
        Assert.Equal("正式扩展补充姓名", cached["ENAME"]);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "公司HTTP经正式扩展嵌套创建部门与人员，提交前缓存隔离且各次审计值保持一致")]
    public async Task NestedCompanyDepartmentPersonCreation_UsesEffectiveSqlViewAndPublishesOneCacheCandidate()
    {
        var stamps = new SteppingEntityAuditStampProvider();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.RemoveAll<IEntityAuditStampProvider>();
            services.AddSingleton<IEntityAuditStampProvider>(stamps);
        });
        var http = new FeatureHttpTestRequests(fixture);
        using var client = await fixture.LoginAdministratorAsync();
        var control = fixture.Services.GetRequiredService<ObjectOperationExtensionProbeControl>();
        control.Mode = ObjectOperationExtensionProbeMode.NestedCreate;
        control.NestedWritesCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        control.ReleaseNestedWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var auditStart = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        stamps.Begin(fixture.ActorId, auditStart);
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);

        var request = FeatureHttpTestRequests.SendAsync(
            client,
            http.Organization("companies", "create", "list.pageActions"),
            HttpMethod.Post,
            "/api/organization/companies",
            new { ECODE = "NESTED-COMPANY", ENAME = "嵌套公司", SortOrder = "0100" });
        await control.NestedWritesCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var unpublished = await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync();
        Assert.DoesNotContain(control.NestedCompanyId!, unpublished.Companies.Keys);
        Assert.DoesNotContain(control.NestedDepartmentId!, unpublished.Departments.Keys);
        Assert.DoesNotContain(control.NestedPersonId!, unpublished.Persons.Keys);
        control.ReleaseNestedWrite.TrySetResult();
        using var response = await request;
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var companyResponse = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(control.NestedCompanyId, Id(companyResponse));

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var company = await ReadOrganizationRowAsync(db, "Company", control.NestedCompanyId!);
        var department = await ReadOrganizationRowAsync(db, "Department", control.NestedDepartmentId!);
        var person = await ReadOrganizationRowAsync(db, "Person", control.NestedPersonId!);
        Assert.Equal(company["EID"], department["CompanyId"]);
        Assert.Equal(company["EID"], person["CompanyId"]);
        Assert.Equal(department["EID"], person["DepartmentId"]);
        Assert.Equal(auditStart, company["CreatedAt"]);
        Assert.Equal(auditStart.AddMinutes(1), department["CreatedAt"]);
        Assert.Equal(auditStart.AddMinutes(2), person["CreatedAt"]);
        Assert.True(stamps.Reads >= 3);
        var published = await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync();
        AssertAuditMatches(company, companyResponse.GetProperty("fields"), published.Companies[control.NestedCompanyId!]);
        AssertAuditMatches(department, control.NestedDepartmentResult!.Detail!.Fields, published.Departments[control.NestedDepartmentId!]);
        AssertAuditMatches(person, control.NestedPersonResult!.Detail!.Fields, published.Persons[control.NestedPersonId!]);

        static async Task<Dictionary<string, object?>> ReadOrganizationRowAsync(
            EfPlatformDbContext db,
            string model,
            string id) => await db.Set<Dictionary<string, object?>>(db.ResolveModelKey(model))
            .AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "EID") == id);

        static void AssertAuditMatches(
            IReadOnlyDictionary<string, object?> stored,
            object? returned,
            IReadOnlyDictionary<string, object?> cached)
        {
            foreach (var field in new[] { "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" })
            {
                Assert.Equal(stored[field], cached[field]);
                if (returned is IReadOnlyDictionary<string, object?> fields)
                {
                    Assert.Equal(stored[field], fields[field]);
                }
                else if (returned is JsonElement element)
                {
                    if (field.EndsWith("By", StringComparison.Ordinal))
                    {
                        Assert.Equal(stored[field], element.GetProperty(field).GetString());
                    }
                    else
                    {
                        Assert.Equal(((DateTime)stored[field]!).ToString("O"), element.GetProperty(field).GetDateTime().ToString("O"));
                    }
                }
            }
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "前序部门修改或删除立即影响人员规则，嵌套失败被忽略仍整体回滚")]
    public async Task NestedDepartmentUpdateOrDelete_WhenLaterPersonFails_RollsBackWholeCompanyOperation()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var client = await fixture.LoginAdministratorAsync();
        var company = await FeatureHttpTestRequests.CreateAsync(
            client,
            http.Organization("companies", "create", "list.pageActions"),
            "/api/organization/companies",
            new { ECODE = "NESTED-ROLLBACK", ENAME = "回滚公司", SortOrder = "0100" });
        var department = await FeatureHttpTestRequests.CreateAsync(
            client,
            http.Organization("departments", "create", "list.pageActions"),
            "/api/organization/departments",
            new { ECODE = "NESTED-ROLLBACK", ENAME = "回滚部门", CompanyId = Id(company), SortOrder = "0100" });
        var control = fixture.Services.GetRequiredService<ObjectOperationExtensionProbeControl>();
        control.NestedCompanyId = Id(company);
        control.NestedDepartmentId = Id(department);

        control.Mode = ObjectOperationExtensionProbeMode.NestedDisableThenCreate;
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        using var disabled = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Organization("companies", "edit", "list.rowActions"),
            HttpMethod.Put,
            "/api/organization/companies/" + Id(company),
            new { ENAME = "不应提交的公司名" });
        Assert.Equal(HttpStatusCode.BadRequest, disabled.StatusCode);
        Assert.Equal("Organization.Person.DepartmentDisabled", control.NestedPersonResult?.Error?.Code);

        control.Mode = ObjectOperationExtensionProbeMode.None;
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var storedCompany = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Company"))
                .AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == Id(company));
            var storedDepartment = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Department"))
                .AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == Id(department));
            Assert.Equal("回滚公司", storedCompany["ENAME"]);
            Assert.True((bool)storedDepartment["IsEnabled"]!);
        }

        control.Mode = ObjectOperationExtensionProbeMode.NestedDeleteThenCreate;
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        using var deleted = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Organization("companies", "edit", "list.rowActions"),
            HttpMethod.Put,
            "/api/organization/companies/" + Id(company),
            new { ENAME = "仍不应提交" });
        Assert.Equal(HttpStatusCode.BadRequest, deleted.StatusCode);
        Assert.Equal("Organization.Person.DepartmentNotFound", control.NestedPersonResult?.Error?.Code);
        control.Mode = ObjectOperationExtensionProbeMode.None;
        var cache = await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync();
        Assert.Equal("回滚公司", cache.Companies[Id(company)]["ENAME"]);
        Assert.True((bool)cache.Departments[Id(department)]["IsEnabled"]!);
        await using var verification = fixture.Services.CreateAsyncScope();
        var verificationDb = verification.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.True(await verificationDb.Set<Dictionary<string, object?>>(verificationDb.ResolveModelKey("Department"))
            .AsNoTracking().AnyAsync(row => EF.Property<string>(row, "EID") == Id(department)));
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "删除 after 再调用同目标正式 adapter 必须进入嵌套入口并使根删除回滚")]
    public async Task CompanyDelete_AfterExtensionRepeatsDeclaredDelete_RollsBackRootDeletion()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var client = await fixture.LoginAdministratorAsync();
        var created = await FeatureHttpTestRequests.CreateAsync(
            client,
            http.Organization("companies", "create", "list.pageActions"),
            "/api/organization/companies",
            new { ECODE = "REPEAT-DELETE", ENAME = "重复删除公司", SortOrder = "0100" });
        var companyId = Id(created);
        var control = fixture.Services.GetRequiredService<ObjectOperationExtensionProbeControl>();
        control.Mode = ObjectOperationExtensionProbeMode.AfterRepeatDelete;
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);

        using var response = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Organization("companies", "delete", "list.rowActions"),
            HttpMethod.Delete,
            "/api/organization/companies/" + companyId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("StandardCrud.NotFound", control.RepeatedDeleteResult?.Error?.Code);
        control.Mode = ObjectOperationExtensionProbeMode.None;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.True(await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Company"))
            .AsNoTracking()
            .AnyAsync(row => EF.Property<string>(row, "EID") == companyId));
        Assert.True((await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync())
            .Companies.ContainsKey(companyId));
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "同事务人员停用后绑定读取当前 SQL 事实且吞掉内层拒绝仍回滚根操作")]
    public async Task NestedPersonDisableThenBind_WhenRejectionIsCaught_RollsBackRootOperation()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var client = await fixture.LoginAdministratorAsync();
        var company = await FeatureHttpTestRequests.CreateAsync(
            client,
            http.Organization("companies", "create", "list.pageActions"),
            "/api/organization/companies",
            new { ECODE = "SPECIAL-ROOT", ENAME = "专用动作根公司", SortOrder = "0100" });
        var person = await FeatureHttpTestRequests.CreateAsync(
            client,
            http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons",
            new { PersonNo = "SPECIAL-NESTED", ENAME = "嵌套专用动作人员", EmploymentStatus = "Active", IsEnabled = true });
        using var accountResponse = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Identity("create"),
            HttpMethod.Post,
            "/api/identity/accounts",
            new { ECODE = "special-nested", ENAME = "嵌套专用动作账号" });
        Assert.Equal(HttpStatusCode.Created, accountResponse.StatusCode);
        var account = (await accountResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("account");
        var control = fixture.Services.GetRequiredService<ObjectOperationExtensionProbeControl>();
        control.Mode = ObjectOperationExtensionProbeMode.NestedDisableThenBind;
        control.NestedPersonId = Id(person);
        control.NestedAccountId = account.GetProperty("eid").GetString();
        control.NestedAccountRevision = account.GetProperty("revision").GetInt64();
        control.ActorId = fixture.ActorId;
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);

        using var response = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Organization("companies", "edit", "list.rowActions"),
            HttpMethod.Put,
            "/api/organization/companies/" + Id(company),
            new { ENAME = "不应提交的公司名" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        control.Mode = ObjectOperationExtensionProbeMode.None;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var storedCompany = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Company"))
            .AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == Id(company));
        var storedPerson = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Person"))
            .AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == Id(person));
        var storedAccount = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("IdentityAccount"))
            .AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == control.NestedAccountId);
        Assert.Equal("专用动作根公司", storedCompany["ENAME"]);
        Assert.True((bool)storedPerson["IsEnabled"]!);
        Assert.Null(storedPerson["AccountEID"]);
        Assert.False((bool)storedAccount["IsPersonnelBound"]!);
        var cache = await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync();
        Assert.Equal("专用动作根公司", cache.Companies[Id(company)]["ENAME"]);
        Assert.True((bool)cache.Persons[Id(person)]["IsEnabled"]!);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "同事务 Person 创建、直接 DML 绑定和普通更新保持绑定及审计事实")]
    public async Task NestedPersonCreateBindUpdate_PreservesTrackedBindingAndAuditInDatabaseResultAndCache()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var client = await fixture.LoginAdministratorAsync();
        var company = await FeatureHttpTestRequests.CreateAsync(
            client,
            http.Organization("companies", "create", "list.pageActions"),
            "/api/organization/companies",
            new { ECODE = "TRACKED-ROOT", ENAME = "跟踪链根公司", SortOrder = "0100" });
        using var accountResponse = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Identity("create"),
            HttpMethod.Post,
            "/api/identity/accounts",
            new { ECODE = "tracked-special", ENAME = "跟踪链账号" });
        var account = (await accountResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("account");
        var control = fixture.Services.GetRequiredService<ObjectOperationExtensionProbeControl>();
        control.Mode = ObjectOperationExtensionProbeMode.NestedCreateBindUpdate;
        control.NestedCompanyId = null;
        control.NestedDepartmentId = null;
        control.NestedAccountId = account.GetProperty("eid").GetString();
        control.NestedAccountRevision = account.GetProperty("revision").GetInt64();
        control.ActorId = fixture.ActorId;
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);

        using var response = await FeatureHttpTestRequests.SendAsync(
            client,
            http.Organization("companies", "edit", "list.rowActions"),
            HttpMethod.Put,
            "/api/organization/companies/" + Id(company),
            new { ENAME = "跟踪链已提交" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(control.NestedPersonResult?.Succeeded);
        Assert.True(control.NestedPersonUpdateResult?.Succeeded);
        Assert.Equal(control.NestedAccountId, control.NestedBindingResult?.AccountEid);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var stored = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Person"))
            .AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == control.NestedPersonId);
        var cached = (await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync()).Persons[control.NestedPersonId!];
        Assert.Equal(control.NestedAccountId, stored["AccountEID"]);
        Assert.Equal("绑定后同事务更新", stored["ENAME"]);
        Assert.Equal(control.NestedAccountId, cached["AccountEID"]);
        Assert.Equal("绑定后同事务更新", cached["ENAME"]);
        foreach (var field in new[] { "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt" })
        {
            Assert.Equal(stored[field], control.NestedPersonUpdateResult!.Detail!.Fields[field]);
            Assert.Equal(stored[field], cached[field]);
        }
    }

    private sealed record ObjectOperationTarget(
        string ObjectKey,
        string ModelName,
        StandardCrudDescriptor Descriptor,
        IReadOnlyDictionary<string, object?> CreateValues,
        IReadOnlyDictionary<string, object?> UpdateValues,
        string RequiredNameError);

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "登记、写入、保存和确定未提交故障共同回滚")]
    public async Task PersonCreation_WhenParticipantOrStoreFails_RollsBackPersonAndRecord()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var client = await fixture.LoginAdministratorAsync();
        foreach (var mode in new[] { "register", "write", "save", "commit" })
        {
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            fixture.Fault.Mode = mode;
            using var failure = await FeatureHttpTestRequests.SendAsync(client, http.Organization("persons", "create", "list.pageActions"), HttpMethod.Post, "/api/organization/persons", new
                { PersonNo = "ROLLBACK-" + mode, ENAME = "故障人员", EmploymentStatus = "Active", IsEnabled = true });
            Assert.True(failure.StatusCode == HttpStatusCode.InternalServerError, mode);
            fixture.Fault.Mode = "";
            var people = await FeatureHttpTestRequests.ReadAsync(client, http.Organization("persons"), "/api/organization/persons?keyword=ROLLBACK");
            Assert.Equal(0, people.GetProperty("total").GetInt32());
            var records = await FeatureHttpTestRequests.ReadAsync(client, http.Audit(), "/api/audit/business-changes");
            Assert.Equal(0, records.GetProperty("total").GetInt32());
        }
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
        using var invalid = await FeatureHttpTestRequests.SendAsync(client, http.Organization("persons", "create", "list.pageActions"), HttpMethod.Post, "/api/organization/persons", new { PersonNo = "INVALID", ENAME = "校验失败", CompanyId = "absent", EmploymentStatus = "Active", IsEnabled = true });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(0, (await FeatureHttpTestRequests.ReadAsync(client, http.Audit(), "/api/audit/business-changes")).GetProperty("total").GetInt32());
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "管理员查询存储过滤稳定分页，普通账号不能用 View 绕过")]
    public async Task Query_FiltersInSqlAndKeepsAdministratorAdmissionAndFailures()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(
            fixture,
            admin,
            "AuditWriter",
            "记录验证职责",
            "writer",
            "人员操作者",
            http.Organization("persons"),
            http.Organization("persons", "create", "list.pageActions"),
            http.Audit());
        using var writer = actor.Client;
        var first = await FeatureHttpTestRequests.CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons", new { PersonNo = "QUERY-A", ENAME = "管理员创建", EmploymentStatus = "Active", IsEnabled = true });
        var second = await FeatureHttpTestRequests.CreateAsync(writer, http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons", new { PersonNo = "QUERY-B", ENAME = "维护者创建", EmploymentStatus = "Active", IsEnabled = true });
        using var deniedUpdate = await FeatureHttpTestRequests.SendAsync(writer, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put, "/api/organization/persons/" + Id(second), new { ENAME = "无权编辑" });
        Assert.Equal(HttpStatusCode.Forbidden, deniedUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await FeatureHttpTestRequests.SendAsync(writer, http.Audit(), HttpMethod.Get, "/api/audit/business-changes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/audit/business-changes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await FeatureHttpTestRequests.SendAsync(admin, http.Identity(), HttpMethod.Get, "/api/audit/business-changes")).StatusCode);
        var filtered = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), $"/api/audit/business-changes?Resource=organization.person&ActorId={actor.AccountId}&EventType=person.created&TargetId={Id(second)}&From=2020-01-01T00:00:00Z&To=2040-01-01T00:00:00Z");
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        var secondRecord = Assert.Single(filtered.GetProperty("items").EnumerateArray());
        Assert.Equal(actor.AccountId, secondRecord.GetProperty("ActorId").GetString());
        Assert.Equal("人员操作者", secondRecord.GetProperty("ActorId_DisplayValue").GetString());
        Assert.Equal("维护者创建", secondRecord.GetProperty("TargetId_DisplayValue").GetString());
        Assert.Equal(secondRecord.GetProperty("ActorId_DisplayValue").GetString(), secondRecord.GetProperty("CreatedBy_DisplayValue").GetString());
        foreach (var range in new[] { "From=invalid", "From=2026-01-01T00:00:00", "From=2040-01-01T00:00:00Z&To=2020-01-01T00:00:00Z" })
            Assert.Equal(HttpStatusCode.BadRequest, (await FeatureHttpTestRequests.SendAsync(admin, http.Audit(), HttpMethod.Get, "/api/audit/business-changes?" + range)).StatusCode);
        fixture.Fault.Commands.Clear();
        var page1 = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), "/api/audit/business-changes?Resource=organization.person&PageSize=1&pageIndex=1");
        var page2 = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), "/api/audit/business-changes?Resource=organization.person&PageSize=1&pageIndex=2");
        Assert.Equal(3, page1.GetProperty("total").GetInt32()); // 包含管理员为普通主体准备的真实人员创建。
        var row1 = Assert.Single(page1.GetProperty("items").EnumerateArray());
        var row2 = Assert.Single(page2.GetProperty("items").EnumerateArray());
        Assert.Equal(row1.GetProperty("OccurredAt").GetString(), row2.GetProperty("OccurredAt").GetString());
        Assert.True(string.CompareOrdinal(row1.GetProperty("EID").GetString(), row2.GetProperty("EID").GetString()) > 0);
        Assert.Contains(fixture.Fault.Commands, sql => sql.Contains("COUNT(*)") && sql.Contains("WHERE"));
        Assert.Contains(fixture.Fault.Commands, sql => sql.Contains("ORDER BY") && sql.Contains("[OccurredAt] DESC") && sql.Contains("[EID] DESC") && sql.Contains("OFFSET") && sql.Contains("FETCH NEXT"));
        fixture.Fault.Mode = "query";
        Assert.Equal(HttpStatusCode.InternalServerError, (await FeatureHttpTestRequests.SendAsync(admin, http.Audit(), HttpMethod.Get, "/api/audit/business-changes")).StatusCode);
        fixture.Fault.Mode = "names";
        var degraded = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), $"/api/audit/business-changes?TargetId={Id(first)}");
        Assert.Equal(Id(first), Assert.Single(degraded.GetProperty("items").EnumerateArray()).GetProperty("TargetId").GetString());
        fixture.Fault.Mode = "";
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(admin);
        Assert.Equal(HttpStatusCode.NoContent, (await FeatureHttpTestRequests.SendAsync(admin, http.Organization("persons", "delete", "list.rowActions"), HttpMethod.Delete, $"/api/organization/persons/{Id(second)}")).StatusCode);
        var retained = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), $"/api/audit/business-changes/{secondRecord.GetProperty("EID").GetString()}");
        Assert.Equal(Id(second), retained.GetProperty("fields").GetProperty("TargetId").GetString());
        Assert.Equal(JsonValueKind.Null, retained.GetProperty("fields").GetProperty("TargetId_DisplayValue").ValueKind);
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
        {
            using var request = new HttpRequestMessage(method, "/api/audit/business-changes" + (method == HttpMethod.Post ? "" : "/" + secondRecord.GetProperty("EID").GetString()));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await admin.SendAsync(request)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await FeatureHttpTestRequests.SendAsync(admin, http.Audit(), HttpMethod.Get, "/api/audit/business-changes/export")).StatusCode);
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

internal sealed class SteppingEntityAuditStampProvider : IEntityAuditStampProvider
{
    private readonly object _gate = new();
    private string _actorId = SystemEntityAuditStampProvider.SystemActorId;
    private DateTime _start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private bool _enabled;

    internal int Reads { get; private set; }

    // 在目标请求前重置可推进的可信 actor 与 UTC 时间序列。
    internal void Begin(string actorId, DateTime start)
    {
        lock (_gate)
        {
            _actorId = actorId;
            _start = start;
            Reads = 0;
            _enabled = true;
        }
    }

    /// <inheritdoc />
    public EntityAuditStamp GetCurrent()
    {
        lock (_gate)
        {
            if (!_enabled)
            {
                return new EntityAuditStamp(
                    SystemEntityAuditStampProvider.SystemActorId,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            }
            return new EntityAuditStamp(_actorId, _start.AddMinutes(Reads++));
        }
    }
}

internal sealed class BusinessAuditHostFixture : IAsyncDisposable
{
    private readonly string _connection;
    private readonly ReusableSqlServerDatabase _database;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "newp-audit-" + Guid.NewGuid().ToString("N"));
    private readonly Action<IServiceCollection>? _configureServices;
    private WebApplication _app = null!;
    internal StoreFault Fault { get; } = new();
    internal IServiceProvider Services => _app.Services;
    internal string StorageRoot => Path.Combine(_root, "storage");
    private string _temporaryCredential = "";
    internal string ActorId { get; private set; } = "";
    internal IReadOnlyList<FeaturePoint> FeatureCatalog { get; private set; } = [];
    private bool _useRequestPresentation;
    private bool _includeWorkspace;
    private bool _includePart;
    private bool _includeDocument;
    private bool _includeFiles;
    private bool _defaultOrganizationInitialized;
    private BusinessAuditHostFixture(ReusableSqlServerDatabase database, Action<IServiceCollection>? configureServices)
    {
        _database = database;
        _connection = database.ConnectionString;
        _configureServices = configureServices;
    }
    internal static async Task<BusinessAuditHostFixture> CreateAsync(Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, bool>? actionStates = null, bool useRequestPresentation = false, bool includeWorkspace = false, bool includePart = false, bool includeDocument = false, bool includeFiles = false)
    {
        var fixture = new BusinessAuditHostFixture(await ReusableSqlServerDatabase.AcquireAsync(allowParallel: true), configureServices);
        fixture._useRequestPresentation = useRequestPresentation;
        fixture._includeWorkspace = includeWorkspace || includePart || includeDocument;
        fixture._includePart = fixture._includeWorkspace;
        fixture._includeDocument = fixture._includeWorkspace;
        fixture._includeFiles = includeFiles || fixture._includeDocument;
        try
        {
            await fixture.StartAsync(actionStates: actionStates);
            using var scope = fixture._app.Services.CreateScope();
            var result = await scope.ServiceProvider.GetRequiredService<IIdentityLocalAdministratorMaintenanceService>().BootstrapAsync(new(
                "admin", "审计管理员", "审计集成准备", new(IdentityMaintenanceActors.LocalMaintenance, IdentitySecurityActorKind.LocalMaintenance, Guid.NewGuid().ToString("N"))));
            fixture.ActorId = result.Account.AccountEid;
            fixture._temporaryCredential = result.TemporaryCredential;
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }
    internal async Task RestartWithReadActionAsync(bool enabled)
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        await StartAsync(enabled);
    }
    internal async Task RestartWithActionAsync(string actionKey, bool enabled)
    {
        await RestartWithActionsAsync(new Dictionary<string, bool> { [actionKey] = enabled });
    }
    internal async Task RestartWithActionsAsync(IReadOnlyDictionary<string, bool> actionStates)
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        await StartAsync(actionStates: actionStates);
    }
    private async Task StartAsync(bool readEnabled = true,
        IReadOnlyDictionary<string, bool>? actionStates = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Host:KeyRingPath"] = Path.Combine(_root, "keys"),
            ["Authentication:Host:CookieName"] = "ICP.Platform.BusinessAuditTest",
            ["Authentication:Host:AllowInsecureDevelopmentCookie"] = "true",
            ["Files:StorageRoot"] = StorageRoot,
            ["Identity:Credentials:UseSharedDevelopmentCredential"] = "false",
            ["Logging:LogLevel:Default"] = "Warning"
        });
        var moduleAssemblies = new[]
        {
            typeof(IdentityModule).Assembly,
            typeof(AuthenticationModule).Assembly,
            typeof(AuthorizationModule).Assembly,
            typeof(AuditModule).Assembly,
            typeof(OrganizationModule).Assembly,
            typeof(ObjectOperationExtensionProbeModule).Assembly
        }.Concat(_includeWorkspace ? [typeof(ICP.Platform.Modules.Workspace.WorkspaceModule).Assembly] : []).Concat(_includePart ? [typeof(ICP.Platform.Modules.Part.PartModule).Assembly] : []).Concat(_includeDocument ? [typeof(ICP.Platform.Modules.Document.DocumentModule).Assembly] : []).Concat(_includeFiles ? [typeof(ICP.Platform.Modules.Files.FilesModule).Assembly] : []).ToArray();
        builder.Services.AddModuleSystem(options =>
        {
            foreach (var assembly in moduleAssemblies)
            {
                options.Assemblies.Add(assembly);
            }
        });
        var productionModuleIds = new[] { "Identity", "Authentication", "Authorization", "Audit", "Organization" }
            .Concat(_includeWorkspace ? ["InforCenter", "Workspace"] : []).Concat(_includePart ? ["Part"] : []).Concat(_includeDocument ? ["Document"] : []).Concat(_includeFiles ? ["Files"] : []).ToArray();
        var hostRoot = Path.GetDirectoryName(typeof(AuthController).Assembly.Location)!;
        var trust = TrustedBuiltInModuleSource.CreateForHost(hostRoot, Path.Combine(hostRoot, "modules"));
        var candidates = new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(trust)
            .Where(candidate => productionModuleIds.Contains(candidate.ModuleId, StringComparer.Ordinal)).ToArray();
        // 注册的测试扩展也必须拥有同一候选中的正式 Capsule，不能只靠 IModule 注册旁路依赖校验。
        var probeModulesRoot = Path.Combine(_root, "probe-capsules");
        var probeRoot = Path.Combine(probeModulesRoot, "ObjectOperationExtensionProbe");
        Directory.CreateDirectory(probeRoot);
        using (var source = typeof(ObjectOperationExtensionProbeModule).Assembly.GetManifestResourceStream(
                   "ICP.Platform.HostApi.Tests.ObjectOperationExtensionProbe.newp.plugin.json")!)
        await using (var target = File.Create(Path.Combine(probeRoot, "plugin.json")))
        {
            await source.CopyToAsync(target);
        }
        candidates = [.. candidates, .. new ModuleCapsuleCandidateReader().ReadBuiltIn(probeModulesRoot)];
        // 下一次启动的正式声明候选；不在活动目录中原位切换开关。
        var owner = Array.FindIndex(candidates, candidate => candidate.ModuleId == "Authorization");
        var authoring = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(hostRoot, "Authorization"), "plugin.json")))!;
        foreach (var action in authoring["dataActions"]!.AsArray())
        {
            var key = action!["key"]!.GetValue<string>();
            if (key == "read") action["enabled"] = readEnabled;
            if (actionStates?.TryGetValue(key, out var enabled) == true) action["enabled"] = enabled;
        }
        candidates[owner] = candidates[owner] with
        {
            DataActions = DataActionCatalogCompiler.Read(JsonSerializer.SerializeToElement(authoring), "Authorization"),
            Provenance = new ModulePlanProvenance("fixture-authoring", "Authorization/plugin.json", "/")
        };
        var compilation = new CanonicalModulePlanCompiler().Compile("personnel-authorization", candidates);
        var modulePlan = compilation.Plan ?? throw new InvalidOperationException("Formal module plan did not compile: " + JsonSerializer.Serialize(compilation.Diagnostics));
        FeatureCatalog = modulePlan.FeatureCatalog;
        builder.Services.AddSingleton(modulePlan);
        builder.Services.AddRelationTrees(modulePlan);
        builder.Services.AddPlatformAuthorization();
        builder.Services.AddAuthorization(options => { options.AddPolicy("AdminOnly", p => p.RequireRole("LocalAdmin")); options.AddPolicy(ICP.Platform.Authorization.Http.DenyAllAttribute.PolicyName, p => p.RequireAssertion(_ => false)); });
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
        // 所有分支都装配Audit履历查询，真实文本目录不能随业务模块选择而缺失。
        var localization = new ICP.Platform.Kernel.PluginPackages.LocalizationRuntimeManifestBuilder().Build(hostRoot);
        builder.Services.AddSingleton(ICP.Platform.HostApi.Presentation.ActiveLocalizationManifest.Parse(localization));
        builder.Services.AddSingleton<IRuntimeTextCatalog>(RuntimeTextCatalog.Parse(localization));
        if (_useRequestPresentation) builder.Services.Replace(ServiceDescriptor.Scoped<IRequestPresentationContextAccessor, ICP.Platform.HostApi.Presentation.HttpRequestPresentationContextAccessor>());
        builder.Services.AddScoped<ITemporalInputNormalizer>(sp => new TemporalInputNormalizer(sp.GetRequiredService<IRequestPresentationContextAccessor>(), "Asia/Shanghai", TimeProvider.System));
        var models = IdentityModelDefinitions.All.Select(m => m with { OwnerModuleId = "Identity" })
            .Concat(AuthenticationModelDefinitions.All.Select(m => m with { OwnerModuleId = "Authentication" }))
            .Concat(AuthorizationModelDefinitions.All.Select(m => m with { OwnerModuleId = "Authorization" }))
            .Concat(OrganizationModelDefinitions.All.Select(m => m with { OwnerModuleId = "Organization" }))
            .Concat(new AuditModelMetadataProvider().GetDefinitions().Select(m => m with { OwnerModuleId = "Audit" }))
            .Concat(_includeWorkspace ? new ICP.Platform.Modules.Workspace.WorkspaceModelMetadataProvider().GetDefinitions() : []).Concat(_includePart ? new ICP.Platform.Modules.Part.PartModelMetadataProvider().GetDefinitions() : []).Concat(_includeDocument ? new ICP.Platform.Modules.Document.DocumentModelMetadataProvider().GetDefinitions() : []).Concat(_includeFiles ? new ICP.Platform.Modules.Files.FilesModelMetadataProvider().GetDefinitions() : []).ToArray();
        // 本夹具显式构造当前模型计划；运行时模块不能再回退到仓库查找缺失的候选 authoring。
        var modelAuthoring = new Dictionary<string, ICP.Platform.Modeling.Metadata.ModelMetadataDocument>(StringComparer.Ordinal);
        if (_includePart) modelAuthoring.Add("Part", new ICP.Platform.Modules.Part.PartModelMetadataProvider().GetDocument());
        if (_includeDocument) modelAuthoring.Add("Document", new ICP.Platform.Modules.Document.DocumentModelMetadataProvider().GetDocument());
        var plan = new CompiledPersistencePlan("business-audit-test", "snapshot", "hash", default, "digest", [], models, []) { ModelAuthoring = modelAuthoring };
        builder.Services.AddSingleton(plan);
        builder.Services.AddDbContext<EfPlatformDbContext>((sp, options) => options.UseSqlServer(_connection)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().AddInterceptors(Fault, new SaveFault(Fault), new CommitFault(Fault)));
        builder.Services.AddControllers(options => { options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true; options.Filters.Add<AuthenticationSessionCookieResultFilter>(); })
            .AddApplicationPart(typeof(AuthController).Assembly).AddApplicationPart(typeof(IdentityModule).Assembly).AddApplicationPart(typeof(AuditModule).Assembly).AddApplicationPart(typeof(AuthorizationModule).Assembly).AddApplicationPart(typeof(OrganizationModule).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        // 名称故障注入放在稳定查询契约，避免数据库拦截器因缓存接入而不再触发。
        builder.Services.AddSingleton<ICP.Platform.Organization.Contracts.IOrganizationQuery>(sp =>
            new FaultedOrganizationQuery(sp.GetRequiredService<ICP.Platform.Modules.Organization.Caching.OrganizationCache>(), Fault));
        // 每次 Host 重启都复用首次配置；故障注入和 Files 选项不能随动作开关悄悄消失。
        _configureServices?.Invoke(builder.Services);
        if (_includeWorkspace) builder.Services.AddControllers().AddApplicationPart(typeof(ICP.Platform.Modules.Workspace.WorkspaceModule).Assembly);
        if (_includePart) builder.Services.AddControllers().AddApplicationPart(typeof(ICP.Platform.Modules.Part.PartModule).Assembly);
        if (_includeDocument) builder.Services.AddControllers().AddApplicationPart(typeof(ICP.Platform.Modules.Document.DocumentModule).Assembly);
        if (_includeFiles) builder.Services.AddControllers().AddApplicationPart(typeof(ICP.Platform.Modules.Files.FilesModule).Assembly);
        _app = builder.Build();
        _app.Use(async (context, next) => { using var activity = new Activity("business-audit-http").Start(); await next(); });
        _app.UseRouting();
        _app.UseRateLimiter();
        _app.UseAuthentication();
        _app.UseMiddleware<ICP.Platform.Authorization.Http.DataScopeEvaluationMiddleware>();
        if (_useRequestPresentation) _app.UseMiddleware<ICP.Platform.HostApi.Presentation.RequestPresentationContextMiddleware>();
        _app.UseHostAntiforgery();
        _app.UseMiddleware<RestrictedAuthenticationSessionMiddleware>();
        _app.UseAuthorization();
        _app.MapControllers();
        _app.MapStandardCrudResources();
        _app.MapRelationTrees();
        ICP.Platform.HostApi.Authorization.FeatureEndpointBindings.Validate(((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)_app).DataSources,
            modulePlan.FeatureCatalog, _app.Services.GetRequiredService<IStandardCrudResourceRegistry>(),
            _app.Services.GetRequiredService<ICP.Platform.Authorization.Contracts.IDataScopeResourceCatalog>().Resources);
        if (_includeWorkspace || _includePart || _includeDocument) ICP.Platform.HostApi.Authorization.EndpointAccessPlanCompiler.Compile(((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)_app).DataSources);
        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.EnsureCreatedAsync();
            await scope.ServiceProvider.GetRequiredService<AuthorizationCatalogInitializer>().InitializeAsync();
            await scope.ServiceProvider.GetRequiredService<IFeatureCatalogSynchronizer>().SynchronizeAsync(modulePlan.FeatureCatalog);
        }
        await _app.StartAsync();
    }
    internal sealed class StoreFault : DbCommandInterceptor
    {
        internal string Mode { get; set; } = "";
        internal Action? AfterCommit { get; set; }
        internal Func<BusinessAuditChange, CancellationToken, Task>? AfterRegister { get; set; }
        internal volatile bool RegistrationWaiting;
        internal TaskCompletionSource ConcurrentCommand { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<string> Commands { get; } = [];
        internal List<string> FeatureCommands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[RoleFunctionGrant]", StringComparison.Ordinal) || command.CommandText.Contains("[FunctionPoint]", StringComparison.Ordinal)) FeatureCommands.Add(command.CommandText);
            if (Mode == "feature-decision" && command.CommandText.Contains("[RoleFunctionGrant]", StringComparison.Ordinal)) throw new InvalidOperationException("injected feature decision failure");
            if (RegistrationWaiting && (command.CommandText.Contains("[Person]", StringComparison.Ordinal) || command.CommandText.Contains("[Role]", StringComparison.Ordinal) || command.CommandText.Contains("sp_getapplock", StringComparison.Ordinal))) ConcurrentCommand.TrySetResult();
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
            if (command.CommandText.Contains("[RoleFunctionGrant]", StringComparison.Ordinal) || command.CommandText.Contains("[FunctionPoint]", StringComparison.Ordinal)) FeatureCommands.Add(command.CommandText);
            if (RegistrationWaiting && (command.CommandText.Contains("[Person]", StringComparison.Ordinal) || command.CommandText.Contains("[Role]", StringComparison.Ordinal) || command.CommandText.Contains("sp_getapplock", StringComparison.Ordinal))) ConcurrentCommand.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
    private sealed class FaultedOrganizationQuery(ICP.Platform.Modules.Organization.Caching.OrganizationCache cache, StoreFault fault)
        : ICP.Platform.Organization.Contracts.IOrganizationQuery
    {
        public ValueTask<ICP.Platform.Organization.Contracts.IOrganizationReadView> ReadAsync(CancellationToken cancellationToken = default) =>
            fault.Mode == "names" ? throw new InvalidOperationException("injected name directory failure")
                : new ICP.Platform.Modules.Organization.Caching.OrganizationQuery(cache).ReadAsync(cancellationToken);
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
        // 在真实 SQL 提交完成后推进测试时钟，不能把提交前观察误当作写后读取。
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            fault.AfterCommit?.Invoke();
            return Task.CompletedTask;
        }
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
    internal async Task RepeatFeatureSynchronizationAsync()
    {
        using var scope = _app.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICP.Platform.Modules.Authorization.Services.ActiveFeatureCatalog>();
        var before = catalog.Snapshot.ByPosition.ToDictionary(pair => pair.Key, pair => pair.Value.Id);
        Fault.FeatureCommands.Clear();
        await scope.ServiceProvider.GetRequiredService<IFeatureCatalogSynchronizer>().SynchronizeAsync(catalog.Snapshot.Points);
        Assert.Equal(before.OrderBy(pair => pair.Key), catalog.Snapshot.ByPosition.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Id)).OrderBy(pair => pair.Key));
        Assert.DoesNotContain(Fault.FeatureCommands, command => command.Contains("INSERT", StringComparison.Ordinal) || command.Contains("DELETE", StringComparison.Ordinal) || command.Contains("UPDATE", StringComparison.Ordinal));
    }
    internal async Task RestoreFeatureCatalogAsync()
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IFeatureCatalogSynchronizer>().SynchronizeAsync(FeatureCatalog);
    }
    internal async Task RemoveActiveFeatureAsync(string position)
    {
        using var scope = _app.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ICP.Platform.Modules.Authorization.Services.ActiveFeatureCatalog>();
        Fault.FeatureCommands.Clear();
        await scope.ServiceProvider.GetRequiredService<IFeatureCatalogSynchronizer>().SynchronizeAsync(
            catalog.Snapshot.Points.Where(point => point.Position != position).ToArray());
        Assert.Equal(2, Fault.FeatureCommands.Count(command => command.StartsWith("DELETE", StringComparison.Ordinal)));
        Assert.All(Fault.FeatureCommands.Where(command => command.Contains("[RoleFunctionGrant]", StringComparison.Ordinal)), command => Assert.Contains("WHERE", command));
    }
    // 只在本用例证明已受支持的内部初始化边界；普通职责准备仍全部走正式HTTP。
    // HTTP删除没有声明入口；历史解析回归直接消费保留的角色服务删除边界。
    internal async Task DeleteRoleForHistoryAsync(string id)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICP.Platform.Modules.Authorization.Services.IRoleService>().DeleteRoleAsync(id);
    }

    internal async Task AssignInternalAdministratorAsync(string id)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPlatformTransactionExecutor>().ExecuteAsync(async token =>
        {
            await scope.ServiceProvider.GetRequiredService<ICP.Platform.Authorization.Contracts.ILocalAdministratorRoleAssignment>().AssignAsync(id, "test-initialization", token);
            return true;
        });
    }
    internal async Task<HttpClient> LoginAdministratorAsync()
    {
        var client = await LoginAsync("admin", _temporaryCredential);
        if (!_defaultOrganizationInitialized && (_includeWorkspace || _includePart || _includeDocument))
        {
            // 真实选用归属的消费者显式准备默认组织；生产GET和DI仍然没有初始化副作用。
            var point = FeatureCatalog.First(point => point.BehaviorKey == "organization.company.initialize");
            using var response = await FeatureHttpTestRequests.SendAsync(client, point, HttpMethod.Post, "/api/organization/initialization");
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            _defaultOrganizationInitialized = true;
        }
        return client;
    }
    internal HttpClient CreateClient() => new(new Cookies(_app.GetTestServer().CreateHandler())) { BaseAddress = new Uri("http://localhost") };
    internal async Task<HttpClient> LoginAsync(string code, string password)
    {
        var client = CreateClient();
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
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_app is not null)
            {
                // 先通知 ApplicationStopping 并等待服务停止，再释放其取消令牌与依赖。
                try { await _app.StopAsync(); }
                finally { await _app.DisposeAsync(); }
            }
        }
        finally
        {
            await _database.DisposeAsync();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }
}
