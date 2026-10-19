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

    internal static async Task<JsonElement> ReadAsync(BusinessAuditHostFixture fixture, HttpClient client, FeaturePoint point,
        string resource, string operation, string path)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, point, HttpMethod.Get, path,
            dataAccess: Declaration(fixture, point, resource, operation));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
