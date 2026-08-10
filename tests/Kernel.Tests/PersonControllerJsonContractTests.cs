using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Abstractions;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization;
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

        var response = await client.PostAsJsonAsync("/api/organization/persons", new Dictionary<string, object?>
        {
            ["PersonNo"] = "P" + Guid.NewGuid().ToString("N")[..8],
            ["ENAME"] = "张三",
            ["EmploymentStatus"] = "FullTime",
            ["IsEnabled"] = true
        });

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

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Platform:PluginInstallRoot", _installRoot);
            builder.UseSetting("Platform:SkipAuthorizationCatalogInitialization", "true");
            builder.UseSetting("Persistence:AutoMigrate", "false");
            builder.UseSetting("DevelopmentSchemaSync:Enabled", "false");
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStandardCrudService>();
                services.AddSingleton<IStandardCrudService>(StandardCrudService);
                services.RemoveAll<IPermissionDecisionSource>();
                services.AddSingleton<IPermissionDecisionSource, PersonCreatePermissionDecisionSource>();
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

    private sealed class PersonCreatePermissionDecisionSource : IPermissionDecisionSource
    {
        public ValueTask<PermissionDecision> EvaluateAsync(
            string subjectId,
            string permissionCode,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(permissionCode == OrganizationPermissions.PersonCreate
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
                [new Claim(ClaimTypes.NameIdentifier, "person-json-contract-subject")],
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
