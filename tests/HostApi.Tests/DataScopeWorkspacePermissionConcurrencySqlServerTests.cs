using System.Data.Common;
using System.Net;
using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeWorkspacePermissionConcurrencySqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "授权提交持有真实图锁，来源移除等待其提交，不能在旧LID校验后交错提交")]
    public async Task PermissionSave_SerializesSourceRemoval_WithGraphThenObjectLock()
    {
        var graph = new GraphCommand();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(graph)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var target = await CreateAsync(fixture, admin, position, "Part", "parts", "createPart", "PermissionLockPart295");
        var manage = fixture.FeatureCatalog.First(point => point.BehaviorKey == "workspace.folder.authorize"
            && point.Key == "authorizeContent" && point.ActionScope == "list.rowActions");
        var input = new DataScopeObjectDto("part.part", target.Id,
            [new("PermissionLockAll295", "all", null, 1, new Dictionary<string, string> { ["read"] = "allow" })]);
        var path = "/api/workspace/content/" + target.LinkId + "/permissions?queryScope="
            + Uri.EscapeDataString(JsonSerializer.Serialize(position, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Fault.AfterRegister = async (change, token) =>
        {
            if (change.EventType != "dataScope.object.changed") return;
            reached.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        var saving = FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, input);
        Task<HttpResponseMessage>? removing = null;
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
            using (var probe = fixture.Services.CreateScope())
            {
                var db = probe.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
                await db.Database.OpenConnectionAsync();
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT APPLOCK_TEST(N'public', N'Workspace.FolderGraph', N'Exclusive', N'Session');";
                Assert.Equal(0, Assert.IsType<int>(await command.ExecuteScalarAsync()));
            }
            graph.Watch = true;
            removing = FeatureHttpTestRequests.SendAsync(admin, Point(fixture, "workspace.folder.remove"), HttpMethod.Delete,
                "/api/workspace/content/references/" + target.LinkId, new { position });
            await graph.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.False(removing.IsCompleted);
            release.TrySetResult();
            using var saved = await saving;
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
            using var removed = await removing;
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        }
        finally
        {
            release.TrySetResult();
            (await saving).Dispose();
            if (removing is not null) (await removing).Dispose();
            fixture.Fault.AfterRegister = null;
            graph.Watch = false;
        }
        using var stale = await FeatureHttpTestRequests.SendAsync(admin, manage, HttpMethod.Put, path, input);
        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        var http = new FeatureHttpTestRequests(fixture);
        var audit = await FeatureHttpTestRequests.ReadAsync(admin, http.Audit(),
            "/api/audit/business-changes?TargetId=" + Uri.EscapeDataString("part.part/" + target.Id) + "&EventType=dataScope.object.changed");
        Assert.Single(audit.GetProperty("items").EnumerateArray());
    }

    private sealed class GraphCommand : DbCommandInterceptor
    {
        internal volatile bool Watch;
        internal TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // 复用真实图写命令的入场点；只观察第二请求，不阻断或替代SQL锁。
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (Watch && command.CommandText.Contains("Workspace.FolderGraph", StringComparison.Ordinal)) Reached.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
