using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.HostApi.Persistence;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Xunit.Abstractions;
using static ICP.Platform.HostApi.Tests.EntityOwnershipBackfillSqlServerHttpApiTests;
using static ICP.Platform.HostApi.Tests.EntityOwnershipTransferSqlServerHttpApiTests;

namespace ICP.Platform.HostApi.Tests;

public sealed class EntityOwnershipTransferTransactionSqlServerHttpApiTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "三模型转交在通知SQL已执行后失败/取消全回滚，真实递增stamp核归属/引用/记录/通知同scope")]
    public async Task ThreeModels_NotificationWriteFailureAndCancellationRollBackAllFacts_AndSuccessUsesOneTrustedStamp()
    {
        var writes = new NotificationWriteProbe();
        var ticks = new AuditTicks();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(writes));
            services.Replace(ServiceDescriptor.Scoped<IEntityAuditStampProvider>(provider => new AdvancingStamp(
                new HttpContextEntityAuditStampProvider(provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
                    provider.GetRequiredService<TimeProvider>()), ticks)));
        }, includeWorkspace: true);
        using var admin = await fixture.LoginAdministratorAsync();
        await InitializeAsync(fixture, admin);
        var position = await ContainerAsync(fixture, admin);
        var recipient = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "LateTransferRecipient", "接收人",
            "late-transfer-recipient", "后段回滚接收人", Point(fixture, "workspace.spaces.access"));
        using var recipientClient = recipient.Client;
        var targets = new List<Target>();
        foreach (var (model, kind, action) in Kinds)
            targets.Add(await CreateAsync(fixture, admin, position, model, kind, action, "LATE-TRANSFER-" + model));
        var models = new[] { "WorkspaceSpace", "WorkspaceFolder", "WorkspaceFolderContent", "Part", "Document", "BusinessChange", "WorkspaceTransferNotification" };
        var before = await SnapshotAsync(fixture, models);
        var transfer = Point(fixture, "workspace.folder.transfer");
        var input = new { position, linkIds = targets.Select(target => target.LinkId).ToArray(), recipientId = recipient.AccountId };

        foreach (var mode in new[] { "write", "cancel" })
        {
            using var cancellation = new CancellationTokenSource();
            writes.Arm(mode, cancellation);
            var elapsed = Stopwatch.StartNew();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspace/ownership/transfer") { Content = JsonContent.Create(input) };
                request.Headers.Add("X-NewP-Feature", transfer.Position);
                request.Headers.Add("X-NewP-Data-Declaration", OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer).Id);
                if (mode == "cancel")
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => admin.SendAsync(request, cancellation.Token));
                else
                {
                    var failure = await Record.ExceptionAsync(async () =>
                    {
                        using var response = await admin.SendAsync(request, cancellation.Token);
                        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                    });
                    if (failure is not null)
                    {
                        var injected = Assert.IsType<InvalidOperationException>(failure.GetBaseException());
                        Assert.Equal("injected after notification SQL completed", injected.Message);
                    }
                }
                Assert.True(writes.NotificationSqlCompleted);
            }
            finally { writes.Disarm(); }
            Assert.Equal(before, await SnapshotAsync(fixture, models));
            output.WriteLine($"transfer rollback mode={mode} models=3 notificationSqlCompleted={writes.NotificationSqlCompleted} elapsedMs={elapsed.Elapsed.TotalMilliseconds:F2}");
        }

        using (var succeeded = await FeatureHttpTestRequests.SendAsync(admin, transfer, HttpMethod.Post, "/api/workspace/ownership/transfer", input, OwnershipDeclaration(fixture, "workspace.folder.transfer", transfer)))
            Assert.True((await ReadOkAsync(succeeded)).GetProperty("succeeded").GetBoolean());
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var events = await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
            .Where(row => EF.Property<string>(row, "EventType") == "entity.ownership.transferred").ToArrayAsync();
        Assert.Equal(3, events.Length);
        var stamp = (DateTime)events[0]["OccurredAt"]!;
        Assert.All(events, row => Assert.Equal(stamp, row["OccurredAt"]));
        foreach (var target in targets)
        {
            var row = await RowAsync(fixture, target);
            Assert.Equal(stamp, row["UpdatedAt"]);
            Assert.Equal(fixture.ActorId, row["UpdatedBy"]);
        }
        var notifications = await db.Set<Dictionary<string, object?>>("WorkspaceTransferNotification").AsNoTracking().ToArrayAsync();
        Assert.Equal(3, notifications.Length);
        Assert.All(notifications, row => Assert.Equal(stamp, row["CreatedAt"]));
        var directoryId = Assert.Single(notifications.Select(row => (string)row["DirectoryId"]!).Distinct(StringComparer.Ordinal));
        var links = await db.Set<Dictionary<string, object?>>("WorkspaceFolderContent").AsNoTracking()
            .Where(row => EF.Property<string>(row, "PEID") == directoryId).ToArrayAsync();
        Assert.Equal(3, links.Length);
        Assert.All(links, row => Assert.Equal(stamp, row["LCreatedAt"]));
        var directory = await db.Set<Dictionary<string, object?>>("WorkspaceFolder").AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "EID") == directoryId);
        Assert.Equal(stamp, directory["CreatedAt"]);
        var receivedIds = links.Select(row => (string)row["LID"]!).ToArray();
        var referenceEvents = await db.Set<Dictionary<string, object?>>("BusinessChange").AsNoTracking()
            .Where(row => EF.Property<string>(row, "EventType") == "workspace.reference.added"
                && receivedIds.Contains(EF.Property<string>(row, "TargetId"))).ToArrayAsync();
        Assert.Equal(3, referenceEvents.Length);
        Assert.All(referenceEvents, row => Assert.Equal(stamp, row["OccurredAt"]));
        output.WriteLine($"transfer success models=3 notifications=3 receivingLinks=3 advancingStampReads={ticks.Count} sharedStamp={stamp:O}");
    }

    private sealed class AuditTicks { internal long Count; }
    private sealed class AdvancingStamp(HttpContextEntityAuditStampProvider inner, AuditTicks ticks) : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent()
        {
            var current = inner.GetCurrent();
            return new(current.ActorId, current.OccurredAtUtc.AddMilliseconds(Interlocked.Increment(ref ticks.Count)));
        }
    }

    // SavedChanges 已消费完整SQL批次；同事务反查本次通知确已入库后才注入，不能把首个结果流当整批完成。
    internal sealed class NotificationWriteProbe : SaveChangesInterceptor
    {
        private string _mode = "";
        private CancellationTokenSource? _request;
        private string[] _pending = [];
        internal bool NotificationSqlCompleted { get; private set; }
        internal int SavedNotifications { get; private set; }
        internal void Arm(string mode, CancellationTokenSource request)
        {
            _mode = mode;
            _request = request;
            NotificationSqlCompleted = false; SavedNotifications = 0;
        }
        internal void Disarm() { _mode = ""; _request = null; _pending = []; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _pending = _mode.Length == 0 ? [] : eventData.Context!.ChangeTracker.Entries<Dictionary<string, object?>>()
                .Where(entry => entry.Metadata.Name == "WorkspaceTransferNotification" && entry.State == EntityState.Added)
                .Select(entry => (string)entry.Entity["EID"]!).ToArray();
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (_mode.Length == 0 || _pending.Length == 0) return result;
            var count = await eventData.Context!.Set<Dictionary<string, object?>>("WorkspaceTransferNotification").AsNoTracking()
                .CountAsync(row => _pending.Contains(EF.Property<string>(row, "EID")), cancellationToken);
            if (count != _pending.Length) throw new InvalidOperationException("notification write probe did not observe the actual saved rows");
            NotificationSqlCompleted = true; SavedNotifications = count;
            if (_mode == "cancel")
            {
                _request!.Cancel();
                throw new OperationCanceledException(_request.Token);
            }
            throw new InvalidOperationException("injected after notification SQL completed");
        }
    }
}
