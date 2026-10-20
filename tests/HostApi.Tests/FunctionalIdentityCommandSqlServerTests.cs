using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class FunctionalIdentityCommandSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "账号全生命周期按钮保留纯功能命令，缺/串用身份不写入，正式输入Revision保持")]
    public async Task IdentityCommands_UseTheirOwnButtonsAndNeverAddAnObjectCheck()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddScoped<IObjectActionAccess, UnexpectedObjectAccess>());
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var create = http.Identity("create");
        using (var missing = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/identity/accounts",
            new { ECODE = "functional326-missing", ENAME = "缺少按钮" }))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var created = await http.CreateButtonAsync(admin, create, "/api/identity/accounts",
            new { ECODE = "functional326-account", ENAME = "纯功能账号" });
        var account = created.GetProperty("account");
        var id = account.GetProperty("eid").GetString()!;
        var prefix = "/api/identity/accounts/" + id;
        var update = http.Identity("edit");
        using (var renamed = await FeatureHttpTestRequests.SendAsync(admin, update, HttpMethod.Put, prefix,
            new { ENAME = "纯功能改名", ExpectedRevision = Revision() }, http.Declaration(update.BehaviorKey, update)))
            account = await ReadAccountAsync(renamed);
        foreach (var (key, target, surface) in new[] { ("disable", "Disabled", "list.rowActions"), ("enable", "Enabled", "detail.pageActions") })
        {
            var point = http.Identity(key, surface);
            var input = new { TargetStatus = target, ExpectedRevision = Revision(), Reason = "纯功能状态命令" };
            using (var borrowed = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Put, prefix + "/status/" + key,
                input, http.Declaration(create.BehaviorKey, create)))
                Assert.Equal(HttpStatusCode.BadRequest, borrowed.StatusCode);
            using var saved = await FeatureHttpTestRequests.SendAsync(admin, point, HttpMethod.Put, prefix + "/status/" + key,
                input, http.Declaration(point.BehaviorKey, point));
            account = await ReadAccountAsync(saved);
        }
        var reset = http.Identity("credentialReset");
        using (var response = await FeatureHttpTestRequests.SendAsync(admin, reset, HttpMethod.Post, prefix + "/credential-reset",
            new { ExpectedRevision = Revision(), Reason = "纯功能重置" }, http.Declaration(reset.BehaviorKey, reset)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            account = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("account");
        }
        var delete = http.Identity("delete");
        using var deleted = await FeatureHttpTestRequests.SendAsync(admin, delete, HttpMethod.Post, prefix + "/delete",
            new { ExpectedRevision = Revision(), Reason = "不可恢复删除" }, http.Declaration(delete.BehaviorKey, delete));
        Assert.Equal("Deleted", (await ReadAccountAsync(deleted)).GetProperty("status").GetString());

        long Revision() => account.GetProperty("revision").GetInt64();
    }

    private static async Task<JsonElement> ReadAccountAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // 真实HTTP和业务服务保留，仅在不应调用的对象动作接口设置精确失败信号。
    private sealed class UnexpectedObjectAccess : IObjectActionAccess
    {
        public Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("FUNCTION_ONLY_COMMAND_REQUESTED_OBJECT_ACCESS");
    }
}
