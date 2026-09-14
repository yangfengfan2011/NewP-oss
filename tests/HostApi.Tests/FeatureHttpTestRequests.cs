using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

/// <summary>测试调用方显式选择正式声明位置；不按 URL 猜测动作或修改客户端默认请求头。</summary>
internal sealed class FeatureHttpTestRequests(IReadOnlyList<FeaturePoint> points)
{
    internal FeatureHttpTestRequests(BusinessAuditHostFixture fixture) : this(fixture.FeatureCatalog) { }

    internal static async Task<FeatureHttpTestRequests> LoadAsync(HttpClient client)
    {
        using var manifest = await client.GetFromJsonAsync<JsonDocument>("/plugins/metadata-runtime-manifest.json")
            ?? throw new InvalidOperationException("Runtime metadata manifest was empty.");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return new(manifest.RootElement.GetProperty("modules").EnumerateArray()
            .SelectMany(module => module.GetProperty("features").Deserialize<FeaturePoint[]>(options)
                ?? throw new InvalidOperationException("Runtime feature catalog was empty.")).ToArray());
    }

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

    internal static Task<HttpResponseMessage> SendAsync(HttpClient client, FeaturePoint point, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-NewP-Feature", point.Position);
        if (body is not null) request.Content = JsonContent.Create(body, options: new JsonSerializerOptions());
        return SendAndDisposeAsync(client, request);
    }

    private static async Task<HttpResponseMessage> SendAndDisposeAsync(HttpClient client, HttpRequestMessage request)
    {
        using (request) return await client.SendAsync(request);
    }

    internal static async Task<JsonElement> CreateAsync(HttpClient client, FeaturePoint point, string path, object body)
    {
        using var response = await SendAsync(client, point, HttpMethod.Post, path, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    internal static async Task<string> ReadTextAsync(HttpClient client, FeaturePoint point, string path)
    {
        using var response = await SendAsync(client, point, HttpMethod.Get, path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadAsStringAsync();
    }

    internal static async Task<JsonElement> ReadAsync(HttpClient client, FeaturePoint point, string path) =>
        JsonSerializer.Deserialize<JsonElement>(await ReadTextAsync(client, point, path));

    internal async Task GrantAsync(HttpClient admin, string roleId, params FeaturePoint[] points)
    {
        var positions = points.SelectMany(point => point.EntryDependencies.Append(point.Position)).Distinct().ToArray();
        using var response = await SendAsync(admin, Workbench("authorization.workbench.save"), HttpMethod.Put,
            $"/api/authorization/workbench/roles/{roleId}", new { positions });
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }
}
