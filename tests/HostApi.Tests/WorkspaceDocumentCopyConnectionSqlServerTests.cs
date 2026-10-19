using System.Data;
using System.Data.Common;
using ICP.Platform.Modules.Document;
using ICP.Platform.Modules.Document.Contracts;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class WorkspaceClipboardSqlServerHttpApiTests
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "同一owner只读单元只打开一次自有连接，正常/失败/取消均归还，调用者已打开的连接保持不变")]
    public async Task ObjectCopy_DocumentCopyReadUnitsOwnOnlyTheirConnection()
    {
        var connections = new CopyReadConnections();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(connections)), includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync(); var root = await Root(fixture, admin);
        var folder = await Folder(fixture, admin, root, "连接所有权源"); await CreateBatchDocument(fixture, admin, folder, 1);
        var id = (string)Assert.Single(await Rows(fixture, "Document"))["EID"]!;
        var file = (await Rows(fixture, "DocumentFileLink")).OrderBy(row => (long)row["Ordinal"]!).First();
        var reference = new FileUsageReference((string)file["FileUsageId"]!, (string)file["CEID"]!, new(DocumentFileOwnerPolicy.Key, (string)file["LID"]!));
        await AsCopyActor(fixture, async provider => {
            var db = provider.GetRequiredService<EfPlatformDbContext>(); var copies = provider.GetRequiredService<IDocumentCopies>();
            Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
            connections.Begin(); await copies.ReadCopySourceAsync(id); connections.Active = false;
            Assert.Equal(1, connections.Opens); Assert.Equal(1, connections.Closes); Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
            await db.Database.OpenConnectionAsync();
            try
            {
                connections.Begin(); await copies.ReadCopySourceAsync(id); connections.Active = false;
                Assert.Equal(0, connections.Opens); Assert.Equal(0, connections.Closes); Assert.Equal(ConnectionState.Open, db.Database.GetDbConnection().State);
            }
            finally { await db.Database.CloseConnectionAsync(); }
            connections.Begin(); await Assert.ThrowsAnyAsync<Exception>(() => copies.ReadCopySourceAsync("missing-document")); connections.Active = false;
            Assert.Equal(1, connections.Opens); Assert.Equal(1, connections.Closes); Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            connections.Begin(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => copies.ReadCopySourceAsync(id, cancelled.Token)); connections.Active = false;
            Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
            var lifecycle = provider.GetRequiredService<IFileLifecycleService>();
            connections.Begin(); await lifecycle.ReadUsageMetadataAsync([reference], new FileOwnerLegacyCopyReadContext(typeof(DocumentFiles), nameof(DocumentFiles.ReadCopySourceAsync))); connections.Active = false;
            Assert.Equal(1, connections.Opens); Assert.Equal(1, connections.Closes); Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
            var files = provider.GetRequiredService<IFileCopyService>(); await files.PrepareCopyAsync("owned-read-unit", reference, DocumentFileOwnerPolicy.Key);
            connections.Begin(); var ready = await files.ReadCopyAsync("owned-read-unit", DocumentFileOwnerPolicy.Key); connections.Active = false;
            Assert.Equal("prepared", ready!.State); Assert.Equal(1, connections.Opens); Assert.Equal(1, connections.Closes);
            await files.CancelCopyAsync("owned-read-unit", DocumentFileOwnerPolicy.Key); return true;
        });
    }
    private sealed class CopyReadConnections : DbConnectionInterceptor
    {
        internal bool Active; internal int Opens; internal int Closes;
        internal void Begin() { Opens = Closes = 0; Active = true; }
        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData data, CancellationToken token = default) { if (Active) Opens++; return Task.CompletedTask; }
        public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData data) { if (Active) Closes++; return Task.CompletedTask; }
    }
}