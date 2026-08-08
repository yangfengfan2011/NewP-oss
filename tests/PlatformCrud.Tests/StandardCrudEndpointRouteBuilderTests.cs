using System.Net;
using System.Net.Http.Json;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Abstractions;
using ICP.Platform.Authorization.Extensions;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Exports;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Query.Descriptors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudEndpointRouteBuilderTests
{
    [Fact]
    public void MapStandardCrudResources_AttachesFixedPermissionRequirementsToEveryRoute()
    {
        var recording = new RecordingStandardCrudService();
        using var app = BuildApp(recording);

        var requirements = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => new EndpointPermission(
                endpoint.RoutePattern.RawText,
                endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [],
                endpoint.Metadata.GetMetadata<RequirePermissionAttribute>()?.PermissionCode))
            .ToArray();

        AssertPermission(requirements, "GET", "/api/organization/persons", "Organization.Person.View");
        AssertPermission(requirements, "GET", "/api/organization/persons/options", "Organization.Person.View");
        AssertPermission(requirements, "POST", "/api/organization/persons/export", "Organization.Person.View");
        AssertPermission(requirements, "GET", "/api/organization/persons/{id}", "Organization.Person.View");
        AssertPermission(requirements, "POST", "/api/organization/persons", "Organization.Person.Create");
        AssertPermission(requirements, "PUT", "/api/organization/persons/{id}", "Organization.Person.Update");
        AssertPermission(requirements, "POST", "/api/organization/persons/{id}/disable", "Organization.Person.Update");
        AssertPermission(requirements, "DELETE", "/api/organization/persons/{id}", "Organization.Person.Delete");
        Assert.All(requirements, static requirement => Assert.NotNull(requirement.Permission));
    }

    [Fact]
    public async Task MapStandardCrudResources_MapsListRouteFromDescriptor()
    {
        var recording = new RecordingStandardCrudService();
        await using var app = BuildApp(recording);
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/api/organization/persons?pageIndex=2&pageSize=5&keyword=张");

        response.EnsureSuccessStatusCode();
        Assert.Equal(2, recording.LastListQuery!.Page);
        Assert.Equal(5, recording.LastListQuery.PageSize);
        Assert.Equal("张", recording.LastListQuery.Filters!["keyword"]);
        Assert.DoesNotContain("pageIndex", recording.LastListQuery.Filters.Keys);
    }

    [Fact]
    public async Task MapStandardCrudResources_ParsesFieldsOutsideFilters()
    {
        var recording = new RecordingStandardCrudService();
        await using var app = BuildApp(recording);
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync(
            "/api/organization/persons?pageIndex=2&pageSize=5&fields=EID,PersonNo,CompanyName,IsEnabled&keyword=张");

        response.EnsureSuccessStatusCode();
        Assert.Equal(["EID", "PersonNo", "CompanyName", "IsEnabled"], recording.LastListQuery!.Fields);
        Assert.Equal("张", recording.LastListQuery.Filters!["keyword"]);
        Assert.DoesNotContain("fields", recording.LastListQuery.Filters.Keys);
    }

    [Fact]
    public async Task MapStandardCrudResources_MapsReadOnlyStandardRoutes()
    {
        var recording = new RecordingStandardCrudService();
        await using var app = BuildApp(recording);
        await app.StartAsync();
        var client = app.GetTestClient();

        var detailResponse = await client.GetAsync("/api/organization/persons/Person_00000000000000000000000000000001");
        var optionsResponse = await client.GetAsync("/api/organization/persons/options?pageSize=7&keyword=张");

        detailResponse.EnsureSuccessStatusCode();
        optionsResponse.EnsureSuccessStatusCode();
        Assert.Equal("Person_00000000000000000000000000000001", recording.LastDetailId);
        Assert.Equal(7, recording.LastOptionsQuery!.PageSize);
        Assert.Equal("张", recording.LastOptionsQuery.Filters!["keyword"]);
    }

    [Fact]
    public async Task MapStandardCrudResources_ExportsCurrentPageAsXlsx()
    {
        var recording = new RecordingStandardCrudService
        {
            ListResult = new StandardCrudListResult(
                [new Dictionary<string, object?> { ["EID"] = "Person_1", ["PersonNo"] = "P001", ["ENAME"] = "张三" }],
                1,
                2,
                5,
                null,
                null)
        };
        await using var app = BuildApp(recording);
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/organization/persons/export",
            new
            {
                mode = "current",
                query = new
                {
                    pageIndex = 2,
                    pageSize = 5,
                    filters = new Dictionary<string, string?> { ["keyword"] = "张" }
                },
                columns = new[]
                {
                    new { key = "personNo", field = "PersonNo", title = "人员编号" },
                    new { key = "name", field = "ENAME", title = "姓名" },
                    new { key = "companyName", field = "CompanyName", title = "公司" },
                    new { key = "blocked", field = "SecretField", title = "秘密字段" }
                }
            });

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2, recording.LastListQuery!.Page);
        Assert.Equal(5, recording.LastListQuery.PageSize);
        Assert.Equal("张", recording.LastListQuery.Filters!["keyword"]);
        Assert.Equal(["PersonNo", "ENAME", "CompanyName"], recording.LastListQuery.Fields);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 0);
        AssertXlsxContains(bytes, "人员编号", "张三");
        AssertXlsxDoesNotContain(bytes, "秘密字段");
    }

    [Fact]
    public async Task MapStandardCrudResources_ExportsAllWithNormalizedPaging()
    {
        var recording = new RecordingStandardCrudService();
        await using var app = BuildApp(recording);
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/organization/persons/export",
            new
            {
                mode = "all",
                query = new
                {
                    pageIndex = 3,
                    pageSize = 20,
                    filters = new Dictionary<string, string?> { ["keyword"] = "张" }
                },
                columns = new[] { new { key = "name", field = "ENAME", title = "姓名" } }
            });

        response.EnsureSuccessStatusCode();
        Assert.Equal(1, recording.LastListQuery!.Page);
        Assert.True(recording.LastListQuery.PageSize > 20);
        Assert.Equal("张", recording.LastListQuery.Filters!["keyword"]);
        Assert.Equal(["ENAME"], recording.LastListQuery.Fields);
    }

    [Fact]
    public async Task MapStandardCrudResources_CreateReturnsDetail()
    {
        var recording = new RecordingStandardCrudService
        {
            MutationResult = StandardCrudMutationResult.Success(Detail("Person_00000000000000000000000000000001"))
        };
        await using var app = BuildApp(recording);
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/organization/persons",
            new Dictionary<string, object?> { ["PersonNo"] = "P001", ["ENAME"] = "张三" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(nameof(IStandardCrudService.CreateAsync), recording.LastMutation);
        Assert.Equal("P001", recording.LastMutationValues!["PersonNo"]!.ToString());
    }

    [Fact]
    public async Task MapStandardCrudResources_MapsUpdateDisableAndDeleteRoutes()
    {
        var recording = new RecordingStandardCrudService();
        await using var app = BuildApp(recording);
        await app.StartAsync();
        var client = app.GetTestClient();

        var updateResponse = await client.PutAsJsonAsync(
            "/api/organization/persons/Person_00000000000000000000000000000001",
            new Dictionary<string, object?> { ["ENAME"] = "李四" });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(nameof(IStandardCrudService.UpdateAsync), recording.LastMutation);
        Assert.Equal("李四", recording.LastMutationValues!["ENAME"]!.ToString());

        var disableResponse = await client.PostAsync(
            "/api/organization/persons/Person_00000000000000000000000000000001/disable",
            content: null);

        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);
        Assert.Equal(nameof(IStandardCrudService.DisableAsync), recording.LastMutation);

        var deleteResponse = await client.DeleteAsync("/api/organization/persons/Person_00000000000000000000000000000001");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(nameof(IStandardCrudService.DeleteAsync), recording.LastMutation);
    }

    [Fact]
    public async Task MapStandardCrudResources_WhenMutationFails_ReturnsStandardErrorStatusCode()
    {
        var recording = new RecordingStandardCrudService
        {
            MutationResult = StandardCrudMutationResult.Failure(new StandardCrudError
            {
                StatusCode = 409,
                Code = "Organization.Person.PersonNoDuplicated",
                Message = "人员工号必须唯一。",
                ReasonKey = "organization.person.personNoDuplicated"
            })
        };
        await using var app = BuildApp(recording);
        await app.StartAsync();

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/organization/persons",
            new Dictionary<string, object?> { ["PersonNo"] = "P001", ["ENAME"] = "张三" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public void MapStandardCrudResources_WhenDerivedRouteConflicts_ThrowsStableMessage()
    {
        var recording = new RecordingStandardCrudService();
        var builder = CreateBuilder(recording);
        builder.Services.AddStandardCrudResource(Descriptor(
            resourceKey: "organization.personOptions",
            route: "api/organization/persons/options"));
        using var app = builder.Build();

        var ex = Assert.Throws<InvalidOperationException>(() => app.MapStandardCrudResources());

        Assert.Equal(
            "标准 CRUD 路由 GET /api/organization/persons/options 被 organization.person 与 organization.personOptions 重复映射。",
            ex.Message);
    }

    private static WebApplication BuildApp(RecordingStandardCrudService recording)
    {
        var builder = CreateBuilder(recording);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "standard-crud-test-subject")],
                "StandardCrudTests"));
            await next();
        });
        app.UseAuthorization();
        app.MapStandardCrudResources();
        return app;
    }

    private static WebApplicationBuilder CreateBuilder(RecordingStandardCrudService recording)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddPlatformAuthorization();
        builder.Services.AddSingleton<IPermissionDecisionSource, GrantAllPermissionDecisionSource>();
        builder.Services.AddSingleton<IStandardCrudService>(recording);
        builder.Services.AddSingleton<StandardCrudXlsxWriter>();
        builder.Services.AddScoped<StandardCrudEndpoint>();
        builder.Services.AddScoped<StandardCrudExportService>();
        builder.Services.AddStandardCrudResource(Descriptor());
        return builder;
    }

    private sealed class GrantAllPermissionDecisionSource : IPermissionDecisionSource
    {
        public ValueTask<PermissionDecision> EvaluateAsync(
            string subjectId,
            string permissionCode,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PermissionDecision.Granted);
    }

    private static StandardCrudDescriptor Descriptor(
        string resourceKey = "organization.person",
        string route = "api/organization/persons") => new()
        {
            ResourceKey = resourceKey,
            ModuleId = "Organization",
            ModelName = "Person",
            Route = route,
            IdField = "EID",
            DisplayField = "ENAME",
            EnabledField = "IsEnabled",
            FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME", "CompanyId", "IsEnabled"],
            DetailFields: ["EID", "PersonNo", "ENAME", "CompanyId", "IsEnabled"],
            CreateFields: ["PersonNo", "ENAME", "CompanyId", "IsEnabled"],
            UpdateFields: ["ENAME", "CompanyId", "IsEnabled"]),
            OptionLabelFields = ["PersonNo", "ENAME"],
            DisplayValueFields =
        [
            new PlatformDisplayValueField("CompanyName", "CompanyId", "Company", "EID", "ENAME")
        ],
            Authorization = StandardCrudAuthorization.ByConvention("Organization.Person"),
            DeletePolicy = StandardCrudDeletePolicy.Conditional
        };

    private static void AssertPermission(
        IReadOnlyList<EndpointPermission> requirements,
        string method,
        string route,
        string permission)
    {
        var requirement = Assert.Single(requirements, item =>
            string.Equals(item.Route, route, StringComparison.Ordinal)
            && item.Methods.Contains(method, StringComparer.Ordinal));
        Assert.Equal(permission, requirement.Permission);
    }

    private sealed record EndpointPermission(
        string? Route,
        IReadOnlyList<string> Methods,
        string? Permission);

    private static StandardCrudDetail Detail(string id) => new()
    {
        Fields = new Dictionary<string, object?>
        {
            ["EID"] = id,
            ["PersonNo"] = "P001",
            ["ENAME"] = "张三",
            ["IsEnabled"] = true
        }
    };

    private static void AssertXlsxContains(byte[] bytes, params string[] expectedValues)
    {
        var sheetXml = ReadXlsxSheetXml(bytes);
        foreach (var expectedValue in expectedValues)
        {
            Assert.Contains(expectedValue, sheetXml);
        }
    }

    private static void AssertXlsxDoesNotContain(byte[] bytes, string unexpectedValue)
    {
        Assert.DoesNotContain(unexpectedValue, ReadXlsxSheetXml(bytes));
    }

    private static string ReadXlsxSheetXml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(entry);
        using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed class RecordingStandardCrudService : IStandardCrudService
    {
        public StandardCrudListQuery? LastListQuery { get; private set; }
        public StandardCrudListQuery? LastOptionsQuery { get; private set; }
        public string? LastDetailId { get; private set; }
        public string? LastMutation { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastMutationValues { get; private set; }
        public StandardCrudListResult ListResult { get; init; } = new([], 0, 1, 20, null, null);
        public StandardCrudMutationResult MutationResult { get; init; } = StandardCrudMutationResult.Success(Detail("Person_00000000000000000000000000000001"));

        public Task<StandardCrudListResult> ListAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken cancellationToken = default)
        {
            LastListQuery = query;
            return Task.FromResult(ListResult with { Page = query.Page, PageSize = query.PageSize, SortBy = query.SortBy, SortDirection = query.SortDirection });
        }

        public Task<StandardCrudDetail?> GetDetailAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
        {
            LastDetailId = id;
            return Task.FromResult<StandardCrudDetail?>(Detail(id));
        }

        public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken cancellationToken = default)
        {
            LastOptionsQuery = query;
            return Task.FromResult<IReadOnlyList<StandardCrudOptionItem>>([]);
        }

        public Task<StandardCrudMutationResult> CreateAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
        {
            LastMutation = nameof(CreateAsync);
            LastMutationValues = values;
            return Task.FromResult(MutationResult);
        }

        public Task<StandardCrudMutationResult> UpdateAsync(StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
        {
            LastMutation = nameof(UpdateAsync);
            LastMutationValues = values;
            return Task.FromResult(MutationResult);
        }

        public Task<StandardCrudMutationResult> DisableAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
        {
            LastMutation = nameof(DisableAsync);
            return Task.FromResult(MutationResult);
        }

        public Task<StandardCrudMutationResult> DeleteAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
        {
            LastMutation = nameof(DeleteAsync);
            return Task.FromResult(StandardCrudMutationResult.Success());
        }
    }
}
