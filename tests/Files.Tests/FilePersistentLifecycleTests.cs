using System.Data.Common;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modules.Files;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Modules.Files.Services;
using ICP.Platform.Modules.Files.Storage;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Tests.Files;

/// <summary>持久状态最低层故障证明；真实 Document policy/HTTP/SQL/进程证明由纵向资产承担。</summary>
public sealed class FilePersistentLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledUploadRetainsConflictAfterCleanup_AndRepeatedCancelIsIdempotent(bool cleanup)
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        var staged = await fixture.StageAsync([1, 2, 3]);
        using var scope = fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFileLifecycleService>();
        await service.CancelStageAsync(staged.UploadId);
        if (cleanup) await fixture.CleanupAsync();
        await service.CancelStageAsync(staged.UploadId);
        await AssertUploadFailureAsync(() => service.ReadOwnedStagedAsync([staged.UploadId], OwnerPolicy.Key), 409, "Files.UploadCancelled");
        await AssertUploadFailureAsync(() => fixture.ConfirmAsync(staged.UploadId, new(OwnerPolicy.Key, "original-owner")), 409, "Files.UploadCancelled");
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task MixedBatchHidesOwnExpiredOrConfirmedStateUntilEveryActorAndPurposeMatches(bool confirmed, bool wrongPurpose, bool reverse)
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        var own = await fixture.StageAsync([1]);
        var hidden = await fixture.StageAsync([2]);
        if (confirmed) await fixture.ConfirmAsync(own.UploadId, new(OwnerPolicy.Key, "original-owner"));
        else fixture.Clock.Advance(TimeSpan.FromHours(24));
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var hiddenLease = await db.Set<Dictionary<string, object?>>("FileUploadLease").SingleAsync(row => EF.Property<string>(row, "EID") == hidden.UploadId);
        hiddenLease[wrongPurpose ? "UsageOwnerKey" : "ActorId"] = "another-identity";
        await db.SaveChangesAsync();
        var uploads = reverse ? new[] { hidden.UploadId, own.UploadId } : new[] { own.UploadId, hidden.UploadId };
        var service = scope.ServiceProvider.GetRequiredService<IFileLifecycleService>();
        await AssertUploadFailureAsync(() => service.ReadOwnedStagedAsync(uploads, OwnerPolicy.Key), 404, "Files.UploadUnavailable");
    }

    [Fact]
    public async Task ForeignActorCannotObserveInvalidReadyMetadata_ButOwnedCorruptionStillFailsClearly()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        var staged = await fixture.StageAsync([3]);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var lease = await db.Set<Dictionary<string, object?>>("FileUploadLease").SingleAsync();
        lease["ActorId"] = "another-actor";
        var file = await db.Set<Dictionary<string, object?>>("FileRecord").SingleAsync();
        file["Length"] = null;
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IFileLifecycleService>();
        var owner = new FileOwnerReference(OwnerPolicy.Key, "original-owner");
        await AssertUploadFailureAsync(() => service.ReadOwnedStagedAsync([staged.UploadId], OwnerPolicy.Key), 404, "Files.UploadUnavailable");
        await AssertUploadFailureAsync(() => fixture.ConfirmAsync(staged.UploadId, owner), 404, "Files.UploadUnavailable");
        await AssertUploadFailureAsync(() => service.CancelStageAsync(staged.UploadId), 404, "Files.UploadUnavailable");
        lease["ActorId"] = "actor";
        await db.SaveChangesAsync();
        Assert.Equal("Files.PersistentAssociationCorrupted", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReadOwnedStagedAsync([staged.UploadId], OwnerPolicy.Key))).Message);
        Assert.Equal("Files.PersistentAssociationCorrupted", (await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ConfirmAsync(staged.UploadId, owner))).Message);
    }

    private static async Task AssertUploadFailureAsync(Func<Task> action, int status, string code)
    {
        var exception = await Assert.ThrowsAsync<FileLifecycleException>(action);
        Assert.Equal(status, exception.StatusCode);
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public async Task ActualAddedDocumentFileLinkWithLid_DoesNotEnterFilesSharedTypeLocalRowsOrFlush()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(includeDocumentModels: true);
        var staged = await fixture.StageAsync([1, 2, 3]);
        var owner = new FileOwnerReference(OwnerPolicy.Key, "DocumentFileLink_00000000000000000000000000000001");
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IFileLifecycleService>();
        var tx = scope.ServiceProvider.GetRequiredService<IPlatformTransactionExecutor>();
        var savedBefore = fixture.Faults.Saves;
        await Assert.ThrowsAsync<InjectedFailure>(() => tx.ExecuteAsync<bool>(async token =>
        {
            // 来自实际 Document authoring 的 Link；它只有 LID，FileUsageId 在 Confirm 后才填齐。
            db.Set<Dictionary<string, object?>>("DocumentFileLink").Add(new()
            {
                ["LID"] = owner.Id, ["PEID"] = "DocumentIteration_00000000000000000000000000000001", ["CEID"] = staged.FileId,
                ["Role"] = "main", ["Ordinal"] = 0L, ["FileUsageId"] = null, ["LCreatedBy"] = "actor", ["LCreatedAt"] = fixture.Clock.GetUtcNow().UtcDateTime,
                ["LUpdatedBy"] = "actor", ["LUpdatedAt"] = fixture.Clock.GetUtcNow().UtcDateTime
            });
            var usage = await service.ConfirmUsageAsync(staged.UploadId, owner, token);
            var repeated = await service.ConfirmUsageAsync(staged.UploadId, owner, token);
            Assert.Equal(usage, repeated);
            Assert.Equal(savedBefore, fixture.Faults.Saves);
            Assert.Single(db.ChangeTracker.Entries(), entry => entry.Metadata.Name == "FileUsage");
            Assert.Single(db.ChangeTracker.Entries(), entry => entry.Metadata.Name == "DocumentFileLink");
            throw new InjectedFailure();
        }).AsTask());
    }
    [Theory]
    [InlineData(1, false, false)]
    [InlineData(1, true, false)]
    [InlineData(2, false, false)]
    [InlineData(2, true, true)]
    public async Task IntentAndReadyCommitFailures_RereadDurableStateWithoutPublishingSuccess(int failAt, bool afterCommit, bool staged)
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        fixture.Faults.FailAt = failAt;
        fixture.Faults.AfterCommit = afterCommit;
        using var scope = fixture.Services.CreateScope();
        var files = scope.ServiceProvider.GetRequiredService<IFileLifecycleService>();
        await Assert.ThrowsAnyAsync<Exception>(() => files.StageAsync(new MemoryStream([1, 2, 3]), "proof.pdf", "application/pdf", OwnerPolicy.Key));
        using var observation = fixture.Services.CreateScope();
        var db = observation.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var records = await db.Set<Dictionary<string, object?>>("FileRecord").AsNoTracking().ToListAsync();
        if (failAt == 1 && !afterCommit)
        {
            Assert.Empty(records);
            Assert.False(Directory.Exists(fixture.StorageRoot) && Directory.EnumerateFiles(fixture.StorageRoot).Any());
            return;
        }
        var record = Assert.Single(records);
        var lease = await db.Set<Dictionary<string, object?>>("FileUploadLease").AsNoTracking().SingleAsync();
        Assert.Equal(staged ? "ready" : "cleanupPending", record["State"]);
        Assert.Equal(staged ? "staged" : "cleanupPending", lease["State"]);
        if (failAt == 1) Assert.Empty(Directory.EnumerateFiles(fixture.StorageRoot));
        if (staged) Assert.True(File.Exists(Path.Combine(fixture.StorageRoot, (string)record["StorageKey"]!)));
        fixture.Clock.Advance(TimeSpan.FromHours(25));
        await fixture.CleanupAsync();
        Assert.Empty(Directory.EnumerateFiles(fixture.StorageRoot));
    }

    [Fact]
    public async Task ConfirmOnlyRegisters_OuterRollbackLeavesStagedReusable_AndCommittedSameTargetIsIdempotent()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        var staged = await fixture.StageAsync([4, 5, 6]);
        var owner = new FileOwnerReference(OwnerPolicy.Key, "OwnerLink_00000000000000000000000000000001");
        using (var scope = fixture.Services.CreateScope())
        {
            var transaction = scope.ServiceProvider.GetRequiredService<IPlatformTransactionExecutor>();
            var files = scope.ServiceProvider.GetRequiredService<IFileLifecycleService>();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var savesBefore = fixture.Faults.Saves;
            await Assert.ThrowsAsync<InjectedFailure>(() => transaction.ExecuteAsync<bool>(async token =>
            {
                var usage = await files.ConfirmUsageAsync(staged.UploadId, owner, token);
                Assert.Equal(savesBefore, fixture.Faults.Saves);
                Assert.Equal(EntityState.Added, db.ChangeTracker.Entries().Single(entry => entry.Metadata.Name == "FileUsage").State);
                Assert.NotEmpty(usage.UsageId);
                throw new InjectedFailure();
            }).AsTask());
        }
        using (var observed = fixture.Services.CreateScope())
        {
            var db = observed.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.Empty(await db.Set<Dictionary<string, object?>>("FileUsage").AsNoTracking().ToListAsync());
            Assert.Equal("staged", (await db.Set<Dictionary<string, object?>>("FileUploadLease").AsNoTracking().SingleAsync())["State"]);
        }
        var confirmed = await fixture.ConfirmAsync(staged.UploadId, owner);
        var repeated = await fixture.ConfirmAsync(staged.UploadId, owner);
        Assert.Equal(confirmed, repeated);
        using var other = fixture.Services.CreateScope();
        var tx = other.ServiceProvider.GetRequiredService<IPlatformTransactionExecutor>();
        var service = other.ServiceProvider.GetRequiredService<IFileLifecycleService>();
        var changedOwner = owner with { Id = "OwnerLink_00000000000000000000000000000002" };
        await AssertUploadFailureAsync(() => tx.ExecuteAsync(token =>
            new ValueTask<ConfirmedFileUsage>(service.ConfirmUsageAsync(staged.UploadId, changedOwner, token))).AsTask(), 409, "Files.UploadAlreadyConfirmed");
    }

    [Fact]
    public async Task LastReleaseMarksPendingInBusinessTransaction_RollbackPreservesUsage_ConfirmedTtlDoesNotDelayCleanup()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        var staged = await fixture.StageAsync([7, 8, 9]);
        var owner = new FileOwnerReference(OwnerPolicy.Key, "OwnerLink_00000000000000000000000000000001");
        var usage = await fixture.ConfirmAsync(staged.UploadId, owner);
        var reference = new FileUsageReference(usage.UsageId, usage.FileId, owner);
        await fixture.CleanupAsync();
        Assert.True(File.Exists(await fixture.ContentPathAsync(staged.FileId)));
        using (var scope = fixture.Services.CreateScope())
        {
            var tx = scope.ServiceProvider.GetRequiredService<IPlatformTransactionExecutor>();
            var files = scope.ServiceProvider.GetRequiredService<IFileLifecycleService>();
            await Assert.ThrowsAsync<InjectedFailure>(() => tx.ExecuteAsync<bool>(async token =>
            { await files.ReleaseUsageAsync(reference, token); throw new InjectedFailure(); }).AsTask());
        }
        await fixture.CleanupAsync();
        Assert.True(File.Exists(await fixture.ContentPathAsync(staged.FileId)));
        using (var scope = fixture.Services.CreateScope())
        {
            var tx = scope.ServiceProvider.GetRequiredService<IPlatformTransactionExecutor>();
            var files = scope.ServiceProvider.GetRequiredService<IFileLifecycleService>();
            await tx.ExecuteAsync(async token => { await files.ReleaseUsageAsync(reference, token); return true; });
        }
        using (var observed = fixture.Services.CreateScope())
        {
            var db = observed.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            Assert.Equal("cleanupPending", (await db.Set<Dictionary<string, object?>>("FileRecord").AsNoTracking().SingleAsync())["State"]);
            Assert.Equal("confirmed", (await db.Set<Dictionary<string, object?>>("FileUploadLease").AsNoTracking().SingleAsync())["State"]);
        }
        await fixture.CleanupAsync();
        await fixture.CleanupAsync();
        Assert.False(File.Exists(await fixture.ContentPathAsync(staged.FileId)));
        await AssertUploadFailureAsync(() => fixture.ConfirmAsync(staged.UploadId, owner), 409, "Files.UploadAlreadyConfirmed");
    }

    [Fact]
    public async Task MetadataChecksBidirectionalFacts_OpenMissingContentFails_AndCancelledDownloadReleasesHandle()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        var staged = await fixture.StageAsync([1, 5, 9]);
        var owner = new FileOwnerReference(OwnerPolicy.Key, "OwnerLink_00000000000000000000000000000001");
        var usage = await fixture.ConfirmAsync(staged.UploadId, owner);
        var reference = new FileUsageReference(usage.UsageId, usage.FileId, owner);
        using var scope = fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IFileLifecycleService>();
        var metadata = Assert.Single(await service.ReadUsageMetadataAsync([reference]));
        Assert.Equal(3L, metadata.Length);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReadUsageMetadataAsync([reference with { FileId = "FileRecord_wrong" }]));
        using var cancellation = new CancellationTokenSource();
        var opened = await service.OpenUsageAsync(reference, cancellation.Token);
        try
        {
            cancellation.Cancel();
            Assert.False(opened.FileStream.CanRead);
            // 删除必须先于显式 Dispose，证明取消已经释放真实 Local 句柄。
            File.Delete(await fixture.ContentPathAsync(staged.FileId));
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenUsageAsync(reference));
            Assert.Equal("Files.ContentMissing", failure.Message);
        }
        finally { await opened.FileStream.DisposeAsync(); }
    }

    [Fact]
    public async Task CancelDuringWriter_ClosesHandlesBeforeCleanup_AndNoLateReadyOrBlobResurrection()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        using var source = new BarrierStream();
        using var uploadScope = fixture.Services.CreateScope();
        var files = uploadScope.ServiceProvider.GetRequiredService<IFileLifecycleService>();
        var upload = files.StageAsync(source, "proof.pdf", "application/pdf", OwnerPolicy.Key);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        string uploadId;
        using (var observation = fixture.Services.CreateScope())
        {
            var db = observation.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var intent = await db.Set<Dictionary<string, object?>>("FileUploadLease").AsNoTracking().SingleAsync();
            uploadId = (string)intent["EID"]!;
            Assert.Equal("receiving", intent["State"]);
        }
        using var cancelScope = fixture.Services.CreateScope();
        await cancelScope.ServiceProvider.GetRequiredService<IFileLifecycleService>().CancelStageAsync(uploadId);
        await Assert.ThrowsAnyAsync<Exception>(() => upload);
        source.Release.TrySetResult();
        await fixture.CleanupAsync();
        Assert.Empty(Directory.EnumerateFiles(fixture.StorageRoot));
        using var final = fixture.Services.CreateScope();
        var finalDb = final.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        Assert.Equal("deleted", (await finalDb.Set<Dictionary<string, object?>>("FileRecord").AsNoTracking().SingleAsync())["State"]);
        Assert.Equal("cleaned", (await finalDb.Set<Dictionary<string, object?>>("FileUploadLease").AsNoTracking().SingleAsync())["State"]);
    }

    [Fact]
    public async Task StagedExpiryEqualsNowIsUnavailable_AndBoundedCleanupRestoresNoWriterReceiving()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        var staged = await fixture.StageAsync([1]);
        fixture.Clock.Advance(TimeSpan.FromHours(24));
        using (var scope = fixture.Services.CreateScope())
            await AssertUploadFailureAsync(() => scope.ServiceProvider.GetRequiredService<IFileLifecycleService>()
                .ReadOwnedStagedAsync([staged.UploadId], OwnerPolicy.Key), 409, "Files.UploadExpired");
        await AssertUploadFailureAsync(() => fixture.ConfirmAsync(staged.UploadId, new(OwnerPolicy.Key, "original-owner")), 409, "Files.UploadExpired");
        await fixture.CleanupAsync();
        Assert.Empty(Directory.EnumerateFiles(fixture.StorageRoot));
        using (var scope = fixture.Services.CreateScope())
        {
            var ids = new PlatformObjectIdGenerator();
            await scope.ServiceProvider.GetRequiredService<FileStateStore>().CreateIntentAsync(new(ids.NewId("FileUploadLease"), ids.NewId("FileRecord"),
                Guid.NewGuid().ToString("N"), "actor", OwnerPolicy.Key, "orphan.pdf", "application/pdf", fixture.Clock.GetUtcNow().UtcDateTime.AddHours(24)), default);
        }
        await fixture.CleanupAsync();
        using var observed = fixture.Services.CreateScope();
        Assert.All(await observed.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("FileRecord")
            .AsNoTracking().ToListAsync(), record => Assert.Equal("deleted", record["State"]));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CancelOrExpiryBeforeOpenOrRename_WriterExitsAndNeverPublishesReady(bool beforeRename, bool expire)
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        fixture.ContentFaults.BarrierPoint = beforeRename ? "rename" : "open";
        using var scope = fixture.Services.CreateScope();
        var upload = scope.ServiceProvider.GetRequiredService<IFileLifecycleService>()
            .StageAsync(new MemoryStream([1, 2, 3]), "proof.pdf", "application/pdf", OwnerPolicy.Key);
        await fixture.ContentFaults.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        string uploadId;
        using (var observed = fixture.Services.CreateScope())
        {
            var db = observed.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            uploadId = (string)(await db.Set<Dictionary<string, object?>>("FileUploadLease").AsNoTracking().SingleAsync())["EID"]!;
        }
        if (expire)
        {
            fixture.Clock.Advance(TimeSpan.FromHours(24));
            await fixture.CleanupAsync();
        }
        else
        {
            using var cancelScope = fixture.Services.CreateScope();
            await cancelScope.ServiceProvider.GetRequiredService<IFileLifecycleService>().CancelStageAsync(uploadId);
            await fixture.CleanupAsync();
        }
        await Assert.ThrowsAnyAsync<Exception>(() => upload);
        fixture.ContentFaults.Resume.TrySetResult();
        await fixture.CleanupAsync();
        Assert.Empty(Directory.EnumerateFiles(fixture.StorageRoot));
        using var final = fixture.Services.CreateScope();
        Assert.Equal("deleted", (await final.ServiceProvider.GetRequiredService<EfPlatformDbContext>()
            .Set<Dictionary<string, object?>>("FileRecord").AsNoTracking().SingleAsync())["State"]);
    }

    [Fact]
    public async Task CleanupIoFailurePreservesPendingAndRetriesOnlyRegisteredKey()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        var staged = await fixture.StageAsync([1, 2, 3]);
        var unknown = Path.Combine(fixture.StorageRoot, "unregistered-owned.bin");
        await File.WriteAllBytesAsync(unknown, [9]);
        using (var scope = fixture.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IFileLifecycleService>().CancelStageAsync(staged.UploadId);
        fixture.ContentFaults.DeleteFailures = 1;
        await fixture.CleanupAsync();
        using (var observed = fixture.Services.CreateScope())
            Assert.Equal("cleanupPending", (await observed.ServiceProvider.GetRequiredService<EfPlatformDbContext>()
                .Set<Dictionary<string, object?>>("FileRecord").AsNoTracking().SingleAsync())["State"]);
        Assert.True(File.Exists(await fixture.ContentPathAsync(staged.FileId)));
        await fixture.CleanupAsync();
        Assert.False(File.Exists(await fixture.ContentPathAsync(staged.FileId)));
        Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(unknown));
    }

    private sealed class LifecycleFixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "NewP-Files-State", Guid.NewGuid().ToString("N"));
        public ServiceProvider Services { get; private set; } = null!;
        public MutableClock Clock { get; } = new();
        public FaultInterceptor Faults { get; } = new();
        public ContentFaultStore ContentFaults { get; private set; } = null!;
        public string StorageRoot => Path.Combine(_root, "private");

        public static async Task<LifecycleFixture> CreateAsync(bool includeDocumentModels = false)
        {
            var fixture = new LifecycleFixture();
            Directory.CreateDirectory(fixture._root);
            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Files:StorageRoot"] = fixture.StorageRoot }).Build();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<TimeProvider>(fixture.Clock);
            services.AddSingleton<IEntityAuditStampProvider>(new StampProvider(fixture.Clock));
            services.AddScoped<IEntityAuditValueProvider, EntityAuditValueProvider>();
            services.AddLogging();
            var filesDefinitions = new FilesModelMetadataProvider().GetDefinitions();
            var definitions = includeDocumentModels ? filesDefinitions.Concat(DocumentDefinitions()).ToArray() : filesDefinitions;
            services.AddScoped(provider => new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(fixture._root, "state.sqlite"))
                .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
                .AddInterceptors(fixture.Faults).Options, definitions, provider.GetRequiredService<IEntityAuditValueProvider>()));
            services.AddScoped<IPlatformTransactionExecutor, EfPlatformTransactionExecutor>();
            services.AddScoped<IFileOwnerPolicy, OwnerPolicy>();
            new FilesModule().ConfigureServices(services);
            services.AddSingleton<IFileContentStore>(provider => fixture.ContentFaults = new(provider.GetRequiredService<LocalStorageProvider>()));
            fixture.Services = services.BuildServiceProvider();
            fixture.ContentFaults = (ContentFaultStore)fixture.Services.GetRequiredService<IFileContentStore>();
            using var scope = fixture.Services.CreateScope();
            // 这里只创建 Files 状态表；额外真实 Link 仅验证 shared tracker，在 Flush 前主动回滚。
            await using var initial = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>()
                .UseSqlite("Data Source=" + Path.Combine(fixture._root, "state.sqlite"))
                .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().AddInterceptors(fixture.Faults).Options,
                filesDefinitions, scope.ServiceProvider.GetRequiredService<IEntityAuditValueProvider>());
            await initial.Database.EnsureCreatedAsync();
            fixture.Faults.Reset();
            return fixture;
        }

        private static IReadOnlyList<ICP.Platform.Modeling.Definitions.ModelDefinition> DocumentDefinitions()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(ModuleAuthoringPaths.ResolveModuleRoot(directory.FullName, "Document"), "metadata/models/DocumentFileLink.model.json")))
                directory = directory.Parent;
            var root = directory?.FullName ?? throw new InvalidOperationException("Document authoring missing.");
            var module = ModuleAuthoringPaths.ResolveModuleRoot(root, "Document");
            var map = new JsonStorageTypeMapLoader().LoadFromFile(Path.Combine(root, "server/platform-modeling/metadata/storage-types.json"));
            return new[] { "Document", "DocumentVersion", "DocumentIteration", "DocumentFileLink" }.Select(name =>
                new ModelMetadataProjector().Project(new JsonModelMetadataDocumentLoader().LoadFromFile(Path.Combine(module, "metadata/models", name + ".model.json")), map, "SqlServer")
                    with { OwnerModuleId = "Document" }).ToArray();
        }

        public async Task<StagedFileInfo> StageAsync(byte[] bytes)
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IFileLifecycleService>().StageAsync(new MemoryStream(bytes), "proof.pdf", "application/pdf", OwnerPolicy.Key);
        }
        public async Task<ConfirmedFileUsage> ConfirmAsync(string uploadId, FileOwnerReference owner)
        {
            using var scope = Services.CreateScope();
            var files = scope.ServiceProvider.GetRequiredService<IFileLifecycleService>();
            return await scope.ServiceProvider.GetRequiredService<IPlatformTransactionExecutor>()
                .ExecuteAsync(token => new ValueTask<ConfirmedFileUsage>(files.ConfirmUsageAsync(uploadId, owner, token)));
        }
        public async Task CleanupAsync()
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<FileCleanupService>().RunOnceAsync();
        }
        public async Task<string> ContentPathAsync(string fileId)
        {
            using var scope = Services.CreateScope();
            var record = await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Set<Dictionary<string, object?>>("FileRecord")
                .AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == fileId);
            return Path.Combine(StorageRoot, (string)record["StorageKey"]!);
        }
        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_root, true);
        }
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
    private sealed class StampProvider(MutableClock clock) : IEntityAuditStampProvider
    { public EntityAuditStamp GetCurrent() => new("actor", clock.GetUtcNow().UtcDateTime); }
    private sealed class OwnerPolicy : IFileOwnerPolicy
    {
        public const string Key = "tests.fileState";
        public string OwnerKey => Key;
        public void ValidateUpload(string safeFileName, string contentType) { }
        public Task VerifyAsync(FileOwnerReference owner, string fileId, string? usageId, FileOwnerOperation operation, CancellationToken token) => Task.CompletedTask;
        public Task VerifyBatchAsync(IReadOnlyList<FileUsageReference> references, FileOwnerOperation operation, CancellationToken token) => Task.CompletedTask;
    }
    private sealed class InjectedFailure : Exception { }
    private sealed class ContentFaultStore(LocalStorageProvider inner) : IFileContentStore
    {
        public string? BarrierPoint { get; set; }
        public int DeleteFailures { get; set; }
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<FileContentMeasurement> WritePartialAsync(string key, Stream source, long maximumBytes, CancellationToken token)
        {
            if (BarrierPoint == "open") { Reached.TrySetResult(); await Resume.Task.WaitAsync(token); }
            return await inner.WritePartialAsync(key, source, maximumBytes, token);
        }
        public async Task CompleteAsync(string key, CancellationToken token)
        {
            if (BarrierPoint == "rename") { Reached.TrySetResult(); await Resume.Task.WaitAsync(token); }
            await inner.CompleteAsync(key, token);
        }
        public Task<Stream> OpenAsync(string key, CancellationToken token) => inner.OpenAsync(key, token);
        public Task DeleteContentAsync(string key, CancellationToken token)
        {
            if (DeleteFailures > 0) { DeleteFailures--; throw new IOException("Injected deletion failure."); }
            return inner.DeleteContentAsync(key, token);
        }
    }
    private sealed class FaultInterceptor : DbTransactionInterceptor, ISaveChangesInterceptor
    {
        public int FailAt { get; set; }
        public bool AfterCommit { get; set; }
        public int Saves;
        private int _commits;
        public void Reset() { _commits = 0; Saves = 0; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _commits);
            if (!AfterCommit && _commits == FailAt) throw new InjectedFailure();
            return ValueTask.FromResult(result);
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (AfterCommit && _commits == FailAt) throw new InjectedFailure();
            return Task.CompletedTask;
        }
        ValueTask<InterceptionResult<int>> ISaveChangesInterceptor.SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken)
        { Saves++; return ValueTask.FromResult(result); }
    }
    private sealed class BarrierStream : Stream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); return 0; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
