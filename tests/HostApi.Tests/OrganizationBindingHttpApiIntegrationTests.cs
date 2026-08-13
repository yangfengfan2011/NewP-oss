using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationBindingHttpApiIntegrationTests
{
    [Fact]
    public async Task FormalHttpRoutes_SearchCandidatesAndBindWithAuthenticatedActor()
    {
        var service = new RecordingBindingService();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(PersonUserBindingsController).Assembly);
        builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, static _ => { });
        builder.Services.AddAuthorization(options => options.AddPolicy(
            "Permission:Organization.Person.LinkUser",
            policy => policy.RequireAuthenticatedUser()));
        builder.Services.AddSingleton<IPersonUserBindingService>(service);

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var candidateRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/organization/persons/Person_1/user-binding/candidates?query=alice");
        candidateRequest.Headers.Add("X-Test-User", "User_Admin");
        var candidateResponse = await client.SendAsync(candidateRequest);
        candidateResponse.EnsureSuccessStatusCode();
        var candidates = await candidateResponse.Content.ReadFromJsonAsync<PersonUserBindingCandidateDto[]>();
        var candidate = Assert.Single(candidates!);
        Assert.Equal("alice", candidate.ECODE);
        Assert.Equal("Person_1", service.CandidatePersonEid);
        Assert.Equal("alice", service.CandidateQuery);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/organization/persons/Person_1/user-binding")
        {
            Content = JsonContent.Create(new BindPersonUserRequest("User_1", 7, "正式 HTTP 绑定"))
        };
        request.Headers.Add("X-Test-User", "User_Admin");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var binding = await response.Content.ReadFromJsonAsync<PersonUserBindingDto>();
        Assert.Equal("User_1", binding?.AccountEid);
        Assert.Equal("User_Admin", service.Actor?.ActorId);
        Assert.False(string.IsNullOrWhiteSpace(service.Actor?.TraceId));

        using var invalidRevision = new HttpRequestMessage(HttpMethod.Put, "/api/organization/persons/Person_2/user-binding")
        {
            Content = new StringContent(
                """{"accountEid":"User_2","expectedRevision":-1,"reason":"invalid revision"}""",
                Encoding.UTF8,
                "application/json")
        };
        invalidRevision.Headers.Add("X-Test-User", "User_Admin");
        var invalidRevisionResponse = await client.SendAsync(invalidRevision);

        Assert.Equal(HttpStatusCode.BadRequest, invalidRevisionResponse.StatusCode);
        Assert.Equal("application/problem+json", invalidRevisionResponse.Content.Headers.ContentType?.MediaType);
        using var invalidRevisionProblem = JsonDocument.Parse(await invalidRevisionResponse.Content.ReadAsStringAsync());
        Assert.Equal("REQUEST_VALIDATION_FAILED", invalidRevisionProblem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(invalidRevisionProblem.RootElement.GetProperty("traceId").GetString()));
        Assert.True(invalidRevisionProblem.RootElement.GetProperty("errors").TryGetProperty("ExpectedRevision", out _));
    }

    private sealed class RecordingBindingService : IPersonUserBindingService
    {
        public string? CandidatePersonEid { get; private set; }
        public string? CandidateQuery { get; private set; }
        public PersonUserBindingActor? Actor { get; private set; }

        public ValueTask<IReadOnlyList<PersonUserBindingCandidateDto>> SearchCandidatesAsync(
            string personEid,
            string? query,
            CancellationToken cancellationToken = default)
        {
            CandidatePersonEid = personEid;
            CandidateQuery = query;
            return ValueTask.FromResult<IReadOnlyList<PersonUserBindingCandidateDto>>([
                new("User_1", "alice", "Alice", "Enabled", 7)
            ]);
        }

        public ValueTask<PersonUserBindingDto> BindAsync(
            string personEid,
            BindPersonUserRequest request,
            PersonUserBindingActor actor,
            CancellationToken cancellationToken = default)
        {
            Actor = actor;
            return ValueTask.FromResult(new PersonUserBindingDto(
                personEid,
                request.AccountEid,
                DateTime.UtcNow,
                actor.ActorId,
                request.Reason,
                new PersonUserBindingAccountDto(request.AccountEid, "alice", "Alice", "Enabled", 8)));
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "OrganizationBindingApiTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var accountEid))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, accountEid.ToString())],
                SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
