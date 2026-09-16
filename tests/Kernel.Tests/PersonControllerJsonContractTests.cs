using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Testing.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class PersonControllerJsonContractTests : IClassFixture<PersonControllerJsonContractTests.TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PersonControllerJsonContractTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreatePerson_AcceptsStringEmploymentStatus_FromStandardCrudPayload()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var manifest = await client.GetFromJsonAsync<JsonDocument>("/plugins/metadata-runtime-manifest.json");
        var points = RuntimeFeatureCatalogTestSupport.Parse(manifest!.RootElement);
        var entry = points.Single(point => point.ModuleId == "Organization" && point.Kind == "tab" && point.Key == "persons");
        _factory.PersonCreate = points.Single(point => point.ParentPosition == entry.Position
            && point.Key == "create" && point.ActionScope == "list.pageActions");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/organization/persons");
        request.Headers.Add("X-NewP-Feature", _factory.PersonCreate.Position);
        request.Content = JsonContent.Create(new Dictionary<string, object?>
        {
            ["PersonNo"] = "P" + Guid.NewGuid().ToString("N")[..8],
            ["ENAME"] = "张三",
            ["EmploymentStatus"] = "FullTime",
            ["IsEnabled"] = true
        });

        using var response = await client.SendAsync(request);

        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Expected Created but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.Equal("FullTime", _factory.StandardCrudService.LastCreateValues!["EmploymentStatus"]);
    }

    public class TestWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
    {
        private readonly string _installRoot;

        public TestWebApplicationFactory()
        {
            _installRoot = Path.Combine(Path.GetTempPath(), $"person_json_contract_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_installRoot);
        }

        public RecordingStandardCrudService StandardCrudService { get; } = new();
        public FeaturePoint? PersonCreate { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Platform:PluginInstallRoot", _installRoot);
            builder.UseSetting("Platform:SkipAuthorizationCatalogInitialization", "true");
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPersistenceMigrationRunner>();
                services.AddSingleton<IPersistenceMigrationRunner, TestPersistenceMigrationRunner>();
                services.RemoveAll<IPersistenceConsistencyChecker>();
                services.AddSingleton<IPersistenceConsistencyChecker, TestPersistenceConsistencyChecker>();
                services.RemoveAll<IStandardCrudService>();
                services.AddSingleton<IStandardCrudService>(StandardCrudService);
                services.RemoveAll<IFeatureDecisionSource>();
                services.AddSingleton<IFeatureDecisionSource>(new PersonCreateFeatureDecisionSource(() => PersonCreate));
                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        static _ => { });
            });
        }

        public new void Dispose()
        {
            base.Dispose();
            if (Directory.Exists(_installRoot))
            {
                Directory.Delete(_installRoot, recursive: true);
            }
        }
    }

    private sealed class PersonCreateFeatureDecisionSource(Func<FeaturePoint?> selectedPoint) : IFeatureDecisionSource
    {
        public ValueTask<PermissionDecision> EvaluateAsync(
            string subjectId,
            string position,
            string moduleId,
            string behaviorKey,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(subjectId == "person-json-contract-subject"
                && selectedPoint() is { } point && point.Position == position
                && point.BehaviorKey == behaviorKey && behaviorKey == "organization.person.create"
                ? PermissionDecision.Granted
                : PermissionDecision.Denied);
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "PersonJsonContract";

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "person-json-contract-subject"),
                    new Claim(AuthenticationSessionClaimTypes.TimeZoneId, "Etc/UTC")
                ],
                SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    public sealed class RecordingStandardCrudService : IStandardCrudService
    {
        public IReadOnlyDictionary<string, object?>? LastCreateValues { get; private set; }

        public Task<StandardCrudMutationResult> CreateAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            LastCreateValues = values;
            return Task.FromResult(StandardCrudMutationResult.Success(new StandardCrudDetail
            {
                Fields = new Dictionary<string, object?>
                {
                    [descriptor.IdField] = "Person_00000000000000000000000000000001",
                    ["PersonNo"] = values["PersonNo"],
                    ["ENAME"] = values["ENAME"],
                    ["EmploymentStatus"] = values["EmploymentStatus"],
                    ["IsEnabled"] = values["IsEnabled"]
                }
            }));
        }

        public Task<StandardCrudMutationResult> DeleteAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StandardCrudMutationResult> EnableAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StandardCrudMutationResult> DisableAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StandardCrudDetail?> GetDetailAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StandardCrudListResult> ListAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudListQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudListQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StandardCrudMutationResult> UpdateAsync(
            StandardCrudDescriptor descriptor,
            string id,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
