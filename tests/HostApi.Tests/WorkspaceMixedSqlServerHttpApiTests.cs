using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Testing;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Modules.Authorization.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Presentation;
using Xunit.Abstractions;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceMixedSqlServerHttpApiTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "混合端点对象权限与强制业务范围交集，候选撤权/末引用移除重加及异常存储")]
    public async Task MixedReferences_IntersectRealPermissionsAndBusinessScopeBeforePagingAndRecheckWrites()
    {
        var business = new PartBusinessConstraint();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            var registration = Assert.Single(services, service => service.ServiceType == typeof(CompiledPersistencePlan));
            var plan = Assert.IsType<CompiledPersistencePlan>(registration.ImplementationInstance); services.Remove(registration);
            services.AddSingleton(plan with { Models = plan.Models.Select(model => model.LinkQuery is not { } query ? model : model with {
                LinkQuery = query with { Endpoints = query.Endpoints.ToDictionary(pair => pair.Key, pair => pair.Key == "Part" ? pair.Value with { ConstraintKey = business.Key } : pair.Value) } }).ToArray() });
            services.AddSingleton<ILinkQueryEndpointConstraint>(business);
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access"); var add = Point(fixture, "workspace.folder.add"); var remove = Point(fixture, "workspace.folder.remove");
        var partRead = Point(fixture, "part.part.read"); var documentRead = Point(fixture, "document.document.read");
        var root = await ContentRoot(fixture, admin); var position = Position(root);
        var targets = new List<(string Model, string Route, FeaturePoint Read, string Id)>();
        foreach (var (model, route, create, read) in new[] { ("Part", "/api/part/parts", "part.part.create", partRead), ("Document", "/api/document/documents", "document.document.create", documentRead) })
        {
            var created = await Post(admin, Point(fixture, create), route, new { ECODE = "INDEPENDENT", ENAME = "可读取元数据" },
                new FeatureHttpTestRequests(fixture).Declaration(create, Point(fixture, create)));
            var id = created.GetProperty("fields").GetProperty("EID").GetString()!; targets.Add((model, route, read, id));
            await Post(admin, add, "/api/workspace/content/references", new { position, target = new { modelKey = model, id } });
        }
        await Post(admin, Point(fixture, "workspace.folder.create"), "/api/workspace/content/folders", new { position, values = new { ENAME = "可读文件夹" } });
        var excluded = await Post(admin, Point(fixture, "part.part.create"), "/api/part/parts", new { ECODE = "EXCLUDED", ENAME = "强制业务排除" }, new FeatureHttpTestRequests(fixture).Declaration("part.part.create", Point(fixture, "part.part.create")));
        var excludedId = excluded.GetProperty("fields").GetProperty("EID").GetString();
        await Post(admin, add, "/api/workspace/content/references", new { position, target = new { modelKey = "Part", id = excludedId } });
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "MixedWriter", "混合写入", "mixed-writer", "混合写入账号", entry, add, remove, partRead, documentRead);
        using var ordinary = actor.Client;
        business.Enabled = true;
        var independent = await FeatureHttpTestRequests.ReadAsync(ordinary, partRead, "/api/part/parts?keyword=EXCLUDED&fields=EID,ECODE,ENAME");
        Assert.Equal(1, independent.GetProperty("total").GetInt32());
        var list = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Url(root) + "&pageSize=2");
        Assert.Equal(3, list.GetProperty("total").GetInt32()); Assert.Equal(2, list.GetProperty("items").GetArrayLength());
        using (var rejected = await FeatureHttpTestRequests.SendAsync(ordinary, add, HttpMethod.Post, "/api/workspace/content/references", new { position, target = new { modelKey = "Part", id = excludedId } }))
            Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        foreach (var target in targets)
        {
            var candidate = await FeatureHttpTestRequests.ReadAsync(ordinary, target.Read, target.Route + "?keyword=INDEPENDENT&fields=EID,ECODE,ENAME");
            Assert.Equal(target.Id, Assert.Single(candidate.GetProperty("items").EnumerateArray()).GetProperty("EID").GetString());
            await Rule(fixture, admin, "Deny" + target.Model.Replace('/', '_'), target.Model == "Part" ? "part.part" : "document.document", target.Id);
            var hidden = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Url(root) + "&pageSize=1"); Assert.Equal(2, hidden.GetProperty("total").GetInt32());
            using var denied = await FeatureHttpTestRequests.SendAsync(ordinary, add, HttpMethod.Post, "/api/workspace/content/references", new { position, target = new { modelKey = target.Model, id = target.Id } });
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            using var deniedDetail = await FeatureHttpTestRequests.SendAsync(ordinary, target.Read, HttpMethod.Get, target.Route + "/" + target.Id); Assert.Equal(HttpStatusCode.NotFound, deniedDetail.StatusCode);
            Assert.Equal(0, (await FeatureHttpTestRequests.ReadAsync(ordinary, target.Read, target.Route + "?keyword=INDEPENDENT")).GetProperty("total").GetInt32());
            await Rule(fixture, admin, "Deny" + target.Model.Replace('/', '_'), target.Model == "Part" ? "part.part" : "document.document", "unused");
            var all = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Url(root));
            var row = Assert.Single(all.GetProperty("items").EnumerateArray(), item => item.GetProperty("associatedObject").GetProperty("modelKey").GetString() == target.Model);
            var before = await FeatureHttpTestRequests.ReadAsync(ordinary, target.Read, target.Route + "/" + target.Id);
            using var removed = await FeatureHttpTestRequests.SendAsync(ordinary, remove, HttpMethod.Delete, "/api/workspace/content/references/" + row.GetProperty("linkId").GetString(), new { position }, WorkspaceClipboardSqlServerHttpApiTests.ClipboardDeclaration(fixture, remove));
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
            Assert.Equal(1, (await FeatureHttpTestRequests.ReadAsync(ordinary, target.Read, target.Route + "?keyword=INDEPENDENT")).GetProperty("total").GetInt32());
            await Rule(fixture, admin, "Deny" + target.Model.Replace('/', '_'), target.Model == "Part" ? "part.part" : "document.document", target.Id);
            using (var revokedAfterLastRemoval = await FeatureHttpTestRequests.SendAsync(ordinary, add, HttpMethod.Post, "/api/workspace/content/references", new { position, target = new { modelKey = target.Model, id = target.Id } }))
                Assert.Equal(HttpStatusCode.NotFound, revokedAfterLastRemoval.StatusCode);
            await Rule(fixture, admin, "Deny" + target.Model.Replace('/', '_'), target.Model == "Part" ? "part.part" : "document.document", "unused");
            await Post(ordinary, add, "/api/workspace/content/references", new { position, target = new { modelKey = target.Model, id = target.Id } });
            var after = await FeatureHttpTestRequests.ReadAsync(ordinary, target.Read, target.Route + "/" + target.Id);
            Assert.Equal(before.GetProperty("fields").GetProperty("UpdatedAt").GetString(), after.GetProperty("fields").GetProperty("UpdatedAt").GetString());
        }
        business.Fail = true;
        using (var failed = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, Url(root))) Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        business.Fail = false;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>(); var links = db.Set<Dictionary<string, object?>>("WorkspaceFolderContent");
            var parentId = root.GetProperty("contentFolderId").GetString()!;
            var seed = await links.AsNoTracking().FirstAsync(row => EF.Property<string>(row, "PEID") == parentId);
            links.Add(new(seed) { ["LID"] = "missing-part", ["ChildModelKey"] = "Part", ["CEID"] = "no-target", ["TreeOrdinal"] = null }); await db.SaveChangesAsync();
        }
        var missing = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Url(root) + "&pageSize=1"); Assert.Equal(3, missing.GetProperty("total").GetInt32());
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>(); var links = db.Set<Dictionary<string, object?>>("WorkspaceFolderContent");
            Assert.Equal(6, await links.CountAsync());
            var corrupt = await links.SingleAsync(row => EF.Property<string>(row, "LID") == "missing-part"); corrupt["ChildModelKey"] = "Unknown/Type"; await db.SaveChangesAsync();
        }
        using (var corrupt = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, Url(root) + "&types=Part")) Assert.Equal(HttpStatusCode.InternalServerError, corrupt.StatusCode);
        using (var scope = fixture.Services.CreateScope()) Assert.Equal(6, await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolderContent").CountAsync());
    }

    private static async Task Rule(BusinessAuditHostFixture fixture, HttpClient admin, string key, string resource, string id)
    {
        var rule = new DataScopeRuleDto(key, key, null, key, true, resource, new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(id)),
            [new(key + "Subject", "all", null, 0, new Dictionary<string, string> { ["read"] = "deny" })], []);
        using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, Point(fixture, "authorization.dataScope.save"), HttpMethod.Put, "/api/authorization/data-scope/rules/" + key, rule);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private sealed class PartBusinessConstraint : ILinkQueryEndpointConstraint
    {
        public string Key => "test.part.business";
        public string ModelKey => "Part";
        public bool Enabled { get; set; }
        public bool Fail { get; set; }
        public Task<Expression<Func<Dictionary<string, object?>, bool>>> ReadPredicateAsync(LinkQueryEndpoint endpoint, CancellationToken token)
        {
            if (Fail) throw new InvalidOperationException("TEST_PART_CONSTRAINT_FAILURE");
            Expression<Func<Dictionary<string, object?>, bool>> predicate = row => !Enabled || EF.Property<string>(row, "ENAME") != "强制业务排除";
            return Task.FromResult(predicate);
        }
    }
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实混合SQL全局分页、跨模型同ID、空编码及窄批量显示")]
    public async Task InterleavedModels_UseGlobalStableSqlPagingAndCanonicalPageDisplay()
    {
        var names = new UserDirectoryObservation();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            var registered = services.Last(service => service.ServiceType == typeof(IUserDisplayNameDirectory));
            Assert.NotNull(registered.ImplementationFactory); services.Remove(registered);
            services.AddScoped<IUserDisplayNameDirectory>(provider => new ObservedUserDirectory((IUserDisplayNameDirectory)registered.ImplementationFactory(provider), names));
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var entry = Point(fixture, "workspace.spaces.access");
        var root = await Post(admin, entry, "/api/workspace/spaces/public/initialize", null); var position = Position(root);
        await Post(admin, Point(fixture, "workspace.folder.create"), "/api/workspace/content/folders", new { position, values = new { ENAME = "seed" } });
        foreach (var (route, behavior) in new[] { ("/api/part/parts", "part.part.create"), ("/api/document/documents", "document.document.create") })
            await Post(admin, Point(fixture, behavior), route, new { ECODE = "seed", ENAME = "seed" }, new FeatureHttpTestRequests(fixture).Declaration(behavior, Point(fixture, behavior)));
        var types = new[] { "Document", "Part", "WorkspaceFolder" };
        // 关系 LID 是夹具的独立身份，不随模型技术名切换重新命名。
        var relationPrefixes = new Dictionary<string, string>
        {
            ["Document"] = "Document-Document", ["Part"] = "Part-Part", ["WorkspaceFolder"] = "Workspace-WorkspaceFolder"
        };
        string database;
        using (var serviceScope = fixture.Services.CreateScope())
        {
            var db = serviceScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>(); database = db.Database.GetDbConnection().Database;
            var links = db.Set<Dictionary<string, object?>>("WorkspaceFolderContent");
            var link = await links.AsNoTracking().SingleAsync();
            foreach (var type in types)
            {
                var entities = db.Set<Dictionary<string, object?>>(type);
                var seed = await entities.AsNoTracking().FirstAsync(row => EF.Property<string>(row, "ENAME") == "seed");
                for (var index = 0; index < 10; index++)
                {
                    var id = "same-id-" + index.ToString("D2");
                    var entity = new Dictionary<string, object?>(seed) { ["EID"] = id, ["ENAME"] = "mix-" + index.ToString("D2"), ["UpdatedAt"] = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc) };
                    if (type != "WorkspaceFolder") entity["ECODE"] = "CODE-" + index.ToString("D2");
                    entities.Add(entity);
                    links.Add(new(link) { ["LID"] = relationPrefixes[type] + "-" + index.ToString("D2"), ["CEID"] = id, ["ChildModelKey"] = type, ["TreeOrdinal"] = type == "WorkspaceFolder" ? index + 2L : (long?)null });
                }
            }
            await db.SaveChangesAsync();
            // 规模夹具在无 HTTP 主体的准备范围保存，随后明确固定其用户/时间显示输入。
            foreach (var type in types)
                await db.Set<Dictionary<string, object?>>(type).Where(row => EF.Property<string>(row, "ENAME").StartsWith("mix-"))
                    .ExecuteUpdateAsync(update => update.SetProperty(row => EF.Property<string>(row, "UpdatedBy"), fixture.ActorId)
                        .SetProperty(row => EF.Property<DateTime>(row, "UpdatedAt"), new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc)));
        }
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "MixedReader", "混合读取", "mixed-reader", "混合读取账号", entry);
        using var ordinary = actor.Client;
        var url = Url(root) + "&keyword=mix-&sortBy=Name&sortDirection=asc&pageSize=7&fields=LID,Name";
        var expected = Enumerable.Range(0, 10).SelectMany(index => types.Select(type => relationPrefixes[type] + "-" + index.ToString("D2"))).ToArray();
        using var costs = new WorkspaceReferenceSqlServerHttpApiTests.SqlObservation(database); costs.Begin();
        var first = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, url);
        var observation = costs.End();
        Assert.Equal(30, first.GetProperty("total").GetInt32());
        Assert.Equal(expected.Take(7), first.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("linkId").GetString()));
        Assert.All(first.GetProperty("items").EnumerateArray(), row => Assert.Equal(new[] { "LID", "Name" }, row.GetProperty("fields").EnumerateObject().Select(field => field.Name)));
        for (var page = 2; page <= 5; page++)
        {
            var result = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, url + "&pageIndex=" + page);
            Assert.Equal(30, result.GetProperty("total").GetInt32());
            Assert.Equal(expected.Skip((page - 1) * 7).Take(7), result.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("linkId").GetString()));
        }
        var plan = await costs.ReplayPlanAsync(fixture);
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "WorkspaceMixedGlobalPage", models = 3, relations = 30, materialized = 7, observation, plan }));
        var display = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Url(root) + "&keyword=mix-00&sortBy=Name&sortDirection=asc");
        Assert.Equal(3, display.GetProperty("total").GetInt32());
        Assert.Equal(types, display.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("associatedObject").GetProperty("modelKey").GetString()));
        foreach (var row in display.GetProperty("items").EnumerateArray())
        {
            Assert.Equal("same-id-00", row.GetProperty("associatedObject").GetProperty("id").GetString());
            var fields = row.GetProperty("fields");
            Assert.Equal(fixture.ActorId, fields.GetProperty("UpdatedBy").GetString());
            Assert.Equal("审计管理员", fields.GetProperty("UpdatedBy_DisplayValue").GetString());
            Assert.Equal("2026-10-08T00:00:00Z", fields.GetProperty("UpdatedAt").GetString());
            Assert.False(string.IsNullOrWhiteSpace(fields.GetProperty("UpdatedAt_DisplayValue").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(fields.GetProperty("ObjectType_DisplayValue").GetString()));
            Assert.False(fields.TryGetProperty("Description", out _));
        }
        names.Reset();
        var batchDisplay = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Url(root) + "&keyword=mix-&sortBy=Name&sortDirection=asc&pageSize=7");
        Assert.Equal(7, batchDisplay.GetProperty("items").GetArrayLength());
        Assert.Equal(3, names.Calls); Assert.All(names.BatchSizes, size => Assert.Equal(1, size));
        output.WriteLine(JsonSerializer.Serialize(new { scenario = "WorkspaceMixedFormalDisplay", materialized = 7, formalDirectoryCalls = names.Calls, distinctUserIdsPerModelBatch = names.BatchSizes.ToArray() }));
        await Rule(fixture, admin, "HideAuditUser", "identity.account", fixture.ActorId);
        var hiddenUser = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Url(root) + "&keyword=mix-00");
        Assert.All(hiddenUser.GetProperty("items").EnumerateArray(), row => {
            Assert.Equal(fixture.ActorId, row.GetProperty("fields").GetProperty("UpdatedBy").GetString());
            Assert.Equal("***", row.GetProperty("fields").GetProperty("UpdatedBy_DisplayValue").GetString());
        });
        var nullCodes = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Url(root) + "&keyword=mix-&Code:isNull=true");
        Assert.Equal(10, nullCodes.GetProperty("total").GetInt32());
        Assert.All(nullCodes.GetProperty("items").EnumerateArray(), row => Assert.Equal(JsonValueKind.Null, row.GetProperty("fields").GetProperty("Code").ValueKind));
        var filtered = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Url(root) + "&keyword=CODE-00&ObjectType=Part");
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        var defaultOrder = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Url(root) + "&keyword=mix-&pageSize=7&fields=LID,Name");
        Assert.Equal(Enumerable.Range(0, 7).Select(index => "Document-Document-" + index.ToString("D2")), defaultOrder.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("linkId").GetString()));
        var codeOrder = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Url(root) + "&keyword=mix-&sortBy=Code&sortDirection=asc&pageSize=11&fields=LID,Code");
        Assert.Equal(Enumerable.Range(0, 10).Select(index => "Workspace-WorkspaceFolder-" + index.ToString("D2")).Append("Document-Document-00"), codeOrder.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("linkId").GetString()));
        foreach (var input in new[] { "&fields=Description", "&sortBy=Name_DisplayValue", "&Name_DisplayValue=x", "&UpdatedAt:gte=2026-01-01", "&Name:unknown=x", "&types=Unknown%2FType", "&ObjectType:contains=Part", "&queryScope=invalid" })
        {
            using var invalid = await FeatureHttpTestRequests.SendAsync(ordinary, entry, HttpMethod.Get, Url(root) + input);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        var injected = await FeatureHttpTestRequests.ReadAsync(ordinary, entry, Url(root) + "&keyword=" + Uri.EscapeDataString("' OR 1=1 --"));
        Assert.Equal(0, injected.GetProperty("total").GetInt32());
    }

    private sealed class UserDirectoryObservation
    {
        public int Calls { get; set; }
        public List<int> BatchSizes { get; } = [];
        public void Reset() { Calls = 0; BatchSizes.Clear(); }
    }

    // 仅观察并转发正式目录；真实 Identity 读取规则、SQL 与 canonical planner 均保持执行。
    private sealed class ObservedUserDirectory(IUserDisplayNameDirectory inner, UserDirectoryObservation observation) : IUserDisplayNameDirectory
    {
        public Task<IReadOnlyDictionary<string, string>> FindManyAsync(IReadOnlyCollection<string> accountIds, CancellationToken cancellationToken = default)
        {
            observation.Calls++; observation.BatchSizes.Add(accountIds.Count);
            return inner.FindManyAsync(accountIds, cancellationToken);
        }
    }

    private static object Position(JsonElement root) => new { spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = root.GetProperty("rootFolderId").GetString(), linkIds = root.TryGetProperty("linkIds", out var links) ? links.EnumerateArray().Select(id => id.GetString()!).ToArray() : Array.Empty<string>() };
    private static string Url(JsonElement root) => "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId=" + Uri.EscapeDataString((root.TryGetProperty("contentFolderId", out var contentFolder) ? contentFolder : root.GetProperty("rootFolderId")).GetString()!)
        + "&queryScope=" + Uri.EscapeDataString(JsonSerializer.Serialize(new { spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = root.GetProperty("rootFolderId").GetString(), linkIds = root.TryGetProperty("linkIds", out var links) ? links.GetRawText() : "[]" }));
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "三类正式业务输入经同一Link查询进入混合列表")]
    public async Task ThreeRealModels_AddAndReadThroughOneContentResource()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true, includePart: true, includeDocument: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = Point(fixture, "workspace.spaces.access");
        var root = await ContentRoot(fixture, admin);
        var position = new { spaceId = root.GetProperty("spaceId").GetString(), rootFolderId = root.GetProperty("rootFolderId").GetString(), linkIds = root.GetProperty("linkIds").EnumerateArray().Select(item => item.GetString()!).ToArray() };
        await Post(admin, Point(fixture, "workspace.folder.create"), "/api/workspace/content/folders", new { position, values = new { ENAME = "B-folder" } });
        foreach (var (model, route, behavior, name) in new[] { ("Part", "/api/part/parts", "part.part.create", "A-part"), ("Document", "/api/document/documents", "document.document.create", "C-document") })
        {
            var created = await Post(admin, Point(fixture, behavior), route, new { ECODE = "SAME-CODE", ENAME = name });
            var id = created.GetProperty("fields").GetProperty("EID").GetString();
            await Post(admin, Point(fixture, "workspace.folder.add"), "/api/workspace/content/references", new { position, target = new { modelKey = model, id } });
        }
        var scope = JsonSerializer.Serialize(new { position.spaceId, position.rootFolderId, linkIds = JsonSerializer.Serialize(position.linkIds) });
        var url = "/api/links/workspace.content?direction=forward&anchorModelKey=WorkspaceFolder&anchorId=" + Uri.EscapeDataString(root.GetProperty("contentFolderId").GetString()!) + "&queryScope=" + Uri.EscapeDataString(scope) + "&sortBy=Name&sortDirection=asc";
        var result = await FeatureHttpTestRequests.ReadAsync(admin, entry, url);
        Assert.Equal(3, result.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "A-part", "B-folder", "C-document" }, result.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("fields").GetProperty("Name").GetString()));
    }

    private static async Task<JsonElement> ContentRoot(BusinessAuditHostFixture fixture, HttpClient admin)
    {
        var position = await DocumentFilesSqlServerHttpApiTests.ContentAsync(fixture, admin);
        using var scope = fixture.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("WorkspaceFolderContent")
            .AsNoTracking().SingleAsync(row => EF.Property<string>(row, "LID") == position.LinkIds[0]);
        return JsonSerializer.SerializeToElement(new { spaceId = position.SpaceId, rootFolderId = position.RootFolderId,
            linkIds = position.LinkIds, contentFolderId = (string)row["CEID"]! });
    }
    // 原 HTTP 场景消费列表位置；树菜单的同一行为拥有独立功能位置。
    private static FeaturePoint Point(BusinessAuditHostFixture fixture, string behavior) => Assert.Single(fixture.FeatureCatalog,
        point => point.BehaviorKey == behavior && (behavior is not ("workspace.folder.remove" or "workspace.folder.editObject")
            || point.ModuleId == "Workspace" && point.StaticScope == "folder" && point.ActionScope == "list.rowActions"
                && point.Key == (behavior == "workspace.folder.remove" ? "remove" : "edit")));
    private static async Task<JsonElement> Post(HttpClient client, FeaturePoint point, string path, object? body, ActionDataAccessRequirement? declaration = null)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(client, point, HttpMethod.Post, path, body, declaration);
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
