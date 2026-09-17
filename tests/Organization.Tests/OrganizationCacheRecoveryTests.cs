using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class OrganizationCacheRecoveryTests
{
    [Fact]
    public async Task UnavailableRecovery_DoesNotQueueNewBusinessTransaction()
    {
        await using var fixture = new Fixture();
        var cache = new ControlledCache(fixture.Services.GetRequiredService<IServiceScopeFactory>());
        await cache.InitializeAsync();
        await using (var commit = await cache.BeginAsync(default))
        {
            await commit.PrepareAsync([], default);
            commit.MarkUnavailable();
        }
        cache.PauseReload = true;
        var recovery = cache.InitializeAsync().AsTask();
        await cache.Entered.Task;
        var transaction = cache.BeginAsync(default).AsTask();
        try
        {
            Assert.True(transaction.IsCompleted, "Known unavailable cache must reject writes instead of queuing behind recovery.");
            await Assert.ThrowsAsync<PlatformCacheUnavailableException>(() => transaction);
        }
        finally
        {
            cache.Release.TrySetResult();
            await recovery;
            if (transaction.IsCompletedSuccessfully) await (await transaction).DisposeAsync();
        }
    }
    [Fact]
    public async Task DuplicateInitialization_MergesAndCanceledWaiterDoesNotCancelOwner()
    {
        await using var fixture = new Fixture();
        var cache = new ControlledCache(fixture.Services.GetRequiredService<IServiceScopeFactory>()) { PauseLoad = true };
        var first = cache.InitializeAsync().AsTask();
        await cache.Entered.Task;
        using var cancellation = new CancellationTokenSource();
        var canceled = cache.InitializeAsync(cancellation.Token).AsTask();
        var second = cache.InitializeAsync().AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.False(first.IsCompleted);
        cache.Release.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, cache.Loads);
        Assert.True(cache.IsAvailable);
        Assert.NotNull(cache.LastMaintenanceDuration);
    }

    [Fact]
    public async Task CanceledRefresh_ReleasesCoordinatorAndRecoveryKeepsKnownRange()
    {
        await using var fixture = new Fixture();
        var cache = new ControlledCache(fixture.Services.GetRequiredService<IServiceScopeFactory>());
        await cache.InitializeAsync();
        await fixture.SaveAsync(Row("company"));
        cache.PauseReload = true;
        using var cancellation = new CancellationTokenSource();
        var refresh = cache.RefreshAsync([new("Organization/Company", "company")], cancellation.Token).AsTask();
        await cache.Entered.Task;
        var read = cache.ReadAsync().AsTask();
        Assert.False(read.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        await Assert.ThrowsAsync<PlatformCacheUnavailableException>(() => read);
        cache.PauseReload = false;
        cache.FailReload = true;
        await Assert.ThrowsAsync<IOException>(() => cache.InitializeAsync().AsTask());
        Assert.Equal(1, cache.Loads); // 读取故障不升级为全量。
        cache.FailReload = false;
        await cache.InitializeAsync();
        Assert.Equal(1, cache.Loads);
        Assert.Single((await cache.ReadAsync()).Companies);
        await using var next = await cache.BeginAsync(default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshAndRecovery_CannotOverwriteLaterTransactionPublication(bool recovering)
    {
        await using var fixture = new Fixture();
        var cache = new ControlledCache(fixture.Services.GetRequiredService<IServiceScopeFactory>());
        await cache.InitializeAsync();
        await fixture.SaveAsync(Row("company"));
        cache.PauseReload = true;
        if (recovering)
        {
            await using var commit = await cache.BeginAsync(default);
            await commit.PrepareAsync([new("Organization/Company", "company", null, Row("company"))], default);
            commit.MarkUnavailable();
        }
        var refresh = recovering ? cache.InitializeAsync().AsTask()
            : cache.RefreshAsync([new("Organization/Company", "company")]).AsTask();
        await cache.Entered.Task;
        if (recovering) await Assert.ThrowsAsync<PlatformCacheUnavailableException>(() => cache.BeginAsync(default).AsTask());
        var writer = recovering ? null : cache.BeginAsync(default).AsTask();
        var duplicateRecovery = cache.InitializeAsync().AsTask();
        if (writer is not null) Assert.False(writer.IsCompleted);
        cache.Release.TrySetResult();
        await refresh;
        await using (var commit = writer is null ? await cache.BeginAsync(default) : await writer)
        {
            var changed = Row("company"); changed["ENAME"] = "later";
            await commit.PrepareAsync([new("Organization/Company", "company", null, changed)], default);
            commit.BlockReads();
            var reader = cache.ReadAsync().AsTask();
            Assert.False(reader.IsCompleted);
            await commit.PublishAsync();
        }
        await duplicateRecovery;
        Assert.Equal("later", (await cache.ReadAsync()).Companies["company"]["ENAME"]);
        Assert.Equal(1, cache.Loads);
    }

    [Fact]
    public async Task InvalidLocalCandidate_RebuildsOnlyWhenKnownRangeCannotBeRepaired()
    {
        await using var fixture = new Fixture();
        var cache = new ControlledCache(fixture.Services.GetRequiredService<IServiceScopeFactory>());
        await cache.InitializeAsync();
        await fixture.SaveAsync(Row("company"));
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            var department = Row("department"); department["CompanyId"] = "company";
            db.Set<Dictionary<string, object?>>("Organization/Department").Add(department);
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.RefreshAsync([new("Organization/Department", "department")]).AsTask());
        Assert.False(cache.IsAvailable);
        Assert.Equal(1, cache.Loads);
        await cache.InitializeAsync();
        Assert.Equal(2, cache.Loads);
        Assert.Single((await cache.ReadAsync()).Departments);
        Assert.Single((await cache.ReadAsync()).Companies);
    }

    [Fact]
    public async Task StoppingMaintenance_CancelsWaitingLoadAndReleasesCoordination()
    {
        await using var fixture = new Fixture();
        var cache = new ControlledCache(fixture.Services.GetRequiredService<IServiceScopeFactory>()) { PauseLoad = true };
        using var stop = new CancellationTokenSource();
        var maintenance = cache.MaintainAsync(_ => Assert.Fail("Cancellation is not a cache failure."), stop.Token);
        await cache.Entered.Task;
        stop.Cancel();
        await maintenance;
        cache.PauseLoad = false;
        await cache.InitializeAsync();
        Assert.True(cache.IsAvailable);
    }

    [Fact]
    public async Task IndependentRefresh_ReadsPersistedRangeWhileTransactionPreparationDoesNotPublish()
    {
        await using var fixture = new Fixture();
        var cache = fixture.Cache;
        await cache.InitializeAsync();
        var before = await cache.ReadAsync();
        await using (var commit = await cache.BeginAsync(default))
        {
            await commit.PrepareAsync([new("Organization/Company", "company", null, Row("company"))], default);
            Assert.Same(before, await cache.ReadAsync());
        }
        await fixture.SaveAsync(Row("company"));
        await cache.RefreshAsync([new("Organization/Company", "company")]);
        Assert.Equal("company", (await cache.ReadAsync()).Companies["company"]["ENAME"]);
        Assert.Empty(before.Companies);
        await using var next = await cache.BeginAsync(default);
    }

    private static Dictionary<string, object?> Row(string id) => new()
    { ["EID"] = id, ["ECODE"] = id, ["ENAME"] = id, ["SortOrder"] = "01", ["IsEnabled"] = true };

    private sealed class ControlledCache(IServiceScopeFactory scopes) : OrganizationCache(scopes)
    {
        public int Loads;
        public bool PauseLoad;
        public bool PauseReload;
        public bool FailReload;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async ValueTask<OrganizationSnapshot> LoadAsync(CancellationToken token)
        {
            Loads++;
            if (PauseLoad) { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return await base.LoadAsync(token);
        }
        protected override async ValueTask<IReadOnlyList<PlatformRecordChange>> ReloadAsync(IReadOnlyList<PlatformRecordChange> changes, CancellationToken token)
        {
            if (PauseReload) { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            if (FailReload) throw new IOException("temporary read failure");
            return await base.ReloadAsync(changes, token);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ServiceProvider Services { get; }
        public OrganizationCache Cache { get; }
        public Fixture()
        {
            var options = new DbContextOptionsBuilder<EfPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options;
            var models = OrganizationModelDefinitions.All.Select(model => model with { OwnerModuleId = "Organization" }).ToArray();
            var services = new ServiceCollection();
            services.AddScoped(_ => new EfPlatformDbContext(options, models));
            Services = services.BuildServiceProvider();
            Cache = new OrganizationCache(Services.GetRequiredService<IServiceScopeFactory>());
        }
        public async Task SaveAsync(Dictionary<string, object?> row)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            db.Set<Dictionary<string, object?>>("Organization/Company").Add(row);
            await db.SaveChangesAsync();
        }
        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }
}
