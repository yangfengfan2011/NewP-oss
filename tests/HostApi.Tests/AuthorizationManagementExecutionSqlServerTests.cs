using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class AuthorizationManagementExecutionSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "角色管理保留功能资格，实际角色读取来源仍过滤不可读对象")]
    public async Task RoleManagementSources_PreserveReadFilteringAndFeatureOnlyWrites()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var member = http.Role("userRoles");
        var workbench = http.Workbench("authorization.workbench.read");
        var saveFeatures = http.Workbench("authorization.workbench.save");
        var create = http.Role("create"); var edit = http.Role("edit"); var read = http.Role();
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Role311Manager", "角色管理主体",
            "role311-manager", "普通角色管理主体", read, create, edit, member, workbench, saveFeatures);
        using var ordinary = actor.Client;
        var role = await FeatureHttpTestRequests.CreateAsync(admin, create, "/api/authorization/roles",
            new { Code = "TARGET311", Name = "不可读的管理目标" });
        var id = role.GetProperty("id").GetString()!;
        var manage = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.role.authorize");
        using (var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put,
            "/api/authorization/data-scope/objects/authorization.role/" + id,
            new DataScopeObjectDto("authorization.role", id,
                [new("Role311Denied", "user", actor.AccountId, 0, new Dictionary<string, string> { ["read"] = "deny", ["edit"] = "deny" })]),
            plan.ActionDataAccessCatalog.Single(item => item.Execution == "authorization.role.authorization.manage")))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        using (var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get,
            "/api/authorization/roles/" + id, dataAccess: http.Declaration("authorization.role.read", read)))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        // 旧无/query协议是全局管理目录，不是标准列表或工作台的范围查询。
        using (var directory = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Get, "/api/authorization/roles"))
        {
            Assert.Equal(HttpStatusCode.OK, directory.StatusCode);
            Assert.Contains("TARGET311", (await directory.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Select(item => item.GetProperty("code").GetString()));
        }
        // 普通角色管理原本不依赖目标数据 read/edit，也不新增点击预检。
        using (var changed = await FeatureHttpTestRequests.SendAsync(ordinary, edit, HttpMethod.Put,
            "/api/authorization/roles/" + id, new { Name = "仅功能资格编辑", IsEnabled = true }))
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using (var created = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post,
            "/api/authorization/roles", new { Code = "CREATED311", Name = "仅功能资格创建" }))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using (var deleted = await FeatureHttpTestRequests.SendAsync(ordinary, edit, HttpMethod.Delete,
            "/api/authorization/roles/" + id)) Assert.Equal(HttpStatusCode.Forbidden, deleted.StatusCode);
        using (var assigned = await FeatureHttpTestRequests.SendAsync(ordinary, member, HttpMethod.Put,
            "/api/authorization/users/" + actor.AccountId + "/roles", new { RoleCodes = new[] { "Role311Manager", "TARGET311" } }))
            Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        using (var configured = await FeatureHttpTestRequests.SendAsync(ordinary, member, HttpMethod.Get,
            "/api/authorization/users/" + actor.AccountId + "/roles"))
            Assert.Contains("TARGET311", (await configured.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("roleCodes").EnumerateArray().Select(value => value.GetString()));
        using (var saved = await FeatureHttpTestRequests.SendAsync(ordinary, saveFeatures, HttpMethod.Put,
            "/api/authorization/workbench/roles/" + id, new { positions = Array.Empty<string>() }))
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);

        var observations = new List<(string Source, HttpStatusCode Status, JsonElement? Body)>();
        foreach (var (source, feature, path) in new[]
        {
            ("user-role-input", member, "/api/authorization/roles/query?query=TARGET311&pageIndex=1&pageSize=20"),
            ("feature-workbench", workbench, "/api/authorization/workbench/roles?query=TARGET311&pageIndex=1&pageSize=20")
        })
        {
            // 正式消费者尚未接入时不手造声明，先记录真实入口拒绝；接入后由真实来源装配结果选定。
            var execution = source == "feature-workbench" ? "authorization.workbench.read" : "authorization.role.list";
            var declaration = plan.ActionDataAccessCatalog.SingleOrDefault(item => item.Execution == execution
                && item.FeaturePositions.Contains(feature.Position));
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, feature, HttpMethod.Get, path, dataAccess: declaration);
            var text = await response.Content.ReadAsStringAsync();
            observations.Add((source, response.StatusCode, response.IsSuccessStatusCode ? JsonSerializer.Deserialize<JsonElement>(text) : null));
        }
        Assert.All(observations, observation =>
        {
            Assert.True(observation.Status == HttpStatusCode.OK, observation.Source + ": HTTP " + (int)observation.Status);
            Assert.Equal(0, observation.Body!.Value.GetProperty("total").GetInt32());
            Assert.Empty(observation.Body.Value.GetProperty("items").EnumerateArray());
        });
        var workbenchRead = plan.ActionDataAccessCatalog.Single(item => item.Execution == "authorization.workbench.read");
        using (var hidden = await FeatureHttpTestRequests.SendAsync(ordinary, workbench, HttpMethod.Get,
            "/api/authorization/workbench/roles/" + id, dataAccess: workbenchRead))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using (var visible = await FeatureHttpTestRequests.SendAsync(ordinary, workbench, HttpMethod.Get,
            "/api/authorization/workbench/roles/" + actor.RoleId, dataAccess: workbenchRead))
            Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, workbench, HttpMethod.Get,
            "/api/authorization/workbench/roles")) Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var swapped = await FeatureHttpTestRequests.SendAsync(ordinary, member, HttpMethod.Get,
            "/api/authorization/roles/query", dataAccess: workbenchRead)) Assert.Equal(HttpStatusCode.BadRequest, swapped.StatusCode);
    }
}
