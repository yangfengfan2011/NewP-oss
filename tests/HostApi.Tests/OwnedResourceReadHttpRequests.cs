using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

/// <summary>读取调用方明确选择标准操作，以实际catalog匹配声明和功能位置，不从URL推测或补缺失声明。</summary>
internal static class OwnedResourceReadHttpRequests
{
    internal static ActionDataAccessRequirement Declaration(BusinessAuditHostFixture fixture, FeaturePoint point, string resource, string operation) =>
        Assert.Single(fixture.Services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog, item =>
            item.Execution == resource + "." + operation && item.Provider == "standardCrud." + operation
            && item.Resources.SequenceEqual([resource]) && item.FeaturePositions.Contains(point.Position, StringComparer.Ordinal));

    /// <summary>真实进程准备从当前manifest选择已知操作；不缓存旧发行声明或按URL推断绑定。</summary>
    internal static async Task<ActionDataAccessRequirement> DeclarationAsync(HttpClient client, FeaturePoint point, string resource, string operation)
    {
        using var manifest = await client.GetFromJsonAsync<JsonDocument>("/plugins/metadata-runtime-manifest.json")
            ?? throw new InvalidOperationException("Runtime metadata manifest was empty.");
        var declarations = manifest.RootElement.GetProperty("modules").EnumerateArray()
            .SelectMany(module => module.GetProperty("dataAccess").EnumerateArray())
            .Select(item => item.Deserialize<ActionDataAccessRequirement>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!).ToArray();
        return Assert.Single(declarations, item => item.ModuleId == point.ModuleId
            && item.Execution == resource + "." + operation && item.Provider == "standardCrud." + operation
            && item.Resources.SequenceEqual([resource]) && item.FeaturePositions.Contains(point.Position, StringComparer.Ordinal));
    }

    internal static async Task<JsonElement> ReadAsync(BusinessAuditHostFixture fixture, HttpClient client, FeaturePoint point,
        string resource, string operation, string path)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, point, HttpMethod.Get, path,
            dataAccess: Declaration(fixture, point, resource, operation));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
