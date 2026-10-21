using System.Data.Common;
using System.Net;
using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class FolderStructureParentAuthorizationSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实共享节点错误父facts在任何配置读取前拒绝，多父语义差异在原锁内拒绝并整图回滚")]
    public async Task StructureInitialization_RejectsCrossedCreatedFactsAndNonEquivalentNewParentsAtomically()
    {
        var state = new CreatedSourceProbe();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.Replace(ServiceDescriptor.Scoped<IDataScopeObjectInitializer>(provider => new CreatedParentInitializer(
                ActivatorUtilities.CreateInstance<DataScopeObjectInitializer>(provider), provider.GetRequiredService<EfPlatformDbContext>(), state)));
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(new CreatedSourceCommands(state)));
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var parentLink = Assert.Single(await RowsAsync(fixture, "WorkspaceFolderContent"), row => Equals(row["LID"], position.LinkIds[^1]));
        await SaveAsync(fixture, admin, position with { LinkIds = [] },
            new("WorkspaceFolder", (string)parentLink["CEID"]!, position.LinkIds[^1], "归属维护目录"),
            [new("ParentSource320", "creator", null, 3, new Dictionary<string, string> { ["delete"] = "allow" }, ExpiresAt: "2099-05-01T00:00:00Z")]);
        var (source, _) = await BuildCapacityGraphAsync(fixture, admin, position, 1);
        var feature = Point(fixture, "workspace.folder.pasteFolderStructure");
        var declaration = new FeatureHttpTestRequests(fixture).Declaration("workspace.folder.pasteFolderStructure", feature);
        var before = await TransactionSnapshotAsync(fixture);
        foreach (var mismatch in new[] { "missing", "parent-id", "parent-values", "parent-resource", "target", "unknown-proof", "ordinary-shape", "different-opinion", "different-expiry" })
        {
            state.Mode = mismatch; state.Reached = false; state.ConfigurationCommands = 0;
            using var response = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post,
                "/api/workspace/content/folders/copy-structure", new { mode = "copy", sourcePosition = position,
                    linkIds = new[] { source.LinkId }, targetPosition = position, name = "SourceRejected320-" + mismatch, copyParentAuthorization = true }, declaration);
            var semanticMismatch = mismatch.StartsWith("different-", StringComparison.Ordinal);
            Assert.Equal(semanticMismatch ? HttpStatusCode.InternalServerError : HttpStatusCode.BadRequest, response.StatusCode);
            Assert.True(state.Reached, mismatch);
            if (semanticMismatch) Assert.True(state.ConfigurationCommands > 0, mismatch);
            else Assert.Equal(0, state.ConfigurationCommands);
            Assert.Equal(before, await TransactionSnapshotAsync(fixture));
        }
        state.Mode = "control"; state.Reached = false; state.ConfigurationCommands = 0;
        using var accepted = await FeatureHttpTestRequests.SendAsync(admin, feature, HttpMethod.Post,
            "/api/workspace/content/folders/copy-structure", new { mode = "copy", sourcePosition = position,
                linkIds = new[] { source.LinkId }, targetPosition = position, name = "SourceAccepted320", copyParentAuthorization = true }, declaration);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.True(state.Reached);
        Assert.True(state.ConfigurationCommands > 0);
    }

    private sealed class CreatedSourceProbe
    {
        internal string Mode = "control";
        internal bool Reached;
        internal bool Capturing;
        internal int ConfigurationCommands;
    }

    // 只观测被破坏的共享节点调用，前序正常根/新父初始化不混入零SQL断言。
    private sealed class CreatedSourceCommands(CreatedSourceProbe state) : DbCommandInterceptor
    {
        private void Observe(DbCommand command)
        {
            if (state.Capturing && command.CommandText.Contains("DataScopeObject", StringComparison.Ordinal)) state.ConfigurationCommands++;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        { Observe(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken token = default)
        { Observe(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<object> result, CancellationToken token = default)
        { Observe(command); return ValueTask.FromResult(result); }
    }

    // 在真实factory形成来源后只破坏一个事实，权限和克隆仍由实际初始化器执行。
    private sealed class CreatedParentInitializer(DataScopeObjectInitializer inner, EfPlatformDbContext db, CreatedSourceProbe state) : IDataScopeObjectInitializer
    {
        public DataScopeObjectCreationProof CaptureCreatedObjects(string resource, IReadOnlyList<DataExecutionWriteFacts> objects) =>
            inner.CaptureCreatedObjects(resource, objects);
        public Task<bool> InitializeFromParentAsync(string resource, string parent, string target, DataScopeObjectInitializationSource source, CancellationToken token = default) =>
            inner.InitializeFromParentAsync(resource, parent, target, source, token);
        public async Task<DataScopeObjectInitialization> InitializeFromParentsAsync(string resource, IReadOnlyList<string> parents, string target,
            DataScopeObjectInitializationSource source, CancellationToken token = default)
        {
            if (source is not DataScopeObjectInitializationSource.CreatedFromCreatedParents graph || parents.Count < 2)
                return await inner.InitializeFromParentsAsync(resource, parents, target, source, token);
            state.Reached = true;
            if (state.Mode.StartsWith("different-", StringComparison.Ordinal))
            {
                var parentId = graph.Parents[0].ObjectId;
                var subject = await db.Set<Dictionary<string, object?>>("DataScopeObjectSubject").SingleAsync(row => EF.Property<string>(row, "ObjectId") == parentId, token);
                if (state.Mode == "different-opinion")
                {
                    var subjectId = (string)subject["EID"]!;
                    var action = await db.Set<Dictionary<string, object?>>("DataScopeObjectAction").SingleAsync(row => EF.Property<string>(row, "EntryId") == subjectId, token);
                    action["Opinion"] = "deny";
                }
                else subject["ExpiresAt"] = subject["ExpiresAt"] switch
                {
                    DateTimeOffset deadline => (object)deadline.AddDays(1), DateTime deadline => (object)deadline.AddDays(1),
                    _ => throw new InvalidOperationException("Expiry fixture must retain its actual persisted type.")
                };
                await db.SaveChangesAsync(token);
            }
            state.Capturing = true;
            try
            {
                if (state.Mode == "missing") return await inner.InitializeFromParentsAsync(resource, parents, target, null!, token);
                var first = graph.Parents[0];
                source = state.Mode switch
                {
                    "parent-id" => graph with { Parents = [first with { ObjectId = "foreign-parent" }, .. graph.Parents.Skip(1)] },
                    "parent-values" => graph with { Parents = [first with { Values = new Dictionary<string, object?> { ["LID"] = first.ObjectId } }, .. graph.Parents.Skip(1)] },
                    "parent-resource" => graph with { Parents = [first with { ResourceKey = "part.part" }, .. graph.Parents.Skip(1)] },
                    "target" => graph with { Target = graph.Target with { ObjectId = "foreign-child" } },
                    "unknown-proof" => graph with { Proof = new UnknownCreationProof() },
                    "ordinary-shape" => new DataScopeObjectInitializationSource.Created(graph.ParentObjectIds, graph.Target),
                    _ => source
                };
                return await inner.InitializeFromParentsAsync(resource, parents, target, source, token);
            }
            finally { state.Capturing = false; }
        }
    }
}
