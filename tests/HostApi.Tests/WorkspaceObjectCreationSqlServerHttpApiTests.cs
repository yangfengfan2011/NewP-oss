using System.Data.Common;
using System.Net;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class WorkspaceObjectCreationSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实文件夹中新建零组件和图文档，复用所属对象校验并原子建立关系")]
    public async Task CreateObjects_UsesCurrentFolderAndOwningValidation()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var entry = Point(fixture, "workspace.spaces.access");
        var position = await RootAsync(admin, entry);
        using var folderResponse = await FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.create"), HttpMethod.Post,
            "/api/workspace/content/folders", new { position, values = new { ENAME = "选中的子文件夹" } });
        Assert.Equal(HttpStatusCode.OK, folderResponse.StatusCode);
        var folderId = JsonSerializer.Deserialize<JsonElement>(await folderResponse.Content.ReadAsStringAsync()).GetProperty("id").GetString()!;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var link = await db.Set<Dictionary<string, object?>>("Workspace/WorkspaceFolderContent").SingleAsync();
            position = position with { LinkIds = [(string)link["LID"]!] };
        }
        foreach (var (kind, model, action) in Targets)
        {
            var create = Point(fixture, "workspace.folder." + action);
            var url = "/api/workspace/content/" + kind;
            using var denied = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, url,
                new { position, values = new { ECODE = "DENIED", ENAME = "错误功能位置" } });
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var invalid = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, url,
                new { position, values = new { ECODE = "", ENAME = "缺少编码" } });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Contains("CodeRequired", await invalid.Content.ReadAsStringAsync());
            using var forged = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, url,
                new { position, values = new { ECODE = "FORGED", ENAME = "伪造身份", EID = "chosen" } });
            Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
            using var missing = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, url,
                new { position = new { position.SpaceId, position.RootFolderId, LinkIds = new[] { "missing" } }, values = new { ECODE = "MISSING", ENAME = "失效位置" } });
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            using var created = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, url,
                new { position, values = new { ECODE = "FOLDER-CREATE", ENAME = "文件夹内新对象", Description = "说明" } });
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            var receipt = JsonSerializer.Deserialize<JsonElement>(await created.Content.ReadAsStringAsync());
            Assert.True(receipt.GetProperty("succeeded").GetBoolean());
            Assert.False(receipt.TryGetProperty("id", out _));
            using var duplicate = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, url,
                new { position, values = new { ECODE = "FOLDER-CREATE", ENAME = "重复编码" } });
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var entity = await db.Set<Dictionary<string, object?>>(model).SingleAsync();
            var link = await db.Set<Dictionary<string, object?>>("Workspace/WorkspaceFolderContent")
                .SingleAsync(row => EF.Property<string>(row, "ChildModelKey") == model);
            Assert.Equal(entity["EID"], link["CEID"]);
            Assert.Equal(folderId, link["PEID"]);
            Assert.Equal(entity["CreatedBy"], link["LCreatedBy"]);
            Assert.Equal(entity["CreatedAt"], link["LCreatedAt"]);
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "实体或关联失败整体回滚，父编辑及新对象创建裁决在事务内生效")]
    public async Task CreateObjects_RollsBackLinkFailureAndRechecksObjectPermissions()
    {
        var fault = new LinkInsertFault();
        var decisions = new CreationDecisions();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(fault));
            services.AddSingleton<IDataScopeObjectDecisionProvider>(decisions);
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        var position = await RootAsync(admin, Point(fixture, "workspace.spaces.access"));
        foreach (var (kind, model, action) in Targets)
        {
            var create = Point(fixture, "workspace.folder." + action);
            var url = "/api/workspace/content/" + kind;
            var body = new { position, values = new { ECODE = "ATOMIC", ENAME = "不能留下孤立对象" } };
            decisions.AllowParent = false;
            using var parentDenied = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, url, body);
            Assert.Equal(HttpStatusCode.Forbidden, parentDenied.StatusCode);
            decisions.AllowParent = true;
            decisions.AllowCreate = false;
            using var createDenied = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, url, body);
            Assert.Equal(HttpStatusCode.Forbidden, createDenied.StatusCode);
            decisions.AllowCreate = true;
            fault.Enabled = true;
            using var failed = await FeatureHttpTestRequests.SendAsync(admin, create, HttpMethod.Post, url, body);
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            fault.Enabled = false;
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.Equal(0, await db.Set<Dictionary<string, object?>>(model).CountAsync());
            Assert.Equal(0, await db.Set<Dictionary<string, object?>>("Workspace/WorkspaceFolderContent").CountAsync());
        }
    }

    private static readonly (string Kind, string Model, string Action)[] Targets =
        [("parts", "Part/Part", "createPart"), ("documents", "Document/Document", "createDocument")];
    private static FeaturePoint Point(BusinessAuditHostFixture fixture, string behavior) => Assert.Single(fixture.FeatureCatalog, point => point.BehaviorKey == behavior);
    private sealed record Position(string SpaceId, string RootFolderId, string[] LinkIds);
    private static async Task<Position> RootAsync(HttpClient admin, FeaturePoint entry)
    {
        using var response = await FeatureHttpTestRequests.SendAsync(admin, entry, HttpMethod.Post, "/api/workspace/spaces/public/initialize");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        return new(root.GetProperty("spaceId").GetString()!, root.GetProperty("rootFolderId").GetString()!, []);
    }
    private sealed class LinkInsertFault : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("INSERT INTO", StringComparison.Ordinal)
                && command.CommandText.Contains("WorkspaceFolderContent", StringComparison.Ordinal)) throw new InvalidOperationException("WORKSPACE_CREATE_LINK_FAILURE");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class CreationDecisions : IDataScopeObjectDecisionProvider
    {
        public bool AllowParent { get; set; } = true;
        public bool AllowCreate { get; set; } = true;
        public Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken cancellationToken = default) => Task.FromResult(AllowParent);
        public Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate, string actionKey, CancellationToken cancellationToken = default) => Task.FromResult(AllowCreate);
    }
}
