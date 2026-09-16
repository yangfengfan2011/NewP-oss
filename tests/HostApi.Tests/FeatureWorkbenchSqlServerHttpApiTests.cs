using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace ICP.Platform.HostApi.Tests;

public sealed class FeatureWorkbenchSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "目标角色读取保留既有 HTTP 契约并过滤非活动目录分配")]
    public async Task RoleRead_PreservesMissingDisabledProtectedAndActiveCatalogContract()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Workbench("authorization.workbench.read");
        var role = await FeatureHttpTestRequests.CreateAsync(admin, http.Role("create"), "/api/authorization/roles",
            new { Code = "ReadContract", Name = "读取契约" });
        var roleId = role.GetProperty("id").GetString()!;
        await http.GrantAsync(admin, roleId, read);
        using (var disabled = await FeatureHttpTestRequests.SendAsync(admin, http.Role("edit"), HttpMethod.Put,
            $"/api/authorization/roles/{roleId}", new { Name = "读取契约", IsEnabled = false }))
            Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);

        // 关系表没有外键；历史残留不能进入当前活动目录响应。
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant")).Add(new()
            {
                ["EID"] = "RoleFunctionGrant_Residual",
                ["RoleId"] = roleId,
                ["FunctionPointId"] = "FunctionPoint_Residual",
                ["GrantedAt"] = DateTime.UtcNow,
                ["GrantedBy"] = "test"
            });
            await db.SaveChangesAsync();
        }

        var actual = await FeatureHttpTestRequests.ReadAsync(admin, read, $"/api/authorization/workbench/roles/{roleId}");
        var actualRole = actual.GetProperty("role");
        Assert.Equal(roleId, actualRole.GetProperty("id").GetString());
        Assert.Equal("ReadContract", actualRole.GetProperty("code").GetString());
        Assert.Equal("读取契约", actualRole.GetProperty("displayName").GetString());
        Assert.Equal("读取契约", actualRole.GetProperty("name").GetString());
        Assert.False(actualRole.GetProperty("isEnabled").GetBoolean());
        Assert.False(actualRole.GetProperty("isBuiltIn").GetBoolean());
        Assert.Equal(read.Position, Assert.Single(actual.GetProperty("positions").EnumerateArray()).GetString());

        using (var missing = await FeatureHttpTestRequests.SendAsync(admin, read, HttpMethod.Get,
            "/api/authorization/workbench/roles/Role_Missing"))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var roles = await FeatureHttpTestRequests.ReadAsync(admin, read,
            "/api/authorization/workbench/roles?query=LocalAdmin&pageIndex=1&pageSize=20");
        var administratorId = Assert.Single(roles.GetProperty("items").EnumerateArray()).GetProperty("EID").GetString();
        var administrator = await FeatureHttpTestRequests.ReadAsync(admin, read,
            $"/api/authorization/workbench/roles/{administratorId}");
        Assert.Equal("LocalAdmin", administrator.GetProperty("role").GetProperty("code").GetString());
        Assert.True(administrator.GetProperty("role").GetProperty("isBuiltIn").GetBoolean());
        var expectedPositions = fixture.FeatureCatalog.Select(point => point.Position).ToHashSet(StringComparer.Ordinal);
        var administratorPositions = administrator.GetProperty("positions").EnumerateArray()
            .Select(position => position.GetString()!).ToArray();
        Assert.Equal(expectedPositions.Count, administratorPositions.Length);
        Assert.True(expectedPositions.SetEquals(administratorPositions));
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "真实普通主体功能授权与同会话在线撤权")]
    public async Task OrdinaryActor_ViewSaveAndRevocationUseCurrentGrantsAndExactBinding()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var current = await admin.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        var positions = current.GetProperty("featurePositions").EnumerateArray().Select(p => p.GetString()!).ToArray();
        var read = fixture.FeatureCatalog.Single(point => point.ModuleId == "Authorization" && point.BehaviorKey == "authorization.workbench.read").Position;
        var save = fixture.FeatureCatalog.Single(point => point.ModuleId == "Authorization" && point.BehaviorKey == "authorization.workbench.save").Position;
        Assert.Contains(read, positions); Assert.Contains(save, positions);
        var role = await FeatureHttpTestRequests.CreateAsync(admin, http.Role("create"), "/api/authorization/roles", new { Code = "FeatureReader", Name = "普通授权维护" });
        var roleId = role.GetProperty("id").GetString()!;
        var target = await FeatureHttpTestRequests.CreateAsync(admin, http.Role("create"), "/api/authorization/roles", new { Code = "GlobalTarget", Name = "其他角色" });
        var targetId = target.GetProperty("id").GetString()!;
        var account = await FeatureHttpTestRequests.CreateAsync(admin, new FeatureHttpTestRequests(fixture).Identity("create"), "/api/identity/accounts", new { ECODE = "feature-reader", ENAME = "普通授权者" });
        var actor = account.GetProperty("account").GetProperty("eid").GetString();
        SetPosition(admin, save);
        using (var grant = await admin.PutAsJsonAsync($"/api/authorization/workbench/roles/{roleId}", new { positions = new[] { read } })) Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        using (var assign = await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put, $"/api/authorization/users/{actor}/roles", new { RoleCodes = new[] { "FeatureReader" } })) Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
        using var ordinary = await fixture.LoginAsync("feature-reader", account.GetProperty("temporaryCredential").GetString()!);
        SetPosition(ordinary, read);
        fixture.Fault.FeatureCommands.Clear();
        using (var list = await ordinary.GetAsync("/api/authorization/workbench/roles?pageIndex=1&pageSize=20")) Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Single(fixture.Fault.FeatureCommands, command => command.Contains("EXISTS", StringComparison.Ordinal) && command.Contains("RoleFunctionGrant", StringComparison.Ordinal));
        SetPosition(ordinary, save);
        using (var denied = await ordinary.PutAsJsonAsync($"/api/authorization/workbench/roles/{targetId}", new { positions = new[] { read } })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var grant = await admin.PutAsJsonAsync($"/api/authorization/workbench/roles/{roleId}", new { positions = new[] { read, save } })) Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        SetPosition(ordinary, read);
        using (var wrong = await ordinary.PutAsJsonAsync($"/api/authorization/workbench/roles/{targetId}", new { positions = new[] { read } })) Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        SetPosition(ordinary, save);
        using (var allowed = await ordinary.PutAsJsonAsync($"/api/authorization/workbench/roles/{targetId}", new { positions = new[] { read } })) Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Fault.AfterRegister = async (change, token) =>
        {
            if (change.EventType == "role.features.changed" && change.TargetId == targetId)
            { admitted.TrySetResult(); await finish.Task.WaitAsync(token); }
        };
        var inFlight = ordinary.PutAsJsonAsync($"/api/authorization/workbench/roles/{targetId}", new { positions = new[] { read, save } });
        try
        {
            await admitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var revoke = await admin.PutAsJsonAsync($"/api/authorization/workbench/roles/{roleId}", new { positions = Array.Empty<string>() });
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        }
        finally { finish.TrySetResult(); }
        using (var completed = await inFlight) Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
        fixture.Fault.AfterRegister = null;
        SetPosition(ordinary, read);
        using (var revoked = await ordinary.GetAsync("/api/authorization/workbench/catalog")) Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        using (var assign = await FeatureHttpTestRequests.SendAsync(admin, http.Role("userRoles"), HttpMethod.Put, $"/api/authorization/users/{actor}/roles", new { RoleCodes = new[] { "FeatureReader", "GlobalTarget" } })) Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
        using (var union = await ordinary.GetAsync("/api/authorization/workbench/catalog")) Assert.Equal(HttpStatusCode.OK, union.StatusCode);
        using (var disabled = await FeatureHttpTestRequests.SendAsync(admin, http.Role("edit"), HttpMethod.Put, $"/api/authorization/roles/{targetId}", new { Name = "其他角色", IsEnabled = false })) Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        using (var denied = await ordinary.GetAsync("/api/authorization/workbench/catalog")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        fixture.Fault.Mode = "feature-decision";
        using (var unavailable = await ordinary.GetAsync("/api/authorization/workbench/catalog")) Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        fixture.Fault.Mode = "";
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "功能授权整组校验、幂等及各事务失败点共同回滚")]
    public async Task Replacement_ValidatesWholeSetAndRollsBackGrantAndAuditTogether()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        await fixture.RepeatFeatureSynchronizationAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var (read, save) = await PositionsAsync(fixture, admin);
        var role = await FeatureHttpTestRequests.CreateAsync(admin, http.Role("create"), "/api/authorization/roles", new { Code = "Replace", Name = "替换测试" });
        var id = role.GetProperty("id").GetString()!;
        SetPosition(admin, save);
        foreach (var invalid in new[] { new[] { save }, new[] { "unknown" } })
        {
            using var result = await admin.PutAsJsonAsync($"/api/authorization/workbench/roles/{id}", new { positions = invalid });
            Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            var problem = await result.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(400, problem.GetProperty("status").GetInt32());
            Assert.Contains(invalid[0] == save ? "缺少本分支入口" : "未知或失效", problem.GetProperty("title").GetString());
        }
        using (var initial = await admin.PutAsJsonAsync($"/api/authorization/workbench/roles/{id}", new { positions = new[] { read } }))
            Assert.Equal(HttpStatusCode.NoContent, initial.StatusCode);
        using (var same = await admin.PutAsJsonAsync($"/api/authorization/workbench/roles/{id}", new { positions = new[] { read, read } }))
            Assert.Equal(HttpStatusCode.NoContent, same.StatusCode);
        foreach (var mode in new[] { "register", "write", "save", "commit" })
        {
            fixture.Fault.Mode = mode;
            using var result = await admin.PutAsJsonAsync($"/api/authorization/workbench/roles/{id}", new { positions = new[] { read, save } });
            fixture.Fault.Mode = "";
            Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
            SetPosition(admin, read);
            var actual = await admin.GetFromJsonAsync<JsonElement>($"/api/authorization/workbench/roles/{id}");
            Assert.Equal(read, Assert.Single(actual.GetProperty("positions").EnumerateArray()).GetString());
            var audit = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), $"/api/audit/business-changes?TargetId={id}&EventType=role.features.changed");
            Assert.Equal(1, audit.GetProperty("total").GetInt32());
            SetPosition(admin, save);
        }
        SetPosition(admin, read);
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/authorization/workbench/roles?query=LocalAdmin");
        var administrator = Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("EID").GetString();
        SetPosition(admin, save);
        using var protectedResult = await admin.PutAsJsonAsync($"/api/authorization/workbench/roles/{administrator}", new { positions = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, protectedResult.StatusCode);
        Assert.Contains("LocalAdmin", (await protectedResult.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "功能授权并发整组替换审计使用锁内实际前驱")]
    public async Task ConcurrentReplacement_RecordsActualPredecessor()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var (read, save) = await PositionsAsync(fixture, admin);
        var role = await FeatureHttpTestRequests.CreateAsync(admin, http.Role("create"), "/api/authorization/roles", new { Code = "Race", Name = "并发测试" });
        var id = role.GetProperty("id").GetString()!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Fault.AfterRegister = async (change, token) =>
        {
            if (change.EventType == "role.features.changed" && change.Fields.Count == 1 && change.Fields[0].After.Value == "true")
            { entered.TrySetResult(); await release.Task.WaitAsync(token); }
        };
        SetPosition(admin, save);
        var first = admin.PutAsJsonAsync($"/api/authorization/workbench/roles/{id}", new { positions = new[] { read } });
        Task<HttpResponseMessage>? second = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = admin.PutAsJsonAsync($"/api/authorization/workbench/roles/{id}", new { positions = new[] { read, save } });
            await fixture.Fault.ConcurrentCommand.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { release.TrySetResult(); }
        using var firstResult = await first;
        using var secondResult = await second!;
        Assert.Equal(HttpStatusCode.NoContent, firstResult.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondResult.StatusCode);
        var audit = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), $"/api/audit/business-changes?TargetId={id}&EventType=role.features.changed");
        var changes = audit.GetProperty("items").EnumerateArray().Select(row => Assert.Single(row.GetProperty("Changes").EnumerateArray())).ToArray();
        Assert.Equal(2, changes.Length);
        Assert.Equal(new[] { read, save }.Order(), changes.Select(change => change.GetProperty("field").GetString()).Order());
        Assert.All(changes, change => { Assert.Equal("false", change.GetProperty("before").GetProperty("value").GetString()); Assert.Equal("true", change.GetProperty("after").GetProperty("value").GetString()); });
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "功能分配历史保留实际撤销和角色删除后的引用展示")]
    public async Task FeatureHistory_PreservesRevocationAndDeletedRoleReference()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var role = await FeatureHttpTestRequests.CreateAsync(admin, http.Role("create"), "/api/authorization/roles", new { Code = "history-role", Name = "历史角色" });
        var id = role.GetProperty("id").GetString()!;
        var read = http.Workbench("authorization.workbench.read");
        var save = http.Workbench("authorization.workbench.save");
        await http.GrantAsync(admin, id, save);
        await http.GrantAsync(admin, id, read);
        var before = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), $"/api/audit/business-changes?TargetId={id}");
        Assert.Equal(2, before.GetProperty("total").GetInt32());
        Assert.All(before.GetProperty("items").EnumerateArray(), row => Assert.Equal("历史角色", row.GetProperty("TargetId_DisplayValue").GetString()));
        var revoked = before.GetProperty("items").EnumerateArray().Single(row => row.GetProperty("Changes").GetArrayLength() == 1);
        var change = Assert.Single(revoked.GetProperty("Changes").EnumerateArray());
        Assert.Equal(save.Position, change.GetProperty("field").GetString());
        Assert.Equal("true", change.GetProperty("before").GetProperty("value").GetString());
        Assert.Equal("false", change.GetProperty("after").GetProperty("value").GetString());
        await fixture.DeleteRoleForHistoryAsync(id);
        var after = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(), $"/api/audit/business-changes?TargetId={id}");
        Assert.Equal(2, after.GetProperty("total").GetInt32());
        Assert.All(after.GetProperty("items").EnumerateArray(), row => Assert.Equal(JsonValueKind.Null, row.GetProperty("TargetId_DisplayValue").ValueKind));
    }

    private static async Task<(string Read, string Save)> PositionsAsync(BusinessAuditHostFixture fixture, HttpClient client)
    {
        var current = await client.GetFromJsonAsync<JsonElement>("/api/auth/current-user");
        var positions = current.GetProperty("featurePositions").EnumerateArray().Select(position => position.GetString()!).ToArray();
        var read = fixture.FeatureCatalog.Single(point => point.ModuleId == "Authorization" && point.BehaviorKey == "authorization.workbench.read").Position;
        var save = fixture.FeatureCatalog.Single(point => point.ModuleId == "Authorization" && point.BehaviorKey == "authorization.workbench.save").Position;
        Assert.Contains(read, positions); Assert.Contains(save, positions);
        return (read, save);
    }

    // 准备经正式HTTP，响应失败直接保留最小Problem Details。
    private static async Task<JsonElement> CreateAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    // 请求仅声明当前入口，服务器仍必须与执行端绑定及在线关系同时核对。
    private static void SetPosition(HttpClient client, string position)
    {
        client.DefaultRequestHeaders.Remove("X-NewP-Feature");
        client.DefaultRequestHeaders.Add("X-NewP-Feature", position);
    }
}
