using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Abstractions;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Exports;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationPermissionPipelineTests
{
    [Fact]
    public async Task BindingRoute_WhenPermissionDecisionUnavailable_ReturnsStableProblemDetails()
    {
        var source = new MutablePermissionDecisionSource(PermissionDecision.Indeterminate);
        await using var scenario = await Scenario.StartAsync(source);
        using var request = Request(HttpMethod.Get, "/api/organization/persons/Person_1", "subject");

        using var response = await scenario.Client.SendAsync(request);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(PermissionGateFailureCodes.DecisionUnavailable, document.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("subject", document.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, scenario.CrudService.CallCount);
    }
    [Theory]
    [InlineData(null, PermissionDecision.Granted, HttpStatusCode.Unauthorized, 0)]
    [InlineData("subject", PermissionDecision.Denied, HttpStatusCode.Forbidden, 0)]
    [InlineData("subject", PermissionDecision.Indeterminate, HttpStatusCode.ServiceUnavailable, 0)]
    [InlineData("subject", PermissionDecision.Granted, HttpStatusCode.OK, 1)]
    public async Task HandWrittenOrganizationEndpoint_EnforcesPermissionBeforeBusiness(
        string? subjectId,
        PermissionDecision decision,
        HttpStatusCode expectedStatus,
        int expectedBusinessCalls)
    {
        var source = new MutablePermissionDecisionSource(decision);
        await using var scenario = await Scenario.StartAsync(source);

        using var request = Request(HttpMethod.Get, "/api/organization/companies", subjectId);
        using var response = await scenario.Client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedBusinessCalls, scenario.CompanyService.CallCount);
        Assert.Equal(0, scenario.CrudService.CallCount);
        if (subjectId is not null)
        {
            Assert.Equal("Organization.Company.View", source.LastPermissionCode);
        }
    }

    [Theory]
    [InlineData(null, PermissionDecision.Granted, HttpStatusCode.Unauthorized, 0)]
    [InlineData("subject", PermissionDecision.Denied, HttpStatusCode.Forbidden, 0)]
    [InlineData("subject", PermissionDecision.Indeterminate, HttpStatusCode.ServiceUnavailable, 0)]
    [InlineData("subject", PermissionDecision.Granted, HttpStatusCode.OK, 1)]
    public async Task PersonStandardCrud_EnforcesPermissionBeforeBusiness(
        string? subjectId,
        PermissionDecision decision,
        HttpStatusCode expectedStatus,
        int expectedBusinessCalls)
    {
        var source = new MutablePermissionDecisionSource(decision);
        await using var scenario = await Scenario.StartAsync(source);

        using var request = Request(HttpMethod.Get, "/api/organization/persons", subjectId);
        using var response = await scenario.Client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedBusinessCalls, scenario.CrudService.CallCount);
        Assert.Equal(0, scenario.CompanyService.CallCount);
        if (subjectId is not null)
        {
            Assert.Equal("Organization.Person.View", source.LastPermissionCode);
        }
    }

    [Theory]
    [InlineData("GET", "/api/organization/persons", "Organization.Person.View")]
    [InlineData("GET", "/api/organization/persons/options", "Organization.Person.View")]
    [InlineData("POST", "/api/organization/persons/export", "Organization.Person.View")]
    [InlineData("GET", "/api/organization/persons/Person_1", "Organization.Person.View")]
    [InlineData("POST", "/api/organization/persons", "Organization.Person.Create")]
    [InlineData("PUT", "/api/organization/persons/Person_1", "Organization.Person.Update")]
    [InlineData("POST", "/api/organization/persons/Person_1/disable", "Organization.Person.Update")]
    [InlineData("DELETE", "/api/organization/persons/Person_1", "Organization.Person.Delete")]
    public async Task PersonStandardCrud_AllMappedOperationsUseFixedPermission(
        string method,
        string path,
        string expectedPermission)
    {
        var source = new MutablePermissionDecisionSource(PermissionDecision.Granted);
        await using var scenario = await Scenario.StartAsync(source);

        using var request = Request(new HttpMethod(method), path, "subject", JsonBody(path, method));
        using var response = await scenario.Client.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode, $"{method} {path} returned {(int)response.StatusCode}.");
        Assert.Equal(expectedPermission, source.LastPermissionCode);
        Assert.Equal(1, scenario.CrudService.CallCount);
        Assert.Equal(0, scenario.CompanyService.CallCount);
    }

    [Fact]
    public async Task PersonStandardCrud_RevokedPermissionTakesEffectOnNextRequest()
    {
        var source = new MutablePermissionDecisionSource(PermissionDecision.Granted);
        await using var scenario = await Scenario.StartAsync(source);

        using var grantedRequest = Request(HttpMethod.Get, "/api/organization/persons", "subject");
        using var granted = await scenario.Client.SendAsync(grantedRequest);
        source.Decision = PermissionDecision.Denied;
        using var revokedRequest = Request(HttpMethod.Get, "/api/organization/persons", "subject");
        using var revoked = await scenario.Client.SendAsync(revokedRequest);

        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.Equal(1, scenario.CrudService.CallCount);
        Assert.Equal(2, source.CallCount);
    }

    private static HttpRequestMessage Request(
        HttpMethod method,
        string path,
        string? subjectId,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        if (subjectId is not null)
        {
            request.Headers.Add(TestAuthenticationHandler.SubjectHeader, subjectId);
        }

        return request;
    }

    private static HttpContent? JsonBody(string path, string method)
    {
        if (path.EndsWith("/export", StringComparison.Ordinal))
        {
            return Json("""
                {
                  "mode": "current",
                  "query": {},
                  "columns": [{ "key": "name", "field": "ENAME", "title": "Name" }]
                }
                """);
        }

        return method is "POST" or "PUT" && !path.EndsWith("/disable", StringComparison.Ordinal)
            ? Json("{}")
            : null;
    }

    private static StringContent Json(string value) =>
        new(value, Encoding.UTF8, "application/json");

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly WebApplication _application;

        private Scenario(
            WebApplication application,
            RecordingCompanyService companyService,
            RecordingStandardCrudService crudService)
        {
            _application = application;
            CompanyService = companyService;
            CrudService = crudService;
        }

        public HttpClient Client { get; private set; } = null!;

        public RecordingCompanyService CompanyService { get; }

        public RecordingStandardCrudService CrudService { get; }

        public static async Task<Scenario> StartAsync(IPermissionDecisionSource source)
        {
            var companyService = new RecordingCompanyService();
            var crudService = new RecordingStandardCrudService();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services
                .AddControllers()
                .AddApplicationPart(typeof(CompaniesController).Assembly);
            builder.Services
                .AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    static _ => { });
            builder.Services.AddPlatformAuthorization();
            builder.Services.AddSingleton(source);
            builder.Services.AddSingleton<ICompanyService>(companyService);
            builder.Services.AddSingleton<IStandardCrudService>(crudService);
            builder.Services.AddSingleton<StandardCrudXlsxWriter>();
            builder.Services.AddScoped<StandardCrudEndpoint>();
            builder.Services.AddScoped<StandardCrudExportService>();
            builder.Services.AddStandardCrudResource(PersonCrudDescriptorFactory.Create());

            var application = builder.Build();
            application.UseAuthentication();
            application.UseAuthorization();
            application.MapControllers();
            application.MapStandardCrudResources();
            await application.StartAsync();

            var scenario = new Scenario(application, companyService, crudService)
            {
                Client = application.GetTestClient()
            };
            return scenario;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _application.DisposeAsync();
        }
    }

    private sealed class MutablePermissionDecisionSource : IPermissionDecisionSource
    {
        private int _callCount;

        public MutablePermissionDecisionSource(PermissionDecision decision)
        {
            Decision = decision;
        }

        public PermissionDecision Decision { get; set; }

        public int CallCount => Volatile.Read(ref _callCount);

        public string? LastPermissionCode { get; private set; }

        public ValueTask<PermissionDecision> EvaluateAsync(
            string subjectId,
            string permissionCode,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            LastPermissionCode = permissionCode;
            return ValueTask.FromResult(Decision);
        }
    }

    private sealed class RecordingCompanyService : ICompanyService
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<List<CompanyDto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new List<CompanyDto>());
        }

        public Task<CompanyDto?> GetByIdAsync(
            string companyId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CompanyDto> CreateAsync(
            CompanyDto dto,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CompanyDto> UpdateAsync(
            string companyId,
            CompanyDto dto,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingStandardCrudService : IStandardCrudService
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<StandardCrudListResult> ListAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudListQuery query,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new StandardCrudListResult([], 0, 1, 20, null, null));
        }

        public Task<StandardCrudDetail?> GetDetailAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult<StandardCrudDetail?>(Detail(id));
        }

        public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudListQuery query,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult<IReadOnlyList<StandardCrudOptionItem>>([]);
        }

        public Task<StandardCrudMutationResult> CreateAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(StandardCrudMutationResult.Success(Detail("Person_1")));
        }

        public Task<StandardCrudMutationResult> UpdateAsync(
            StandardCrudDescriptor descriptor,
            string id,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(StandardCrudMutationResult.Success(Detail(id)));
        }

        public Task<StandardCrudMutationResult> DisableAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(StandardCrudMutationResult.Success(Detail(id)));
        }

        public Task<StandardCrudMutationResult> DeleteAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(StandardCrudMutationResult.Success());
        }

        private static StandardCrudDetail Detail(string id) => new()
        {
            Fields = new Dictionary<string, object?>
            {
                ["EID"] = id,
                ["ENAME"] = "Test"
            }
        };
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "OrganizationPermissionTests";
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
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
