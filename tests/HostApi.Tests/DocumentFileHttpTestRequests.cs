using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

/// <summary>旧 Document 文件消费者显式采用本视图正式声明；不按 URL 或目录首项推断绑定。</summary>
internal static class DocumentFileHttpTestRequests
{
    internal static Task<HttpResponseMessage> SendAsync(BusinessAuditHostFixture fixture, HttpClient client,
        FeaturePoint point, HttpMethod method, string path)
    {
        var execution = point.BehaviorKey switch
        {
            "document.document.read" => "document.document.read",
            "document.document.download" => "document.files.download",
            _ => throw new InvalidOperationException("Not a Document file read feature.")
        };
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.ModuleId == "Document"
            && item.ViewKey == "document.document.detail" && item.Execution == execution
            && item.FeaturePositions.Contains(point.Position));
        return FeatureHttpTestRequests.SendAsync(client, point, method, path, dataAccess: declaration);
    }

    internal static async Task<JsonElement> ReadAsync(BusinessAuditHostFixture fixture, HttpClient client, FeaturePoint point, string path)
    {
        using var response = await SendAsync(fixture, client, point, HttpMethod.Get, path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
