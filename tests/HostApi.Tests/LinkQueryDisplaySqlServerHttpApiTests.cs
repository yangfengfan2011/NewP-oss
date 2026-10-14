using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class LinkQueryDisplaySqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实端点计划驱动非 CRUD 批量显示、CalendarDate 与空映射")]
    public async Task TypedPlans_ProjectNonCrudReferencesAndCalendarDatesThroughRealLinkQueries()
    {
        var resolver = new FolderSpaceDisplayResolver();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.AddSingleton<IStandardCrudDisplayValueResolver>(resolver);
            // 只扩展本测试的读取目录，复用真实 Workspace/Person 模型、存储与授权；不修改正式 metadata。
            services.AddSingleton(provider => DisplayCatalog(provider));
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "workspace.spaces.access");
        var create = Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == "workspace.folder.create");
        using var initialize = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        var root = await initialize.Content.ReadFromJsonAsync<JsonElement>();
        var rootId = root.GetProperty("rootFolderId").GetString()!;
        var spaceId = root.GetProperty("spaceId").GetString()!;
        var position = new { spaceId, rootFolderId = rootId, linkIds = Array.Empty<string>() };
        for (var index = 0; index < 2; index++)
        {
            using var created = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post,
                "/api/workspace/content/folders", new { position, values = new { ENAME = "显示文件夹" + index } });
            Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        }
        var http = new FeatureHttpTestRequests(fixture);
        var person = await FeatureHttpTestRequests.CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "LINK-DISPLAY", ENAME = "日期端点", BirthDate = "2000-02-29", EmploymentStatus = "Active", IsEnabled = true });
        var personId = person.GetProperty("fields").GetProperty("EID").GetString()!;
        using (var scope = fixture.Services.CreateScope())
        {
            Assert.Null(scope.ServiceProvider.GetRequiredService<IStandardCrudResourceRegistry>().FindByResourceKey("workspace.folder"));
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var links = db.Set<Dictionary<string, object?>>("WorkspaceFolderContent");
            // 构造通用 Link 的读取样本；Person 实体仍由正式创建入口写入，Workspace 命令不新增人员接入能力。
            var seed = await links.AsNoTracking().FirstAsync(row => EF.Property<string>(row, "PEID") == rootId);
            links.Add(new(seed) { ["LID"] = Guid.NewGuid().ToString("N"), ["CEID"] = personId, ["ChildModelKey"] = "Person", ["TreeOrdinal"] = null });
            await db.SaveChangesAsync();
        }
        var queryScope = Uri.EscapeDataString(JsonSerializer.Serialize(new { spaceId, rootFolderId = rootId, linkIds = "[]" }));
        var url = "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId=" + rootId + "&queryScope=" + queryScope;
        resolver.Calls.Clear();
        var result = await FeatureHttpTestRequests.ReadAsync(admin, entry, url);
        var rows = result.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, rows.Length);
        var batch = Assert.Single(resolver.Calls);
        Assert.Equal(2, batch.Length);
        Assert.All(batch, value => Assert.Equal(spaceId, value));
        foreach (var row in rows)
        {
            var fields = row.GetProperty("fields");
            Assert.False(fields.TryGetProperty("Name_DisplayValue", out _));
            if (row.GetProperty("associatedObject").GetProperty("modelKey").GetString() == "Person")
            {
                Assert.Equal("2000-02-29", fields.GetProperty("BirthDate").GetString());
                Assert.Equal(person.GetProperty("fields").GetProperty("BirthDate_DisplayValue").GetString(), fields.GetProperty("BirthDate_DisplayValue").GetString());
                Assert.Equal(JsonValueKind.Null, fields.GetProperty("Origin_DisplayValue").ValueKind);
            }
            else
            {
                Assert.Equal(spaceId, fields.GetProperty("Origin").GetString());
                Assert.Equal("空间显示", fields.GetProperty("Origin_DisplayValue").GetString());
                Assert.Equal(JsonValueKind.Null, fields.GetProperty("BirthDate_DisplayValue").ValueKind);
            }
        }
        resolver.Calls.Clear();
        await FeatureHttpTestRequests.ReadAsync(admin, entry, url + "&fields=Name");
        Assert.Empty(resolver.Calls);
        using var invalid = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Get, url + "&fields=Name_DisplayValue");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var reverse = await FeatureHttpTestRequests.ReadAsync(admin, entry,
            "/api/links/workspace.content?direction=reverse&anchorModelKey=Person&anchorId=" + personId);
        var parent = Assert.Single(reverse.GetProperty("items").EnumerateArray());
        Assert.Equal(rootId, parent.GetProperty("associatedObject").GetProperty("id").GetString());
        Assert.Equal("空间显示", parent.GetProperty("fields").GetProperty("Origin_DisplayValue").GetString());
    }

    private static LinkQueryCatalog DisplayCatalog(IServiceProvider provider)
    {
        var plan = provider.GetRequiredService<CompiledPersistencePlan>();
        var original = Assert.Single(plan.Models, model => model.LinkQuery is not null);
        var query = original.LinkQuery!;
        var types = query.Endpoints.Keys.Append("Person").ToArray();
        var folder = plan.Models.Single(model => model.LogicalKey == "WorkspaceFolder");
        var origin = folder.Fields.Single(field => field.Name == "OriginSpaceId");
        var endpoints = query.Endpoints.ToDictionary();
        endpoints.Add("Person", new() { ResourceKey = "organization.person" });
        var columns = query.Columns.Select(column => column with
        {
            Nullable = column.Nullable || column.Field == "ObjectType",
            Mappings = column.Mappings.Append(new KeyValuePair<string, string?>("Person", column.Field switch
            { "ObjectType" => null, "Code" => "PersonNo", "Name" => "ENAME", _ => column.Field })).ToDictionary()
        }).ToList();
        columns.Add(new() { Field = "Origin", ValueType = origin.ValueType.ToString(), Nullable = true,
            Mappings = types.ToDictionary(type => type, type => type == folder.LogicalKey ? "OriginSpaceId" : (string?)null) });
        columns.Add(new() { Field = "BirthDate", ValueType = "CalendarDate", Nullable = true,
            Mappings = types.ToDictionary(type => type, type => type == "Person" ? "BirthDate" : (string?)null) });
        var models = plan.Models.Select(model => model == folder ? model with
        {
            Fields = model.Fields.Select(field => field == origin ? field with { Kind = FieldKind.Reference,
                DisplayRule = new("reference", "WorkspaceSpace", "EID", "ENAME") } : field).ToArray()
        } : model == original ? model with
        {
            LinkedTypes = [.. model.LinkedTypes, new ModelMetadataLinkedType { Parent = "WorkspaceFolder", Child = "Person" }],
            LinkQuery = query with { Endpoints = endpoints, Columns = columns }
        } : model).ToArray();
        var modules = provider.GetRequiredService<CanonicalModulePlan>();
        modules = modules with { Modules = modules.Modules.Select(module => module.ModuleId == "Workspace"
            ? module with { Dependencies = [.. module.Dependencies, "Organization"] } : module).ToArray() };
        return new(plan with { Models = models }, provider.GetRequiredService<IStandardCrudResourceRegistry>(),
            provider.GetServices<DataScopeResource>(), modules);
    }

    private sealed class FolderSpaceDisplayResolver : IStandardCrudDisplayValueResolver
    {
        public string ResourceKey => "workspace.folder";
        public List<string?[]> Calls { get; } = [];
        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
            IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
            IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
            CancellationToken cancellationToken = default)
        {
            var field = Assert.Single(requestedDisplayFields);
            Assert.Equal("OriginSpaceId", field.SourceField);
            Assert.Equal("WorkspaceSpace", field.TargetModelName);
            Calls.Add(rows.Select(row => row["OriginSpaceId"] as string).ToArray());
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(rows.Select(_ =>
                (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { [field.Field] = "空间显示" }).ToArray());
        }
    }
}
