using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class PartHistoryDeclarationSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "独立Part四出口只消费自身read声明与EID，无Workspace资格，拒绝声明换绑和跨对象记录")]
    public async Task IndependentPart_AllDeclarationsUseReadWithoutWorkspace_AndRejectForgery()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        Assert.DoesNotContain(plan.Modules, module => module.ModuleId is "Workspace" or "Document");
        var history = Point(fixture, "part.part.history");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PartHistory302", "独立零组件履历", "part-history302", "零组件履历账号", history);
        using var ordinary = actor.Client;
        var id = await CreateAsync(fixture, admin, "PART-HISTORY-302");
        var otherId = await CreateAsync(fixture, admin, "PART-HISTORY-OTHER-302");
        await ConfigureAsync(fixture, admin, id, "allow", "deny");
        var declarations = plan.ActionDataAccessCatalog.Where(item => item.ModuleId == "Part").ToArray();
        Assert.All(declarations, item => Assert.Equal(new[] { "read" }, item.Actions));
        var basePath = "/api/part/parts/" + id + "/history";
        var list = Assert.Single(declarations, item => item.Execution == "part.history.list");
        using var listed = await SendAsync(ordinary, history, list.Id, HttpMethod.Get, basePath + "?eventType=part.created");
        var page = await ReadOkAsync(listed);
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        var recordId = item.GetProperty("id").GetString()!;
        foreach (var hidden in new[] { "FieldsJson", "TraceId", "Configuration", id, actor.AccountId })
            Assert.DoesNotContain(hidden, item.GetRawText(), StringComparison.Ordinal);
        foreach (var entered in declarations.Where(item => item.Execution == "part.history.enter"))
        {
            using var check = await SendAsync(ordinary, history, entered.Id, HttpMethod.Get, basePath + "/check/enter");
            Assert.Equal(HttpStatusCode.NoContent, check.StatusCode);
        }
        foreach (var declaration in declarations.Where(item => item.Execution is not ("part.history.enter" or "part.history.list")))
        {
            var operation = declaration.Execution.Split('.')[^1];
            var path = operation is "preview" or "detail" ? basePath + "/check/" + operation : basePath + "/" + operation;
            var selected = declaration.ActionKey.EndsWith("Selected", StringComparison.Ordinal);
            using var response = await SendAsync(ordinary, history, declaration.Id,
                operation is "print" or "export" ? HttpMethod.Post : HttpMethod.Get, path,
                operation is "print" or "export" ? new ObjectHistoryOutputRequest(selected ? "selected" : "all", new(EventType: "part.created"), selected ? [recordId] : null) : null);
            Assert.Equal(operation is "preview" or "detail" ? HttpStatusCode.NoContent : HttpStatusCode.OK, response.StatusCode);
        }
        var detail = Assert.Single(declarations, item => item.Execution == "part.history.detail");
        using (var response = await SendAsync(ordinary, history, detail.Id, HttpMethod.Get, basePath + "/" + recordId)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var other = await SendAsync(ordinary, history, list.Id, HttpMethod.Get, "/api/part/parts/" + otherId + "/history?eventType=part.created");
        var foreignRecord = Assert.Single((await ReadOkAsync(other)).GetProperty("items").EnumerateArray()).GetProperty("id").GetString()!;
        using (var crossObject = await SendAsync(ordinary, history, detail.Id, HttpMethod.Get, basePath + "/" + foreignRecord)) Assert.Equal(HttpStatusCode.NotFound, crossObject.StatusCode);
        foreach (var output in declarations.Where(item => item.ActionKey.EndsWith("Selected", StringComparison.Ordinal)))
        {
            using var crossObject = await SendAsync(ordinary, history, output.Id, HttpMethod.Post, basePath + "/" + output.Execution.Split('.')[^1],
                new ObjectHistoryOutputRequest("selected", new(), [recordId, foreignRecord]));
            Assert.Equal(HttpStatusCode.NotFound, crossObject.StatusCode);
        }
        foreach (var invalid in new string?[] { null, "unregistered", detail.Id })
        {
            using var response = await SendAsync(ordinary, history, invalid, HttpMethod.Get, basePath);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using (var missing = await SendAsync(ordinary, history, list.Id, HttpMethod.Get, basePath.Replace(id, "Part_missing", StringComparison.Ordinal))) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using (var forgedScope = await SendAsync(ordinary, history, list.Id, HttpMethod.Get, basePath + "?queryScope=forged")) Assert.Equal(HttpStatusCode.BadRequest, forgedScope.StatusCode);
        var read = fixture.FeatureCatalog.Single(point => point.ModuleId == "Part" && point.Kind == "navigation" && point.Key == "part.parts");
        var reader = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PartNoHistory302", "只读零组件", "part-no-history302", "零组件只读账号", read);
        using var readOnly = reader.Client;
        using var deniedFeature = await SendAsync(readOnly, history, list.Id, HttpMethod.Get, basePath);
        Assert.Equal(HttpStatusCode.Forbidden, deniedFeature.StatusCode);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Part预检不免除四出口撤权，查询故障和真实SQL取消不能返回空结果或部分输出")]
    public async Task IndependentPart_RechecksRevocation_AndPropagatesFailuresAndCancellation()
    {
        var cancellation = new ObjectHistoryOutputSqlServerTests.CancelHistoryQuery();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(cancellation)), includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var history = Point(fixture, "part.part.history");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PartRecheck302", "零组件重判", "part-recheck302", "零组件重判账号", history);
        using var ordinary = actor.Client;
        var id = await CreateAsync(fixture, admin, "PART-RECHECK-302");
        await ConfigureAsync(fixture, admin, id, "allow", "deny");
        var declarations = fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog.Where(item => item.ModuleId == "Part").ToArray();
        var basePath = "/api/part/parts/" + id + "/history";
        var list = Assert.Single(declarations, item => item.Execution == "part.history.list");
        using var listed = await SendAsync(ordinary, history, list.Id, HttpMethod.Get, basePath);
        var recordId = (await ReadOkAsync(listed)).GetProperty("items")[0].GetProperty("id").GetString()!;
        var output = Assert.Single(declarations, item => item.ActionKey == "exportAll");
        fixture.Fault.Mode = "query";
        try
        {
            foreach (var operation in new[] { "list", "detail", "export", "print" })
            {
                var declaration = declarations.First(item => item.Execution == "part.history." + operation);
                using var failure = await SendAsync(ordinary, history, declaration.Id, operation is "export" or "print" ? HttpMethod.Post : HttpMethod.Get,
                    basePath + (operation == "list" ? "" : operation == "detail" ? "/" + recordId : "/" + operation),
                    operation is "export" or "print" ? new ObjectHistoryOutputRequest("all", new()) : null);
                Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
            }
        }
        finally { fixture.Fault.Mode = ""; }
        using (var canceled = new CancellationTokenSource())
        {
            cancellation.Cancel = canceled;
            try
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(ordinary, history, output.Id, HttpMethod.Post, basePath + "/export",
                    new ObjectHistoryOutputRequest("all", new()), canceled.Token));
                Assert.True(cancellation.Fired);
            }
            finally { cancellation.Cancel = null; }
        }
        var enter = declarations.First(item => item.Execution == "part.history.enter");
        using (var check = await SendAsync(ordinary, history, enter.Id, HttpMethod.Get, basePath + "/check/enter")) Assert.Equal(HttpStatusCode.NoContent, check.StatusCode);
        await ConfigureAsync(fixture, admin, id, "deny", "allow");
        foreach (var declaration in declarations)
        {
            var operation = declaration.Execution.Split('.')[^1];
            var path = basePath + (operation is "enter" or "preview" ? "/check/" + operation : operation == "list" ? "" : operation == "detail" ? "/" + recordId : "/" + operation);
            using var denied = await SendAsync(ordinary, history, declaration.Id, operation is "export" or "print" ? HttpMethod.Post : HttpMethod.Get,
                path, operation is "export" or "print" ? new ObjectHistoryOutputRequest("all", new()) : null);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
    }

    private static async Task<string> CreateAsync(BusinessAuditHostFixture fixture, HttpClient admin, string code)
    {
        var created = await FeatureHttpTestRequests.CreateAsync(admin, Point(fixture, "part.part.create"), "/api/part/parts", new { ECODE = code, ENAME = "零组件履历" });
        return created.GetProperty("fields").GetProperty("EID").GetString()!;
    }

    private static async Task ConfigureAsync(BusinessAuditHostFixture fixture, HttpClient admin, string id, string read, string history)
    {
        var manage = fixture.FeatureCatalog.Single(point => point.ModuleId == "Part" && point.Key == "authorize" && point.ActionScope == "list.rowActions");
        using var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, "/api/part/parts/" + id + "/permissions",
            new DataScopeObjectDto("part.part", id, [new("part302", "all", null, 1, new Dictionary<string, string> { ["read"] = read, ["history"] = history })]));
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, FeaturePoint feature, string? declaration, HttpMethod method, string path,
        object? body = null, CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-NewP-Feature", feature.Position);
        if (declaration is not null) request.Headers.Add("X-NewP-Data-Declaration", declaration);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request, token);
    }
}
