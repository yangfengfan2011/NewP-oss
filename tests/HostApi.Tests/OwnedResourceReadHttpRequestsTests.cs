using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class OwnedResourceReadHttpRequestsTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("module")]
    [InlineData("execution")]
    [InlineData("provider")]
    [InlineData("resource")]
    [InlineData("feature")]
    public async Task ManifestDeclaration_SelectsOnlyTheActualOwnerExecutionProviderResourceAndFeature(string problem)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var module = JsonNode.Parse(File.ReadAllText(Path.Combine(root.FullName, "tests/Kernel.Tests/Fixtures/DocumentFileDataAccess.contract.json")))!;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var original = Assert.Single(module["dataAccess"]!.Deserialize<ActionDataAccessRequirement[]>(options)!, item => item.Execution == "document.document.create");
        var point = Assert.Single(module["features"]!.Deserialize<FeaturePoint[]>(options)!, item => original.FeaturePositions.Contains(item.Position));
        var selected = problem switch
        {
            "module" => original with { ModuleId = "Part" },
            "execution" => original with { Execution = "document.document.read" },
            "provider" => original with { Provider = "standardCrud.read" },
            "resource" => original with { Resources = ["part.part"] },
            "feature" => original with { FeaturePositions = ["not-the-actual-point"] },
            _ => original
        };
        module["dataAccess"] = JsonSerializer.SerializeToNode(problem == "missing" ? []
            : problem == "duplicate" ? new[] { selected, selected } : [selected], options);
        using var client = new HttpClient(new ManifestHandler(JsonSerializer.Serialize(new { modules = new[] { module } }, options)))
            { BaseAddress = new Uri("http://manifest-only.test") };
        if (problem == "valid")
            Assert.Equal(original.Id, (await OwnedResourceReadHttpRequests.DeclarationAsync(client, point, "document.document", "create")).Id);
        else await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() => OwnedResourceReadHttpRequests.DeclarationAsync(client, point, "document.document", "create"));
    }

    private sealed class ManifestHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/plugins/metadata-runtime-manifest.json", request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
