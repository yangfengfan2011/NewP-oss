using System.Net;
using System.Net.Http.Json;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudEndpointRouteBuilderTests
{
    [Fact]
    public async Task MapStandardCrudResources_MapsListRouteFromDescriptor()
    {
        var recording = new RecordingStandardCrudService();
        await using var app = BuildApp(recording);
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/api/organization/persons?page=2&pageSize=5&keyword=张");

        response.EnsureSuccessStatusCode();
        Assert.Equal(2, recording.LastListQuery!.Page);
        Assert.Equal(5, recording.LastListQuery.PageSize);
        Assert.Equal("张", recording.LastListQuery.Filters!["keyword"]);
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
        app.MapStandardCrudResources();
        return app;
    }

    private static WebApplicationBuilder CreateBuilder(RecordingStandardCrudService recording)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IStandardCrudService>(recording);
        builder.Services.AddScoped<StandardCrudEndpoint>();
        builder.Services.AddStandardCrudResource(Descriptor());
        return builder;
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
            ListFields: ["EID", "PersonNo", "ENAME", "IsEnabled"],
            DetailFields: ["EID", "PersonNo", "ENAME", "IsEnabled"],
            CreateFields: ["PersonNo", "ENAME", "IsEnabled"],
            UpdateFields: ["ENAME", "IsEnabled"]),
        OptionLabelFields = ["PersonNo", "ENAME"],
        DeletePolicy = StandardCrudDeletePolicy.Conditional
    };

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

    private sealed class RecordingStandardCrudService : IStandardCrudService
    {
        public StandardCrudListQuery? LastListQuery { get; private set; }
        public StandardCrudListQuery? LastOptionsQuery { get; private set; }
        public string? LastDetailId { get; private set; }
        public string? LastMutation { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastMutationValues { get; private set; }
        public StandardCrudMutationResult MutationResult { get; init; } = StandardCrudMutationResult.Success(Detail("Person_00000000000000000000000000000001"));

        public Task<StandardCrudListResult> ListAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken cancellationToken = default)
        {
            LastListQuery = query;
            return Task.FromResult(new StandardCrudListResult([], 0, query.Page, query.PageSize, query.SortBy, query.SortDirection));
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
