using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class ExampleExecutionDeclarationSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "Example正式执行消费真实候选和编辑前事实，缺失/串用声明不能绕过")]
    public async Task ExampleCrud_CandidatePreEditAndDeclarationBoundariesUseRealSql()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeExample: true);
        var http = new FeatureHttpTestRequests(fixture);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Example();
        var create = http.Example("create");
        var edit = http.Example("edit");
        var delete = http.Example("delete");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "ExampleContract", "执行合同",
            "example-contract", "执行合同账号", read, create, edit, delete);
        using var ordinary = actor.Client;
        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        await Rule("RejectCreate", "RecordSource", "Denied", "create");
        await Rule("HideAfterWrite", "ENAME", "不可读", "read");
        await Rule("RejectEdit", "ENAME", "不可编辑", "edit");

        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post, "/api/example/records", Payload("MISSING", "允许", "Manual")))
            await Status(missing, HttpStatusCode.BadRequest, "ActionDataAccess.DeclarationInvalid");
        using (var swapped = await FeatureHttpTestRequests.SendAsync(ordinary, create, HttpMethod.Post, "/api/example/records", Payload("SWAPPED", "允许", "Manual"), Declaration("update", edit)))
            await Status(swapped, HttpStatusCode.BadRequest, "ActionDataAccess.DeclarationInvalid");
        using (var denied = await Send(create, "create", HttpMethod.Post, "/api/example/records", Payload("CREATE-DENIED", "允许", "Denied")))
            await Status(denied, HttpStatusCode.Forbidden, "DataScope.ActionDenied");
        using (var createdHidden = await Send(create, "create", HttpMethod.Post, "/api/example/records", Payload("CREATE-HIDDEN", "不可读", "Manual")))
        {
            await Status(createdHidden, HttpStatusCode.Created);
            var receipt = await createdHidden.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(receipt.GetProperty("succeeded").GetBoolean());
            Assert.Single(receipt.EnumerateObject());
        }
        var hiddenId = await CreateAdmin("HIDDEN-TARGET", "不可读");
        using (var deniedRead = await Send(edit, "update", HttpMethod.Put, "/api/example/records/" + hiddenId, new { ENAME = "不得写入" }))
            await Status(deniedRead, HttpStatusCode.NotFound);
        using (var hiddenRead = await Send(read, "read", HttpMethod.Get, "/api/example/records/" + hiddenId))
            await Status(hiddenRead, HttpStatusCode.NotFound);
        var noEditId = await CreateAdmin("EDIT-DENIED", "不可编辑");
        using (var deniedEdit = await Send(edit, "update", HttpMethod.Put, "/api/example/records/" + noEditId, new { ENAME = "不得写入" }))
            await Status(deniedEdit, HttpStatusCode.Forbidden, "DataScope.ActionDenied");
        var id = await CreateAdmin("EDIT-HIDDEN", "编辑前可读");
        using (var preparation = await Send(edit, "update", HttpMethod.Get, "/api/example/records/" + id))
            await Status(preparation, HttpStatusCode.OK);
        using (var updatedHidden = await Send(edit, "update", HttpMethod.Put, "/api/example/records/" + id, new { ENAME = "不可读" }))
        {
            await Status(updatedHidden, HttpStatusCode.OK);
            Assert.True((await updatedHidden.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("succeeded").GetBoolean());
        }
        using (var stored = await FeatureHttpTestRequests.SendAsync(admin, read, HttpMethod.Get, "/api/example/records/" + id, dataAccess: Declaration("read", read)))
        {
            await Status(stored, HttpStatusCode.OK);
            Assert.Equal("不可读", (await stored.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fields").GetProperty("ENAME").GetString());
        }
        var deletable = await CreateAdmin("DELETE-ALLOWED", "可删除");
        using (var deleted = await Send(delete, "delete", HttpMethod.Delete, "/api/example/records/" + deletable))
            await Status(deleted, HttpStatusCode.NoContent);

        ActionDataAccessRequirement Declaration(string execution, FeaturePoint point) => plan.ActionDataAccessCatalog.Single(item =>
            item.Execution == "example.record." + execution && item.FeaturePositions.Contains(point.Position));
        Task<HttpResponseMessage> Send(FeaturePoint point, string execution, HttpMethod method, string path, object? body = null) =>
            FeatureHttpTestRequests.SendAsync(ordinary, point, method, path, body, Declaration(execution, point));
        async Task<string> CreateAdmin(string code, string name)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, "/api/example/records", Payload(code, name, "Manual"), Declaration("create", create));
            await Status(response, HttpStatusCode.Created);
            return BusinessAuditSqlServerHttpApiTests.Id(await response.Content.ReadFromJsonAsync<JsonElement>());
        }
        async Task Rule(string suffix, string field, string value, string action)
        {
            var key = "ExampleExecution" + suffix;
            var condition = new DataScopeConditionDto("attribute", field, "equal", JsonSerializer.SerializeToElement(value));
            if (suffix == "RejectCreate") condition = new("group", "", "and", JsonSerializer.SerializeToElement((object?)null), Children:
                [condition, new("attribute", "CreatedBy", "equal", JsonSerializer.SerializeToElement(actor.AccountId))]);
            var rule = new DataScopeRuleDto(key, key, null, suffix, true, "example.record", condition,
                [new(key + "Subject", "all", null, 0, new Dictionary<string, string> { [action] = "deny" })], []);
            using var response = await FeatureHttpTestRequests.SendAsync(admin, save, HttpMethod.Put, "/api/authorization/data-scope/rules/" + key, rule);
            await Status(response, HttpStatusCode.NoContent);
        }
    }

    private static object Payload(string code, string name, string source) => new { ECODE = code, ENAME = name, RecordSource = source };
    private static async Task Status(HttpResponseMessage response, HttpStatusCode expected, string? code = null)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}; actual {response.StatusCode}; {body}");
        if (code is not null) Assert.Equal(code, JsonSerializer.Deserialize<JsonElement>(body).GetProperty("code").GetString());
    }
}
