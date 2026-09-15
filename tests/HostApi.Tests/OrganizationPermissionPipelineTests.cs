using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Exports;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Organization;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Models;
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
        var source = new MutableFeatureDecisionSource(PermissionDecision.Indeterminate);
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
    public async Task CompanyStandardCrud_EnforcesPermissionBeforeBusiness(
        string? subjectId,
        PermissionDecision decision,
        HttpStatusCode expectedStatus,
        int expectedBusinessCalls)
    {
        var source = new MutableFeatureDecisionSource(decision);
        await using var scenario = await Scenario.StartAsync(source);

        using var request = Request(HttpMethod.Get, "/api/organization/companies", subjectId);
        using var response = await scenario.Client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedBusinessCalls, scenario.CrudService.CallCount);
        if (expectedStatus == HttpStatusCode.OK)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("items").ValueKind);
            Assert.True(document.RootElement.TryGetProperty("total", out _));
            Assert.True(document.RootElement.TryGetProperty("page", out _));
            Assert.True(document.RootElement.TryGetProperty("pageSize", out _));
        }

        if (subjectId is not null)
        {
            Assert.Equal("organization.company.read", source.LastBehaviorKey);
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
        var source = new MutableFeatureDecisionSource(decision);
        await using var scenario = await Scenario.StartAsync(source);

        using var request = Request(HttpMethod.Get, "/api/organization/persons", subjectId);
        using var response = await scenario.Client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedBusinessCalls, scenario.CrudService.CallCount);
        if (subjectId is not null)
        {
            Assert.Equal("organization.person.read", source.LastBehaviorKey);
        }
    }

    [Theory]
    [InlineData(null, PermissionDecision.Granted, HttpStatusCode.Unauthorized, 0)]
    [InlineData("subject", PermissionDecision.Denied, HttpStatusCode.Forbidden, 0)]
    [InlineData("subject", PermissionDecision.Indeterminate, HttpStatusCode.ServiceUnavailable, 0)]
    [InlineData("subject", PermissionDecision.Granted, HttpStatusCode.OK, 1)]
    public async Task PersonOrganizationLookup_EnforcesPersonViewBeforeDirectoryRead(
        string? subjectId,
        PermissionDecision decision,
        HttpStatusCode expectedStatus,
        int expectedDirectoryCalls)
    {
        var source = new MutableFeatureDecisionSource(decision);
        await using var scenario = await Scenario.StartAsync(source);
        using var request = Request(
            HttpMethod.Get,
            "/api/organization/person-lookups/departments?companyId=Company_1&page=1&pageSize=20",
            subjectId);

        using var response = await scenario.Client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedDirectoryCalls, scenario.DepartmentDirectory.LookupCallCount);
        if (subjectId is not null)
        {
            Assert.Equal("organization.person.department.lookup", source.LastBehaviorKey);
        }
    }

    [Theory]
    [InlineData(null, PermissionDecision.Granted, HttpStatusCode.Unauthorized, 0)]
    [InlineData("subject", PermissionDecision.Denied, HttpStatusCode.Forbidden, 0)]
    [InlineData("subject", PermissionDecision.Indeterminate, HttpStatusCode.ServiceUnavailable, 0)]
    [InlineData("subject", PermissionDecision.Granted, HttpStatusCode.OK, 1)]
    public async Task DepartmentParentLookup_EnforcesDepartmentViewBeforeDirectoryRead(
        string? subjectId,
        PermissionDecision decision,
        HttpStatusCode expectedStatus,
        int expectedDirectoryCalls)
    {
        var source = new MutableFeatureDecisionSource(decision);
        await using var scenario = await Scenario.StartAsync(source);
        using var request = Request(
            HttpMethod.Get,
            "/api/organization/department-lookups/parents?companyId=Company_1&page=1&pageSize=20",
            subjectId);

        using var response = await scenario.Client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedDirectoryCalls, scenario.DepartmentDirectory.ParentLookupCallCount);
        if (subjectId is not null)
        {
            Assert.Equal("organization.department.parent.lookup", source.LastBehaviorKey);
        }
    }

    [Theory]
    [InlineData(null, PermissionDecision.Granted, HttpStatusCode.Unauthorized, 0)]
    [InlineData("subject", PermissionDecision.Denied, HttpStatusCode.Forbidden, 0)]
    [InlineData("subject", PermissionDecision.Indeterminate, HttpStatusCode.ServiceUnavailable, 0)]
    [InlineData("subject", PermissionDecision.Granted, HttpStatusCode.OK, 1)]
    public async Task DepartmentCompanyLookup_EnforcesDepartmentViewBeforeDirectoryRead(
        string? subjectId,
        PermissionDecision decision,
        HttpStatusCode expectedStatus,
        int expectedDirectoryCalls)
    {
        var source = new MutableFeatureDecisionSource(decision);
        await using var scenario = await Scenario.StartAsync(source);
        using var request = Request(
            HttpMethod.Get,
            "/api/organization/department-lookups/companies?page=1&pageSize=20",
            subjectId);

        using var response = await scenario.Client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expectedDirectoryCalls, scenario.CompanyDirectory.LookupCallCount);
        if (subjectId is not null)
        {
            Assert.Equal("organization.department.company.lookup", source.LastBehaviorKey);
        }
    }

    [Fact]
    public async Task DepartmentCompanyLookup_ForwardsPagingAndReturnsDisabledCurrentValue()
    {
        var source = new MutableFeatureDecisionSource(PermissionDecision.Granted);
        await using var scenario = await Scenario.StartAsync(source);
        scenario.CompanyDirectory.Result = new OrganizationDirectoryPage<Company>(
            [new Company { Id = "Company_1", Name = "总部", IsEnabled = true }],
            [new Company { Id = "Company_2", Name = "旧公司", IsEnabled = false }],
            2,
            10,
            true);
        using var request = Request(
            HttpMethod.Get,
            "/api/organization/department-lookups/companies?keyword=%E6%80%BB%E9%83%A8&page=2&pageSize=10&currentValueIds=Company_2",
            "subject");

        using var response = await scenario.Client.SendAsync(request);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("organization.department.company.lookup", source.LastBehaviorKey);
        Assert.Equal("总部", document.RootElement.GetProperty("items")[0].GetProperty("label").GetString());
        Assert.Equal("旧公司（已停用）", document.RootElement.GetProperty("currentItems")[0].GetProperty("label").GetString());
        Assert.True(document.RootElement.GetProperty("currentItems")[0].GetProperty("disabled").GetBoolean());
        Assert.Equal("总部", scenario.CompanyDirectory.Request?.Keyword);
        Assert.Equal(2, scenario.CompanyDirectory.Request?.Page);
        Assert.Equal(10, scenario.CompanyDirectory.Request?.PageSize);
        Assert.Equal(["Company_2"], scenario.CompanyDirectory.Request?.CurrentValueIds);
    }

    [Fact]
    public async Task DepartmentParentLookup_ResolvesCurrentDepartmentAndAllowsExplicitDisabledCandidate()
    {
        var source = new MutableFeatureDecisionSource(PermissionDecision.Granted);
        await using var scenario = await Scenario.StartAsync(source);
        scenario.DepartmentDirectory.DepartmentByCode = new Department
        {
            Id = "Department_1",
            CompanyId = "Company_1",
            Code = "D001",
            Name = "当前部门",
            IsEnabled = true
        };
        scenario.DepartmentDirectory.ParentResult = new OrganizationDirectoryPage<Department>(
            [new Department { Id = "Department_2", CompanyId = "Company_1", Name = "停用上级", IsEnabled = false }],
            [new Department { Id = "Department_3", CompanyId = "Company_1", Name = "旧上级", IsEnabled = false }],
            2,
            10,
            true);
        using var request = Request(
            HttpMethod.Get,
            "/api/organization/department-lookups/parents?companyId=Company_1&currentDepartmentCode=D001&keyword=%E4%B8%8A%E7%BA%A7&page=2&pageSize=10&currentValueIds=Department_3&includeDisabled=true",
            "subject");

        using var response = await scenario.Client.SendAsync(request);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("停用上级（已停用）", document.RootElement.GetProperty("items")[0].GetProperty("label").GetString());
        Assert.False(document.RootElement.GetProperty("items")[0].GetProperty("disabled").GetBoolean());
        Assert.Equal("旧上级（已停用）", document.RootElement.GetProperty("currentItems")[0].GetProperty("label").GetString());
        Assert.True(document.RootElement.GetProperty("currentItems")[0].GetProperty("disabled").GetBoolean());
        Assert.Equal("Company_1", scenario.DepartmentDirectory.ParentCompanyId);
        Assert.Equal("Department_1", scenario.DepartmentDirectory.CurrentDepartmentId);
        Assert.Equal("上级", scenario.DepartmentDirectory.ParentRequest?.Keyword);
        Assert.Equal(2, scenario.DepartmentDirectory.ParentRequest?.Page);
        Assert.Equal(10, scenario.DepartmentDirectory.ParentRequest?.PageSize);
        Assert.Equal(["Department_3"], scenario.DepartmentDirectory.ParentRequest?.CurrentValueIds);
        Assert.True(scenario.DepartmentDirectory.ParentRequest?.IncludeDisabled);
    }

    [Fact]
    public async Task PersonOrganizationLookup_ForwardsPagingAndReturnsDisabledCurrentValue()
    {
        var source = new MutableFeatureDecisionSource(PermissionDecision.Granted);
        await using var scenario = await Scenario.StartAsync(source);
        scenario.CompanyDirectory.Result = new OrganizationDirectoryPage<Company>(
            [new Company { Id = "Company_1", Name = "总部", IsEnabled = true }],
            [new Company { Id = "Company_2", Name = "旧公司", IsEnabled = false }],
            2,
            10,
            true);
        using var request = Request(
            HttpMethod.Get,
            "/api/organization/person-lookups/companies?keyword=%E6%80%BB%E9%83%A8&page=2&pageSize=10&currentValueIds=Company_2",
            "subject");

        using var response = await scenario.Client.SendAsync(request);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("总部", document.RootElement.GetProperty("items")[0].GetProperty("label").GetString());
        Assert.Equal("旧公司（已停用）", document.RootElement.GetProperty("currentItems")[0].GetProperty("label").GetString());
        Assert.True(document.RootElement.GetProperty("currentItems")[0].GetProperty("disabled").GetBoolean());
        Assert.Equal("总部", scenario.CompanyDirectory.Request?.Keyword);
        Assert.Equal(2, scenario.CompanyDirectory.Request?.Page);
        Assert.Equal(10, scenario.CompanyDirectory.Request?.PageSize);
        Assert.Equal(["Company_2"], scenario.CompanyDirectory.Request?.CurrentValueIds);
    }

    [Fact]
    public async Task PersonOrganizationLookup_AllowsDisabledDirectoryItemsOnlyForExplicitListFiltering()
    {
        var source = new MutableFeatureDecisionSource(PermissionDecision.Granted);
        await using var scenario = await Scenario.StartAsync(source);
        scenario.CompanyDirectory.Result = new OrganizationDirectoryPage<Company>(
            [new Company { Id = "Company_2", Name = "旧公司", IsEnabled = false }],
            [],
            1,
            20,
            false);
        using var request = Request(
            HttpMethod.Get,
            "/api/organization/person-lookups/companies?includeDisabled=true",
            "subject");

        using var response = await scenario.Client.SendAsync(request);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(scenario.CompanyDirectory.Request?.IncludeDisabled);
        Assert.False(document.RootElement.GetProperty("items")[0].GetProperty("disabled").GetBoolean());
        Assert.Equal("旧公司（已停用）", document.RootElement.GetProperty("items")[0].GetProperty("label").GetString());
    }

    [Theory]
    [InlineData("GET", "/api/organization/persons", "organization.person.read")]
    [InlineData("GET", "/api/organization/persons/options", null)]
    [InlineData("POST", "/api/organization/persons/export", "organization.person.export")]
    [InlineData("GET", "/api/organization/persons/Person_1", "organization.person.read")]
    [InlineData("POST", "/api/organization/persons", "organization.person.create")]
    [InlineData("PUT", "/api/organization/persons/Person_1", "organization.person.update")]
    [InlineData("POST", "/api/organization/persons/Person_1/disable", null)]
    [InlineData("DELETE", "/api/organization/persons/Person_1", "organization.person.delete")]
    public async Task PersonStandardCrud_AllMappedOperationsUseDeclaredFeature(
        string method,
        string path,
        string? expectedBehavior)
    {
        var source = new MutableFeatureDecisionSource(PermissionDecision.Granted);
        await using var scenario = await Scenario.StartAsync(source);

        using var request = Request(new HttpMethod(method), path, "subject", JsonBody(path, method));
        using var response = await scenario.Client.SendAsync(request);

        if (expectedBehavior is null)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(0, scenario.CrudService.CallCount);
            Assert.Equal(0, source.CallCount);
            return;
        }
        Assert.True(response.IsSuccessStatusCode, $"{method} {path} returned {(int)response.StatusCode}.");
        Assert.Equal(expectedBehavior, source.LastBehaviorKey);
        Assert.Equal(1, scenario.CrudService.CallCount);
    }

    [Theory]
    [InlineData("GET", "/api/organization/positions", "organization.position.read")]
    [InlineData("GET", "/api/organization/positions/options", "organization.position.lookup")]
    [InlineData("POST", "/api/organization/positions", "organization.position.create")]
    [InlineData("PUT", "/api/organization/positions/Position_1", "organization.position.update")]
    [InlineData("DELETE", "/api/organization/positions/Position_1", "organization.position.delete")]
    public async Task PositionStandardCrud_OperationsUseDeclaredFeature(
        string method,
        string path,
        string? expectedBehavior)
    {
        var source = new MutableFeatureDecisionSource(PermissionDecision.Granted);
        await using var scenario = await Scenario.StartAsync(source);

        using var request = Request(new HttpMethod(method), path, "subject", JsonBody(path, method));
        using var response = await scenario.Client.SendAsync(request);

        if (expectedBehavior is null)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(0, scenario.CrudService.CallCount);
            Assert.Equal(0, source.CallCount);
            return;
        }
        Assert.True(response.IsSuccessStatusCode, $"{method} {path} returned {(int)response.StatusCode}.");
        Assert.Equal(expectedBehavior, source.LastBehaviorKey);
        Assert.Equal(1, scenario.CrudService.CallCount);
    }

    [Theory]
    [InlineData("GET", "/api/organization/companies", "organization.company.read")]
    [InlineData("GET", "/api/organization/companies/options", null)]
    [InlineData("POST", "/api/organization/companies", "organization.company.create")]
    [InlineData("PUT", "/api/organization/companies/Company_1", "organization.company.update")]
    [InlineData("DELETE", "/api/organization/companies/Company_1", "organization.company.delete")]
    public async Task CompanyStandardCrud_OperationsUseDeclaredFeature(
        string method,
        string path,
        string? expectedBehavior)
    {
        var source = new MutableFeatureDecisionSource(PermissionDecision.Granted);
        await using var scenario = await Scenario.StartAsync(source);

        using var request = Request(new HttpMethod(method), path, "subject", JsonBody(path, method));
        using var response = await scenario.Client.SendAsync(request);

        if (expectedBehavior is null)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(0, scenario.CrudService.CallCount);
            Assert.Equal(0, source.CallCount);
            return;
        }
        Assert.True(response.IsSuccessStatusCode, $"{method} {path} returned {(int)response.StatusCode}.");
        Assert.Equal(expectedBehavior, source.LastBehaviorKey);
        Assert.Equal(1, scenario.CrudService.CallCount);
    }

    [Theory]
    [InlineData("GET", "/api/organization/departments", "organization.department.read")]
    [InlineData("GET", "/api/organization/departments/options", null)]
    [InlineData("POST", "/api/organization/departments", "organization.department.create")]
    [InlineData("PUT", "/api/organization/departments/Department_1", "organization.department.update")]
    [InlineData("DELETE", "/api/organization/departments/Department_1", "organization.department.delete")]
    public async Task DepartmentStandardCrud_OperationsUseDeclaredFeature(
        string method,
        string path,
        string? expectedBehavior)
    {
        var source = new MutableFeatureDecisionSource(PermissionDecision.Granted);
        await using var scenario = await Scenario.StartAsync(source);

        using var request = Request(new HttpMethod(method), path, "subject", JsonBody(path, method));
        using var response = await scenario.Client.SendAsync(request);

        if (expectedBehavior is null)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(0, scenario.CrudService.CallCount);
            Assert.Equal(0, source.CallCount);
            return;
        }
        Assert.True(response.IsSuccessStatusCode, $"{method} {path} returned {(int)response.StatusCode}.");
        Assert.Equal(expectedBehavior, source.LastBehaviorKey);
        Assert.Equal(1, scenario.CrudService.CallCount);
    }

    [Theory]
    [InlineData("Position_Missing", HttpStatusCode.NotFound, "StandardCrud.NotFound")]
    [InlineData("Position_Referenced", HttpStatusCode.Conflict, "Organization.Position.DeleteBlockedByPersonReference")]
    [InlineData("Position_Enabled", HttpStatusCode.NoContent, null)]
    [InlineData("Position_Disabled", HttpStatusCode.NoContent, null)]
    public async Task PositionStandardCrud_DeleteProjectsStableHttpOutcome(
        string id,
        HttpStatusCode expectedStatus,
        string? expectedCode)
    {
        var source = new MutableFeatureDecisionSource(PermissionDecision.Granted);
        await using var scenario = await Scenario.StartAsync(source);
        scenario.CrudService.DeleteResultFactory = currentId => currentId switch
        {
            "Position_Missing" => StandardCrudMutationResult.Failure(new StandardCrudError
            {
                StatusCode = 404,
                Code = "StandardCrud.NotFound",
                Message = "岗位不存在。",
                ReasonKey = "standardCrud.notFound"
            }),
            "Position_Referenced" => StandardCrudMutationResult.Failure(new StandardCrudError
            {
                StatusCode = 409,
                Code = "Organization.Position.DeleteBlockedByPersonReference",
                Message = "岗位当前被人员引用，不能删除。",
                ReasonKey = "organization.position.deleteBlockedByPersonReference"
            }),
            _ => StandardCrudMutationResult.Success()
        };

        using var request = Request(HttpMethod.Delete, $"/api/organization/positions/{id}", "subject");
        using var response = await scenario.Client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedCode is not null)
        {
            var error = await response.Content.ReadFromJsonAsync<StandardCrudError>();
            Assert.Equal(expectedCode, error?.Code);
        }
    }

    [Fact]
    public async Task PersonStandardCrud_RevokedFeatureTakesEffectOnNextRequest()
    {
        var source = new MutableFeatureDecisionSource(PermissionDecision.Granted);
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
        request.Headers.Add("X-NewP-Feature", "test-position");
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
            RecordingStandardCrudService crudService,
            RecordingCompanyDirectory companyDirectory,
            RecordingDepartmentDirectory departmentDirectory)
        {
            _application = application;
            CrudService = crudService;
            CompanyDirectory = companyDirectory;
            DepartmentDirectory = departmentDirectory;
        }

        public HttpClient Client { get; private set; } = null!;

        public RecordingStandardCrudService CrudService { get; }

        public RecordingCompanyDirectory CompanyDirectory { get; }

        public RecordingDepartmentDirectory DepartmentDirectory { get; }

        public static async Task<Scenario> StartAsync(ICP.Platform.Authorization.Http.IFeatureDecisionSource source)
        {
            var crudService = new RecordingStandardCrudService();
            var companyDirectory = new RecordingCompanyDirectory();
            var departmentDirectory = new RecordingDepartmentDirectory();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services
                .AddControllers()
                .AddApplicationPart(typeof(PersonOrganizationLookupsController).Assembly);
            builder.Services
                .AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    static _ => { });
            builder.Services.AddPlatformAuthorization();
            builder.Services.AddAuthorization(options => options.AddPolicy(ICP.Platform.Authorization.Http.DenyAllAttribute.PolicyName, policy => policy.RequireAssertion(_ => false)));
            builder.Services.AddSingleton(source);
            builder.Services.AddSingleton<ICompanyDirectory>(companyDirectory);
            builder.Services.AddSingleton<IDepartmentDirectory>(departmentDirectory);
            builder.Services.AddSingleton<IDepartmentHierarchyDirectory>(departmentDirectory);
            builder.Services.AddSingleton<IStandardCrudService>(crudService);
            builder.Services.AddSingleton<StandardCrudXlsxWriter>();
            builder.Services.AddScoped<StandardCrudEndpoint>();
            builder.Services.AddScoped<StandardCrudExportService>();
            builder.Services.AddStandardCrudResource(PersonCrudDescriptorFactory.Create());
            builder.Services.AddStandardCrudResource(PositionCrudDescriptorFactory.Create());
            builder.Services.AddStandardCrudResource(CompanyCrudDescriptorFactory.Create());
            builder.Services.AddStandardCrudResource(DepartmentCrudDescriptorFactory.Create());

            var application = builder.Build();
            application.UseAuthentication();
            application.UseAuthorization();
            application.MapControllers();
            application.MapStandardCrudResources();
            await application.StartAsync();

            var scenario = new Scenario(
                application,
                crudService,
                companyDirectory,
                departmentDirectory)
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

    private sealed class RecordingCompanyDirectory : ICompanyDirectory
    {
        private int _lookupCallCount;

        public int LookupCallCount => Volatile.Read(ref _lookupCallCount);

        public OrganizationDirectoryLookupRequest? Request { get; private set; }

        public OrganizationDirectoryPage<Company> Result { get; set; } = new([], [], 1, 20, false);

        public Task<Company?> GetByIdAsync(string companyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, Company>> GetByIdsAsync(
            IReadOnlyCollection<string> companyIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Company?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OrganizationDirectoryPage<Company>> LookupAsync(
            OrganizationDirectoryLookupRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _lookupCallCount);
            Request = request;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingDepartmentDirectory : IDepartmentDirectory, IDepartmentHierarchyDirectory
    {
        private int _lookupCallCount;
        private int _parentLookupCallCount;

        public int LookupCallCount => Volatile.Read(ref _lookupCallCount);

        public int ParentLookupCallCount => Volatile.Read(ref _parentLookupCallCount);

        public Department? DepartmentByCode { get; set; }

        public OrganizationDirectoryPage<Department> ParentResult { get; set; } = new([], [], 1, 20, false);

        public string? ParentCompanyId { get; private set; }

        public string? CurrentDepartmentId { get; private set; }

        public OrganizationDirectoryLookupRequest? ParentRequest { get; private set; }

        public Task<Department?> GetByIdAsync(string departmentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, Department>> GetByIdsAsync(
            IReadOnlyCollection<string> departmentIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Department?> GetByCompanyAndCodeAsync(
            string companyId,
            string code,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                DepartmentByCode is not null
                && string.Equals(DepartmentByCode.CompanyId, companyId, StringComparison.Ordinal)
                && string.Equals(DepartmentByCode.Code, code, StringComparison.Ordinal)
                    ? DepartmentByCode
                    : null);

        public Task<OrganizationDirectoryPage<Department>> LookupAsync(
            string companyId,
            OrganizationDirectoryLookupRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _lookupCallCount);
            return Task.FromResult(new OrganizationDirectoryPage<Department>([], [], request.Page, request.PageSize, false));
        }

        public Task<OrganizationDirectoryPage<Department>> LookupParentCandidatesAsync(
            string companyId,
            string? currentDepartmentId,
            OrganizationDirectoryLookupRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _parentLookupCallCount);
            ParentCompanyId = companyId;
            CurrentDepartmentId = currentDepartmentId;
            ParentRequest = request;
            return Task.FromResult(ParentResult);
        }
    }

    private sealed class MutableFeatureDecisionSource : ICP.Platform.Authorization.Http.IFeatureDecisionSource
    {
        private int _callCount;

        public MutableFeatureDecisionSource(PermissionDecision decision)
        {
            Decision = decision;
        }

        public PermissionDecision Decision { get; set; }

        public int CallCount => Volatile.Read(ref _callCount);

        public string? LastBehaviorKey { get; private set; }

        public ValueTask<PermissionDecision> EvaluateAsync(
            string subjectId,
            string position,
            string moduleId,
            string behaviorKey,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            LastBehaviorKey = behaviorKey;
            return ValueTask.FromResult(Decision);
        }
    }

    private sealed class RecordingStandardCrudService : IStandardCrudService
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Func<string, StandardCrudMutationResult> DeleteResultFactory { get; set; } =
            static _ => StandardCrudMutationResult.Success();

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

        public Task<StandardCrudMutationResult> EnableAsync(
            StandardCrudDescriptor descriptor,
            string id,
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
            return Task.FromResult(DeleteResultFactory(id));
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
