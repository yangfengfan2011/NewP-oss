using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Audit.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

/// <summary>履历协议测试的调用方工厂；只从正式计划/清单选择对应出口声明，不合成授权目录。</summary>
internal sealed class ObjectHistoryHttpTestRequests(IReadOnlyList<ActionDataAccessRequirement> declarations)
{
    internal ObjectHistoryHttpTestRequests(BusinessAuditHostFixture fixture) : this(fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog) { }

    internal static async Task<ObjectHistoryHttpTestRequests> LoadAsync(HttpClient client)
    {
        using var manifest = await client.GetFromJsonAsync<JsonDocument>("/plugins/metadata-runtime-manifest.json") ?? throw new InvalidOperationException("Missing runtime manifest.");
        return new(manifest.RootElement.GetProperty("modules").EnumerateArray().SelectMany(module => module.GetProperty("dataAccess").EnumerateArray())
            .Select(item => item.Deserialize<ActionDataAccessRequirement>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!).ToArray());
    }

    internal async Task<JsonElement> ReadAsync(HttpClient client, FeaturePoint point, string path)
    {
        using var response = await SendAsync(client, point, HttpMethod.Get, path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<JsonElement>(text);
    }

    internal async Task<HttpResponseMessage> SendAsync(HttpClient client, FeaturePoint point, HttpMethod method, string path, object? body = null)
    {
        var url = new Uri(new Uri("http://history-test.local"), path);
        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var moduleId = segments.Length > 4 && segments[0] == "api" && segments[4] == "history"
            ? declarations.Select(item => item.ModuleId).Distinct(StringComparer.Ordinal).SingleOrDefault(owner => owner.Equals(segments[1], StringComparison.OrdinalIgnoreCase)) : null;
        if (moduleId is null)
            return await FeatureHttpTestRequests.SendAsync(client, point, method, path, body);
        var suffix = url.AbsolutePath[(url.AbsolutePath.IndexOf("/history", StringComparison.Ordinal) + "/history".Length)..];
        var operation = suffix == "" ? "list" : suffix is "/export" or "/print" ? suffix[1..]
            : suffix.StartsWith("/check/", StringComparison.Ordinal) ? suffix[7..] : "detail";
        var actionKey = operation is "export" or "print" ? operation + (body is ObjectHistoryOutputRequest { Mode: "selected" } ? "Selected" : "All") : null;
        var declaration = declarations.Single(item => item.ModuleId == moduleId && item.Execution == moduleId.ToLowerInvariant() + ".history." + operation
            && (operation != "enter" || item.Surface == "rowActions") && (actionKey is null || item.ActionKey == actionKey));
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-NewP-Feature", point.Position);
        request.Headers.Add("X-NewP-Data-Declaration", declaration.Id);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
}
