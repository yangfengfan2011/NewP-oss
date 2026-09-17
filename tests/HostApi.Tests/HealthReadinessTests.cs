using System.Net;
using System.Text.Json;
using ICP.Platform.HostApi.Controllers;
using ICP.Platform.HostApi.Readiness;
using ICP.Platform.Persistence.Transactions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class HealthReadinessTests
{
    [Fact]
    public async Task NamedCacheReadiness_ReportsPublishedCountAndUnknownWithoutReadingData()
    {
        var cache = new MutableCacheStatus();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(HealthController).Assembly);
        builder.Services.AddHostReadiness();
        builder.Services.AddSingleton<IPlatformCacheStatus>(cache);
        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();
        foreach (var count in new long?[] { null, 0, 4, null })
        {
            cache.RecordCount = count;
            using var response = await client.GetAsync("/api/health/ready");
            Assert.Equal(count.HasValue ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var stage = Assert.Single(body.RootElement.GetProperty("stages").EnumerateArray());
            Assert.Equal("named-caches", stage.GetProperty("name").GetString());
            Assert.Contains($"Fixture: {count?.ToString() ?? "unknown"}", stage.GetProperty("diagnostic").GetString());
        }
    }

    private sealed class MutableCacheStatus : IPlatformCacheStatus
    {
        public string Name => "Fixture";
        public bool IsAvailable => RecordCount.HasValue;
        public long? RecordCount { get; set; }
    }

    [Fact]
    public void Get_LivenessRemainsIndependentFromReadinessContributors()
    {
        var controller = new HealthController();

        var response = Assert.IsType<OkObjectResult>(controller.Get());

        Assert.NotNull(response.Value);
    }

    [Fact]
    public async Task LivenessEndpoint_DoesNotRequireReadinessServices()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(HealthController).Assembly);

        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoints_ExposeIndependentLivenessAndReadinessContracts()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(HealthController).Assembly);
        builder.Services.AddSingleton<IReadinessEvaluator>(new ReadinessEvaluator(
        [
            FakeContributor.Required(
                "host",
                10,
                _ => ValueTask.FromResult(ReadinessContribution.Ready("ready")))
        ]));

        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        var client = app.GetTestClient();

        var liveness = await client.GetAsync("/api/health");
        var readiness = await client.GetAsync("/api/health/ready");
        using var readinessJson = JsonDocument.Parse(await readiness.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        Assert.Equal("ready", readinessJson.RootElement.GetProperty("status").GetString());
        Assert.Single(readinessJson.RootElement.GetProperty("stages").EnumerateArray());
    }

    [Fact]
    public async Task GetReady_WhenNoRequiredContributorsExist_ReturnsServiceUnavailable()
    {
        var evaluator = new ReadinessEvaluator([]);
        var controller = new HealthController();

        var response = Assert.IsType<ObjectResult>(await controller.GetReady(evaluator, CancellationToken.None));
        var body = Assert.IsType<ReadinessResponseDto>(response.Value);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal("indeterminate", body.Status);
        Assert.Equal(ReadinessFailureCodes.RequiredContributorsMissing, body.FailureCode);
        Assert.Empty(body.Stages);
    }

    [Theory]
    [InlineData(ReadinessState.Starting, "HOST_STARTING", "starting")]
    [InlineData(ReadinessState.Failed, "HOST_FAILED", "failed")]
    [InlineData(ReadinessState.Indeterminate, "HOST_INDETERMINATE", "indeterminate")]
    public async Task GetReady_WhenRequiredContributorIsNotReady_ReturnsStableServiceUnavailable(
        ReadinessState state,
        string failureCode,
        string expectedStatus)
    {
        var contributor = FakeContributor.Required(
            "host",
            order: 10,
            _ => ValueTask.FromResult(new ReadinessContribution(state, failureCode, "sanitized diagnostic")));
        var evaluator = new ReadinessEvaluator([contributor]);
        var controller = new HealthController();

        var response = Assert.IsType<ObjectResult>(await controller.GetReady(evaluator, CancellationToken.None));
        var body = Assert.IsType<ReadinessResponseDto>(response.Value);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal(expectedStatus, body.Status);
        Assert.Equal(failureCode, body.FailureCode);
        var stage = Assert.Single(body.Stages);
        Assert.Equal("sanitized diagnostic", stage.Diagnostic);
    }

    [Fact]
    public async Task GetReady_WhenAllRequiredContributorsAreReady_ReturnsOrderedSuccess()
    {
        var invocationOrder = new List<string>();
        var contributors = new IReadinessContributor[]
        {
            FakeContributor.Required("manifest", 20, _ => Ready("manifest", invocationOrder)),
            FakeContributor.Optional("telemetry", 5, _ => Failed("telemetry", invocationOrder)),
            FakeContributor.Required("database", 10, _ => Ready("database", invocationOrder))
        };
        var evaluator = new ReadinessEvaluator(contributors);
        var controller = new HealthController();

        var response = Assert.IsType<OkObjectResult>(await controller.GetReady(evaluator, CancellationToken.None));
        var body = Assert.IsType<ReadinessResponseDto>(response.Value);

        Assert.Equal("ready", body.Status);
        Assert.Null(body.FailureCode);
        Assert.Equal(["telemetry", "database", "manifest"], invocationOrder);
        Assert.Equal(["telemetry", "database", "manifest"], body.Stages.Select(stage => stage.Name));
    }

    [Fact]
    public async Task GetReady_WhenSupervisorIdentityIsConfigured_ExposesOnlyStableOwnershipFields()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DevSupervisor:RepositoryId"] = "repo-a",
                ["DevSupervisor:WorktreeId"] = "worktree-a",
                ["DevSupervisor:RunId"] = "run-a",
                ["DevSupervisor:EndpointTokenFingerprint"] = "token-fingerprint-a"
            })
            .Build();
        var evaluator = new ReadinessEvaluator(
        [
            FakeContributor.Required(
                "host",
                10,
                _ => ValueTask.FromResult(ReadinessContribution.Ready("ready")))
        ]);
        var controller = new HealthController(configuration);

        var response = Assert.IsType<OkObjectResult>(await controller.GetReady(evaluator, CancellationToken.None));
        var body = Assert.IsType<ReadinessResponseDto>(response.Value);

        Assert.NotNull(body.Supervisor);
        Assert.Equal("repo-a", body.Supervisor.RepositoryId);
        Assert.Equal("worktree-a", body.Supervisor.WorktreeId);
        Assert.Equal("run-a", body.Supervisor.RunId);
        Assert.Equal("token-fingerprint-a", body.Supervisor.EndpointTokenFingerprint);
        Assert.DoesNotContain("Password", JsonSerializer.Serialize(body), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetReady_WhenContributorThrows_DoesNotExposeExceptionDetails()
    {
        const string sensitiveException = "Server=private;Password=REDACTED_FOR_OPEN_SOURCE; D:\\deploy\\plugin";
        var contributor = FakeContributor.Required(
            "database",
            10,
            _ => throw new InvalidOperationException(sensitiveException));
        var evaluator = new ReadinessEvaluator([contributor]);
        var controller = new HealthController();

        var response = Assert.IsType<ObjectResult>(await controller.GetReady(evaluator, CancellationToken.None));
        var body = Assert.IsType<ReadinessResponseDto>(response.Value);
        var serialized = System.Text.Json.JsonSerializer.Serialize(body);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal(ReadinessFailureCodes.ContributorException, body.FailureCode);
        Assert.DoesNotContain(sensitiveException, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("D:\\deploy", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvaluateAsync_WhenHostRequestIsCancelled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var contributor = FakeContributor.Required(
            "host",
            10,
            token => new ValueTask<ReadinessContribution>(Task.FromCanceled<ReadinessContribution>(token)));
        var evaluator = new ReadinessEvaluator([contributor]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => evaluator.EvaluateAsync(cancellation.Token));
    }

    [Fact]
    public async Task EvaluateAsync_WhenCancellationStartsDuringLastCheck_DoesNotReturnReady()
    {
        using var cancellation = new CancellationTokenSource();
        var contributor = FakeContributor.Required(
            "host",
            10,
            _ =>
            {
                cancellation.Cancel();
                return ValueTask.FromResult(ReadinessContribution.Ready());
            });
        var evaluator = new ReadinessEvaluator([contributor]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => evaluator.EvaluateAsync(cancellation.Token));
    }

    [Fact]
    public void AddHostReadiness_RegistersStableEvaluatorInterface()
    {
        var services = new ServiceCollection();

        services.AddHostReadiness();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<ReadinessEvaluator>(provider.GetRequiredService<IReadinessEvaluator>());
    }

    private static ValueTask<ReadinessContribution> Ready(string name, ICollection<string> invocationOrder)
    {
        invocationOrder.Add(name);
        return ValueTask.FromResult(ReadinessContribution.Ready("ready"));
    }

    private static ValueTask<ReadinessContribution> Failed(string name, ICollection<string> invocationOrder)
    {
        invocationOrder.Add(name);
        return ValueTask.FromResult(new ReadinessContribution(ReadinessState.Failed, "OPTIONAL_FAILED", "optional"));
    }

    private sealed class FakeContributor(
        string name,
        int order,
        bool isRequired,
        Func<CancellationToken, ValueTask<ReadinessContribution>> check) : IReadinessContributor
    {
        public string Name { get; } = name;

        public int Order { get; } = order;

        public bool IsRequired { get; } = isRequired;

        public ValueTask<ReadinessContribution> CheckAsync(CancellationToken cancellationToken) =>
            check(cancellationToken);

        public static FakeContributor Required(
            string name,
            int order,
            Func<CancellationToken, ValueTask<ReadinessContribution>> check) =>
            new(name, order, isRequired: true, check);

        public static FakeContributor Optional(
            string name,
            int order,
            Func<CancellationToken, ValueTask<ReadinessContribution>> check) =>
            new(name, order, isRequired: false, check);
    }
}
