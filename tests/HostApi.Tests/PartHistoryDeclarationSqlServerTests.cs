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
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实Part履历按钮使用统一身份，预检后撤权仍拒绝输出且不能借用预览身份")]
    public async Task PartHistory_ButtonIdentityRechecksTheFormalObjectForEveryOutput()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var feature = Point(fixture, "part.part.history");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PartButton336", "履历按钮", "part-button336", "履历按钮账号", feature);
        using var ordinary = actor.Client;
        var id = await CreateAsync(fixture, admin, "PART-BUTTON-336");
        await ConfigureAsync(fixture, admin, id, "allow");
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var enter = plan.ActionDataAccessCatalog.First(item => item.ModuleId == "Part" && item.ViewKey == "part.part.list"
            && item.Surface == "rowActions" && item.ActionKey == "history");
        var preview = plan.ActionDataAccessCatalog.Single(item => item.ModuleId == "Part" && item.ViewKey == "part.history.list"
            && item.Surface == "pageActions" && item.ActionKey == "output");
        var output = plan.ActionDataAccessCatalog.Single(item => item.ModuleId == "Part" && item.ViewKey == "part.history.list"
            && item.Surface == "outputActions" && item.ActionKey == "exportAll");
        var path = "/api/part/parts/" + id + "/history";
        using (var checkedEntry = await SendButton(enter.Id, HttpMethod.Get, path + "/check/enter"))
            Assert.Equal(HttpStatusCode.NoContent, checkedEntry.StatusCode);
        using (var exported = await SendButton(output.Id, HttpMethod.Post, path + "/export", new ObjectHistoryOutputRequest("all", new())))
            Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        using (var borrowed = await SendButton(preview.Id, HttpMethod.Post, path + "/export", new ObjectHistoryOutputRequest("all", new())))
            Assert.Equal(HttpStatusCode.BadRequest, borrowed.StatusCode);
        await ConfigureAsync(fixture, admin, id, "deny");
        using (var revoked = await SendButton(output.Id, HttpMethod.Post, path + "/export", new ObjectHistoryOutputRequest("all", new())))
            Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);

        async Task<HttpResponseMessage> SendButton(string buttonId, HttpMethod method, string url, object? body = null)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Add("X-NewP-Feature", feature.Position);
            request.Headers.Add("X-NewP-Button", buttonId);
            if (body is not null) request.Content = JsonContent.Create(body);
            return await ordinary.SendAsync(request);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "Part履历保留Workspace功能祖先，四出口只消费自身read声明与EID，无需来源LID，拒绝换绑和跨对象记录")]
    public async Task OwnedPart_AllDeclarationsUseReadFromEid_AndRejectForgery()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includePart: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        Assert.Contains(plan.Modules, module => module.ModuleId == "Workspace");
        var history = Point(fixture, "part.part.history");
        var workspaceEntry = Assert.Single(plan.FeatureCatalog, feature => feature.ModuleId == "Workspace" && feature.Key == "workspace");
        Assert.Contains(workspaceEntry.Position, history.EntryDependencies);
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PartHistory302", "独立零组件履历", "part-history302", "零组件履历账号", history);
        using var ordinary = actor.Client;
        var id = await CreateAsync(fixture, admin, "PART-HISTORY-302");
        var otherId = await CreateAsync(fixture, admin, "PART-HISTORY-OTHER-302");
        await ConfigureAsync(fixture, admin, id, "allow");
        var declarations = plan.ActionDataAccessCatalog.Where(item => item.ModuleId == "Part" && (item.Execution == "part.history.list" || item.BusinessBinding == "part.part.history")).ToArray();
        Assert.All(declarations, item => Assert.Equal(new[] { "read" }, item.Actions));
        var basePath = "/api/part/parts/" + id + "/history";
        var list = Assert.Single(declarations, item => Operation(item) == "list");
        using var listed = await SendAsync(fixture, ordinary, history, list.Id, HttpMethod.Get, basePath + "?eventType=part.created");
        var page = await ReadOkAsync(listed);
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        var recordId = item.GetProperty("id").GetString()!;
        foreach (var hidden in new[] { "FieldsJson", "TraceId", "Configuration", id, actor.AccountId })
            Assert.DoesNotContain(hidden, item.GetRawText(), StringComparison.Ordinal);
        foreach (var entered in declarations.Where(item => Operation(item) == "enter"))
        {
            using var check = await SendAsync(fixture, ordinary, history, entered.Id, HttpMethod.Get, basePath + "/check/enter");
            Assert.Equal(HttpStatusCode.NoContent, check.StatusCode);
        }
        foreach (var declaration in declarations.Where(item => Operation(item) is not ("enter" or "list")))
        {
            var operation = Operation(declaration);
            var path = operation is "preview" or "detail" ? basePath + "/check/" + operation : basePath + "/" + operation;
            var selected = declaration.ActionKey.EndsWith("Selected", StringComparison.Ordinal);
            using var response = await SendAsync(fixture, ordinary, history, declaration.Id,
                operation is "print" or "export" ? HttpMethod.Post : HttpMethod.Get, path,
                operation is "print" or "export" ? new ObjectHistoryOutputRequest(selected ? "selected" : "all", new(EventType: "part.created"), selected ? [recordId] : null) : null);
            Assert.Equal(operation is "preview" or "detail" ? HttpStatusCode.NoContent : HttpStatusCode.OK, response.StatusCode);
        }
        var detail = Assert.Single(declarations, item => Operation(item) == "detail");
        using (var response = await SendAsync(fixture, ordinary, history, detail.Id, HttpMethod.Get, basePath + "/" + recordId)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var other = await SendAsync(fixture, ordinary, history, list.Id, HttpMethod.Get, "/api/part/parts/" + otherId + "/history?eventType=part.created");
        var foreignRecord = Assert.Single((await ReadOkAsync(other)).GetProperty("items").EnumerateArray()).GetProperty("id").GetString()!;
        using (var crossObject = await SendAsync(fixture, ordinary, history, detail.Id, HttpMethod.Get, basePath + "/" + foreignRecord)) Assert.Equal(HttpStatusCode.NotFound, crossObject.StatusCode);
        foreach (var output in declarations.Where(item => item.ActionKey.EndsWith("Selected", StringComparison.Ordinal)))
        {
            using var crossObject = await SendAsync(fixture, ordinary, history, output.Id, HttpMethod.Post, basePath + "/" + Operation(output),
                new ObjectHistoryOutputRequest("selected", new(), [recordId, foreignRecord]));
            Assert.Equal(HttpStatusCode.NotFound, crossObject.StatusCode);
        }
        foreach (var invalid in new string?[] { null, "unregistered", detail.Id })
        {
            using var response = await SendAsync(fixture, ordinary, history, invalid, HttpMethod.Get, basePath);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using (var missing = await SendAsync(fixture, ordinary, history, list.Id, HttpMethod.Get, basePath.Replace(id, "Part_missing", StringComparison.Ordinal))) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using (var forgedScope = await SendAsync(fixture, ordinary, history, list.Id, HttpMethod.Get, basePath + "?queryScope=forged")) Assert.Equal(HttpStatusCode.BadRequest, forgedScope.StatusCode);
        var read = fixture.FeatureCatalog.Single(point => point.ModuleId == "Part" && point.Kind == "navigation" && point.Key == "part.parts");
        var reader = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "PartNoHistory302", "只读零组件", "part-no-history302", "零组件只读账号", read);
        using var readOnly = reader.Client;
        using var deniedFeature = await SendAsync(fixture, readOnly, history, list.Id, HttpMethod.Get, basePath);
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
        await ConfigureAsync(fixture, admin, id, "allow");
        var declarations = fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog.Where(item => item.ModuleId == "Part" && (item.Execution == "part.history.list" || item.BusinessBinding == "part.part.history")).ToArray();
        var basePath = "/api/part/parts/" + id + "/history";
        var list = Assert.Single(declarations, item => Operation(item) == "list");
        using var listed = await SendAsync(fixture, ordinary, history, list.Id, HttpMethod.Get, basePath);
        var recordId = (await ReadOkAsync(listed)).GetProperty("items")[0].GetProperty("id").GetString()!;
        var output = Assert.Single(declarations, item => item.ActionKey == "exportAll");
        fixture.Fault.Mode = "query";
        try
        {
            foreach (var operation in new[] { "list", "detail", "export", "print" })
            {
                var declaration = declarations.First(item => Operation(item) == operation);
                using var failure = await SendAsync(fixture, ordinary, history, declaration.Id, operation is "export" or "print" ? HttpMethod.Post : HttpMethod.Get,
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
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(fixture, ordinary, history, output.Id, HttpMethod.Post, basePath + "/export",
                    new ObjectHistoryOutputRequest("all", new()), canceled.Token));
                Assert.True(cancellation.Fired);
            }
            finally { cancellation.Cancel = null; }
        }
        var enter = declarations.First(item => Operation(item) == "enter");
        using (var check = await SendAsync(fixture, ordinary, history, enter.Id, HttpMethod.Get, basePath + "/check/enter")) Assert.Equal(HttpStatusCode.NoContent, check.StatusCode);
        await ConfigureAsync(fixture, admin, id, "deny");
        foreach (var declaration in declarations)
        {
            var operation = Operation(declaration);
            var path = basePath + (operation is "enter" or "preview" ? "/check/" + operation : operation == "list" ? "" : operation == "detail" ? "/" + recordId : "/" + operation);
            using var denied = await SendAsync(fixture, ordinary, history, declaration.Id, operation is "export" or "print" ? HttpMethod.Post : HttpMethod.Get,
                path, operation is "export" or "print" ? new ObjectHistoryOutputRequest(declaration.ActionKey.EndsWith("Selected", StringComparison.Ordinal) ? "selected" : "all", new(), declaration.ActionKey.EndsWith("Selected", StringComparison.Ordinal) ? [recordId] : null) : null);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
    }

    private static async Task<string> CreateAsync(BusinessAuditHostFixture fixture, HttpClient admin, string code)
    {
        var point = Point(fixture, "part.part.create");
        var created = await FeatureHttpTestRequests.CreateAsync(admin, point, "/api/part/parts", new { ECODE = code, ENAME = "零组件履历" },
            new FeatureHttpTestRequests(fixture).Declaration("part.part.create", point));
        return created.GetProperty("fields").GetProperty("EID").GetString()!;
    }

    private static async Task ConfigureAsync(BusinessAuditHostFixture fixture, HttpClient admin, string id, string read)
    {
        var manage = fixture.FeatureCatalog.Single(point => point.ModuleId == "Part" && point.Key == "authorize" && point.ActionScope == "list.rowActions");
        using var saved = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, "/api/part/parts/" + id + "/permissions",
            new DataScopeObjectDto("part.part", id, [new("part302", "all", null, 1, new Dictionary<string, string> { ["read"] = read })]),
            OwnedResourceReadHttpRequests.ForExecution(fixture, manage, "part.part.authorization.manage", "authorization.object.manage", "part.part"));
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
    }

    private static string Operation(ActionDataAccessRequirement declaration) => declaration.Surface == "read" ? "list"
        : declaration.ActionKey switch
        {
            "history" or "part.object.history" => "enter", "view" => "detail", "output" => "preview",
            "exportAll" or "exportSelected" => "export", "printAll" or "printSelected" => "print",
            _ => throw new InvalidOperationException("Unexpected history button.")
        };
    private static async Task<HttpResponseMessage> SendAsync(BusinessAuditHostFixture fixture, HttpClient client, FeaturePoint feature, string? declaration, HttpMethod method, string path,
        object? body = null, CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-NewP-Feature", feature.Position);
        if (declaration is not null) request.Headers.Add(fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog.SingleOrDefault(item => item.Id == declaration)?.BusinessBinding is null ? "X-NewP-Data-Declaration" : "X-NewP-Button", declaration);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request, token);
    }
}
