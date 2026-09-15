using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Authorization.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ICP.Platform.Authorization.Tests;

public sealed class PermissionHttpAdapterTests
{
    [Theory]
    [InlineData(null, PermissionDecision.Granted, HttpStatusCode.Unauthorized, 0, 0)]
    [InlineData("trusted-subject", PermissionDecision.Granted, HttpStatusCode.OK, 1, 1)]
    [InlineData("trusted-subject", PermissionDecision.Denied, HttpStatusCode.Forbidden, 0, 1)]
    [InlineData("trusted-subject", PermissionDecision.Indeterminate, HttpStatusCode.ServiceUnavailable, 0, 1)]
    public async Task RequireFeature_MapsDecisionBeforeBusinessInvocation(
        string? subjectId,
        PermissionDecision decision,
        HttpStatusCode expectedStatus,
        int expectedBusinessCalls,
        int expectedDecisionCalls)
    {
        var source = new RecordingPermissionDecisionSource(decision);
        await using var scenario = await PermissionHttpScenario.StartAsync(source);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        if (subjectId is not null)
        {
            request.Headers.Add(TestAuthenticationHandler.SubjectHeader, subjectId);
        }

        using var response = await scenario.Client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedBusinessCalls, scenario.BusinessCalls);
        Assert.Equal(expectedDecisionCalls, source.CallCount);
        if (expectedStatus is HttpStatusCode.ServiceUnavailable)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            Assert.Contains(PermissionGateFailureCodes.DecisionUnavailable, responseBody, StringComparison.Ordinal);
            Assert.DoesNotContain("storage", responseBody, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task RequireFeature_UsesTrustedClaimInsteadOfRequestUserId()
    {
        var source = new RecordingPermissionDecisionSource(PermissionDecision.Granted);
        await using var scenario = await PermissionHttpScenario.StartAsync(source);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/protected?userId=attacker");
        request.Headers.Add(TestAuthenticationHandler.SubjectHeader, "trusted-subject");

        using var response = await scenario.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("trusted-subject", source.LastSubjectId);
        Assert.Equal("organization.person.read", source.LastPermissionCode);
    }

    private sealed class PermissionHttpScenario : IAsyncDisposable
    {
        private readonly WebApplication _application;
        private int _businessCalls;

        private PermissionHttpScenario(WebApplication application)
        {
            _application = application;
        }

        public HttpClient Client { get; private set; } = null!;

        public int BusinessCalls => Volatile.Read(ref _businessCalls);

        public static async Task<PermissionHttpScenario> StartAsync(IFeatureDecisionSource source)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services
                .AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    static _ => { });
            builder.Services.AddPlatformAuthorization();
            builder.Services.AddSingleton(source);

            var application = builder.Build();
            var scenario = new PermissionHttpScenario(application);
            application.UseAuthentication();
            application.UseAuthorization();
            application.MapGet("/protected", () =>
                {
                    Interlocked.Increment(ref scenario._businessCalls);
                    return Results.Ok();
                })
                .RequireAuthorization(new RequireFeatureAttribute("organization.person.read"));
            await application.StartAsync();
            scenario.Client = application.GetTestClient();
            scenario.Client.DefaultRequestHeaders.Add("X-NewP-Feature", "declared.read");
            return scenario;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _application.DisposeAsync();
        }
    }

    private sealed class RecordingPermissionDecisionSource : IFeatureDecisionSource
    {
        private readonly PermissionDecision _decision;
        private int _callCount;

        public RecordingPermissionDecisionSource(PermissionDecision decision)
        {
            _decision = decision;
        }

        public int CallCount => Volatile.Read(ref _callCount);

        public string? LastSubjectId { get; private set; }

        public string? LastPermissionCode { get; private set; }

        public ValueTask<PermissionDecision> EvaluateAsync(
            string subjectId,
            string position,
            string behaviorKey,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            LastSubjectId = subjectId;
            LastPermissionCode = behaviorKey;
            return ValueTask.FromResult(_decision);
        }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "PermissionTests";
        public const string SubjectHeader = "X-Test-Subject";

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(SubjectHeader, out var subjectId)
                || string.IsNullOrWhiteSpace(subjectId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, subjectId.ToString())],
                SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
