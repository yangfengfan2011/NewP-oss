using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Testing.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

/// <summary>测试调用方显式选择正式声明位置；不按 URL 猜测动作或修改客户端默认请求头。</summary>
internal sealed class FeatureHttpTestRequests(IReadOnlyList<FeaturePoint> points)
{
    private IReadOnlyList<ActionDataAccessRequirement> _declarations = [];

    internal FeatureHttpTestRequests(IReadOnlyList<FeaturePoint> points, IReadOnlyList<ActionDataAccessRequirement> declarations) : this(points)
    {
        _declarations = declarations;
    }

    internal FeatureHttpTestRequests(BusinessAuditHostFixture fixture) : this(fixture.FeatureCatalog)
    {
        _declarations = fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog;
    }

    internal FeatureHttpTestRequests(FeatureCatalogHostFixture fixture) : this(fixture.Catalog, fixture.DataAccessCatalog) { }

    internal static async Task<FeatureHttpTestRequests> LoadAsync(HttpClient client)
    {
        using var manifest = await client.GetFromJsonAsync<JsonDocument>("/plugins/metadata-runtime-manifest.json")
            ?? throw new InvalidOperationException("Runtime metadata manifest was empty.");
        return new(RuntimeFeatureCatalogTestSupport.Parse(manifest.RootElement))
        {
            _declarations = manifest.RootElement.GetProperty("modules").EnumerateArray()
                .Where(module => module.TryGetProperty("dataAccess", out _))
                .SelectMany(module => JsonSerializer.Deserialize<ActionDataAccessRequirement[]>(module.GetProperty("dataAccess"),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? []).ToArray()
        };
    }

    // 调用方固定实际执行与功能点；不按URL或缺失请求头选择声明，反例仍可直接SendAsync不带声明。
    internal ActionDataAccessRequirement Declaration(string execution, FeaturePoint point) =>
        _declarations.Single(item => (item.Execution == execution || item.BusinessBinding == execution) && item.FeaturePositions.Contains(point.Position));

    // 读取目录与同位置导航按钮独立；调用者明确选择原执行，不按业务绑定挑一个按钮替代。
    internal ActionDataAccessRequirement ReadDeclaration(string execution, FeaturePoint point) =>
        _declarations.Single(item => item.Execution == execution && item.BusinessBinding is null && item.Surface == "read"
            && item.FeaturePositions.Contains(point.Position));

    internal FeaturePoint Organization(string tab, string? action = null, string? surface = null, string? nodeType = null)
    {
        var entry = points.Single(point => point.ModuleId == "Organization" && point.Kind == "tab"
            && point.Key == tab && points.Single(parent => parent.Position == point.ParentPosition).Key == "organization.structure");
        return action is null ? entry : points.Single(point => point.ParentPosition == entry.Position
            && point.Key == action && point.ActionScope == surface && point.StaticScope == nodeType);
    }

    internal FeaturePoint Identity(string? action = null, string? surface = null)
    {
        var entry = points.Single(point => point.ModuleId == "Identity" && point.Kind == "navigation" && point.Key == "identity.accounts");
        return action is null ? entry : points.Single(point => point.ParentPosition == entry.Position && point.Key == action
            && point.ActionScope == (surface ?? (action == "create" ? "list.pageActions" : "list.rowActions")));
    }

    internal FeaturePoint Example(string? action = null, string? surface = null)
    {
        var entry = points.Single(point => point.ModuleId == "Example" && point.Kind == "navigation" && point.Key == "example.records");
        return action is null ? entry : points.Single(point => point.ParentPosition == entry.Position && point.Key == action
            && point.ActionScope == (surface ?? (action == "export" ? "list" : action == "create" ? "list.pageActions" : "list.rowActions")));
    }

    internal FeaturePoint Audit() => points.Single(point => point.ModuleId == "Audit" && point.Key == "audit.changes" && point.Kind == "navigation");

    internal FeaturePoint Role(string? action = null, string? surface = null)
    {
        var entry = points.Single(point => point.ModuleId == "Authorization" && point.Kind == "navigation" && point.Key == "authorization.roles");
        return action is null ? entry : points.Single(point => point.ParentPosition == entry.Position && point.Key == action
            && point.ActionScope == (surface ?? (action == "create" ? "list.pageActions" : "list.rowActions")));
    }

    internal FeaturePoint Workbench(string behavior) => points.Single(point => point.ModuleId == "Authorization" && point.BehaviorKey == behavior);

    internal static Task<HttpResponseMessage> SendAsync(HttpClient client, FeaturePoint point, HttpMethod method, string path, object? body = null, ActionDataAccessRequirement? dataAccess = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-NewP-Feature", point.Position);
        if (dataAccess is not null) request.Headers.Add(dataAccess.BusinessBinding is null ? "X-NewP-Data-Declaration" : "X-NewP-Button", dataAccess.Id);
        if (body is not null) request.Content = JsonContent.Create(body, options: new JsonSerializerOptions());
        return SendAndDisposeAsync(client, request);
    }

    private static async Task<HttpResponseMessage> SendAndDisposeAsync(HttpClient client, HttpRequestMessage request)
    {
        using (request) return await client.SendAsync(request);
    }

    // 正向创建使用本夹具已保存的真实目录；未知或歧义先失败，不触发HTTP，也不按URL猜测身份。
    internal Task<JsonElement> CreateButtonAsync(HttpClient client, FeaturePoint point, string path, object body,
        ActionDataAccessRequirement? dataAccess = null) =>
        CreateAsync(client, point, path, body, dataAccess ?? Declaration(point.BehaviorKey, point));

    // 仅显式正向或业务反例准备采用按钮；遗漏/借用身份反例仍直接调用原始SendAsync。
    internal Task<HttpResponseMessage> SendButtonAsync(HttpClient client, FeaturePoint point, HttpMethod method, string path,
        object? body = null) => SendAsync(client, point, method, path, body, _declarations.Single(button =>
            button.BusinessBinding == point.BehaviorKey && button.FeaturePositions.Contains(point.Position)));

    internal async Task<JsonElement> InitializeOrganizationAsync(HttpClient client)
    {
        var point = Organization("companies", "initialize", "list.pageActions");
        using var response = await SendButtonAsync(client, point, HttpMethod.Post, "/api/organization/initialization");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    internal static async Task<JsonElement> CreateAsync(HttpClient client, FeaturePoint point, string path, object body, ActionDataAccessRequirement? dataAccess = null)
    {
        using var response = await SendAsync(client, point, HttpMethod.Post, path, body, dataAccess);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    internal static async Task<string> ReadTextAsync(HttpClient client, FeaturePoint point, string path, ActionDataAccessRequirement? dataAccess = null)
    {
        using var response = await SendAsync(client, point, HttpMethod.Get, path, dataAccess: dataAccess);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var failure = await response.Content.ReadAsStringAsync();
            var headers = failure.IndexOf("\nHEADERS", StringComparison.Ordinal);
            Assert.Fail(headers < 0 ? failure : failure[..headers]);
        }
        return await response.Content.ReadAsStringAsync();
    }

    internal static async Task<JsonElement> ReadAsync(HttpClient client, FeaturePoint point, string path, ActionDataAccessRequirement? dataAccess = null) =>
        JsonSerializer.Deserialize<JsonElement>(await ReadTextAsync(client, point, path, dataAccess));

    internal async Task GrantAsync(HttpClient admin, string roleId, params FeaturePoint[] points)
    {
        var positions = RuntimeFeatureCatalogTestSupport.ExpandGrantSet(points);
        using var response = await SendAsync(admin, Workbench("authorization.workbench.save"), HttpMethod.Put,
            $"/api/authorization/workbench/roles/{roleId}", new { positions },
            Declaration("authorization.workbench.save", Workbench("authorization.workbench.save")));
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }
}
