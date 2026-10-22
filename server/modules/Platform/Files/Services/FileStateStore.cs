using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Modules.Files.Storage;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.EfCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Files.Services;

/// <summary>Files 自有持久状态和事务锁；不持有任何文件 I/O 句柄。</summary>
public sealed partial class FileStateStore(EfPlatformDbContext db, IPlatformTransactionExecutor transactions, TimeProvider clock, IEntityAuditValueProvider audit)
{
    public const string RecordModel = "FileRecord";
    public const string LeaseModel = "FileUploadLease";
    public const string UsageModel = "FileUsage";
    internal DbSet<Dictionary<string, object?>> Records => db.Set<Dictionary<string, object?>>(RecordModel);
    internal DbSet<Dictionary<string, object?>> Leases => db.Set<Dictionary<string, object?>>(LeaseModel);
    internal DbSet<Dictionary<string, object?>> Usages => db.Set<Dictionary<string, object?>>(UsageModel);
    internal DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>同一只读单元复用自有连接并在失败/取消后归还；不建立事务、不跨字节I/O持有或关闭调用者连接。</summary>
    internal async Task<T> ReadUnitAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        var openedHere = db.Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        try
        {
            if (openedHere) await db.Database.OpenConnectionAsync(token);
            return await action();
        }
        finally { if (openedHere) await db.Database.CloseConnectionAsync(); }
    }
    /// <summary>只允许在真实平台事务中取得同文件锁，调用方先取得所属业务锁。</summary>
    public Task LockAsync(string fileId, CancellationToken token)
    {
        RequireTransaction();
        if (db.Database.IsSqlServer())
            return db.Database.ExecuteSqlRawAsync(
                "DECLARE @result int; EXEC @result = sp_getapplock @Resource=@key, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @result < 0 THROW 51000, 'Files state lock failed.', 1;",
                [new SqlParameter("@key", "Files." + fileId)], token);
        // 非 SQL Server 只供最低层状态测试，正式 Host 的提供者由模型计划约束。
        return Task.CompletedTask;
    }

    /// <summary>意图拥有已分配键和可信 actor；事务确认返回后才允许 I/O。</summary>
    public async Task CreateIntentAsync(FileReceiveIntent intent, CancellationToken token)
    {
        await transactions.ExecuteAsync(async inner =>
        {
            Records.Add(new()
            {
                ["EID"] = intent.FileId, ["StorageProviderKey"] = "Local", ["StorageKey"] = intent.StorageKey,
                ["FileName"] = intent.FileName, ["ContentType"] = intent.ContentType, ["Length"] = null, ["Sha256"] = null, ["State"] = "receiving"
            });
            Leases.Add(new()
            {
                ["EID"] = intent.UploadId, ["FileId"] = intent.FileId, ["ActorId"] = intent.ActorId,
                ["UsageOwnerKey"] = intent.OwnerKey, ["ExpiresAt"] = intent.ExpiresAt, ["State"] = "receiving", ["ConfirmedUsageId"] = null
            });
            await Task.CompletedTask;
            return true;
        }, token);
    }

    /// <summary>新 scope 无跟踪重读；提供可信归属时先过滤 lease，避免泄漏其他主体的文件回链。</summary>
    public async Task<FileStatePair?> ReadPairAsync(string uploadId, CancellationToken token, string? actorId = null, string? ownerKey = null)
    {
        var lease = await Leases.AsNoTracking().SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == uploadId
            && (actorId == null || EF.Property<string>(row, "ActorId") == actorId)
            && (ownerKey == null || EF.Property<string>(row, "UsageOwnerKey") == ownerKey), token);
        if (lease is null) return null;
        var fileId = Text(lease, "FileId");
        var file = await Records.AsNoTracking().SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == fileId, token)
            ?? throw Corrupted();
        return new(file, lease);
    }

    /// <summary>第二短事务只从仍有效 receiving 推进到 ready/staged，取消和到期优先。</summary>
    public async Task MarkReadyAsync(string uploadId, FileContentMeasurement measurement, CancellationToken token)
    {
        var pair = await ReadPairAsync(uploadId, token) ?? throw Unavailable();
        await transactions.ExecuteAsync(async inner =>
        {
            await LockAsync(Text(pair.File, "EID"), inner);
            var locked = await TrackedPairAsync(uploadId, inner);
            if (Text(locked.File, "State") != "receiving" || Text(locked.Lease, "State") != "receiving" || Expired(locked.Lease))
                throw Unavailable();
            locked.File["Length"] = measurement.Length;
            locked.File["Sha256"] = measurement.Sha256;
            locked.File["State"] = "ready";
            locked.Lease["State"] = "staged";
            return true;
        }, token);
    }

    /// <summary>仅对持久 receiving 失败标 pending；未知 ready 或 confirmed 绝不盲删。</summary>
    public async Task RecoverReceivingAsync(string uploadId, CancellationToken token)
    {
        var pair = await ReadPairAsync(uploadId, token);
        if (pair is null) return;
        await transactions.ExecuteAsync(async inner =>
        {
            await LockAsync(Text(pair.File, "EID"), inner);
            var locked = await TrackedPairAsync(uploadId, inner);
            if (Text(locked.File, "State") == "receiving" && Text(locked.Lease, "State") == "receiving"
                && !await HasActiveUsageAsync(Text(locked.File, "EID"), inner))
            {
                locked.File["State"] = "cleanupPending";
                locked.Lease["State"] = "cleanupPending";
            }
            return true;
        }, token);
    }

    /// <summary>当前 actor 的取消先提交 pending，再由调用方 abort/wait gate。</summary>
    public async Task<string> CancelAsync(string uploadId, string actorId, CancellationToken token)
    {
        var pair = await ReadPairAsync(uploadId, token, actorId) ?? throw Unavailable();
        return await transactions.ExecuteAsync(async inner =>
        {
            var fileId = Text(pair.File, "EID");
            await LockAsync(fileId, inner);
            var locked = await TrackedPairAsync(uploadId, inner, actorId);
            if (Text(locked.Lease, "State") == "confirmed") throw AlreadyConfirmed();
            if (Text(locked.Lease, "State") is "receiving" or "staged") locked.Lease["State"] = "cancelled";
            if (Text(locked.File, "State") != "deleted" && !await HasActiveUsageAsync(fileId, inner)
                && !await HasLegalLeaseAsync(fileId, uploadId, inner)) locked.File["State"] = "cleanupPending";
            return Text(locked.File, "StorageKey");
        }, token);
    }

    /// <summary>候选在数据库过滤和裁剪；无全盘扫描或完整表物化。</summary>
    public Task<List<Dictionary<string, object?>>> ReadCleanupCandidatesAsync(int batchSize, CancellationToken token)
    {
        var now = Now;
        return Records.AsNoTracking().Where(file => EF.Property<string>(file, "State") == "cleanupPending"
            || EF.Property<string>(file, "State") == "receiving"
            || (EF.Property<string>(file, "State") == "ready"
                && !Usages.Any(usage => EF.Property<string>(usage, "FileId") == EF.Property<string>(file, "EID") && EF.Property<string>(usage, "State") == "active")
                && !Leases.Any(lease => EF.Property<string>(lease, "FileId") == EF.Property<string>(file, "EID")
                    && (EF.Property<string>(lease, "State") == "receiving" || EF.Property<string>(lease, "State") == "staged")
                    && EF.Property<DateTime>(lease, "ExpiresAt") > now)
                && !Copies.Any(copy => (EF.Property<string>(copy, "FileId") == EF.Property<string>(file, "EID")
                        || EF.Property<string>(copy, "SourceFileId") == EF.Property<string>(file, "EID"))
                    && (EF.Property<string>(copy, "State") == "receiving" || EF.Property<string>(copy, "State") == "prepared")
                    && EF.Property<DateTime>(copy, "ExpiresAt") > now)))
            .OrderBy(file => EF.Property<string>(file, "State") == "cleanupPending" ? 0 : 1)
            .ThenBy(file => EF.Property<DateTime>(file, "CreatedAt")).ThenBy(file => EF.Property<string>(file, "EID"))
            .Take(batchSize).ToListAsync(token);
    }

    /// <summary>短事务标记回收；活跃使用或合法 lease 阻止删除，receiving 仅恢复无 writer 或已到期。</summary>
    public async Task<bool> TryMarkCleanupPendingAsync(string fileId, bool hasWriter, CancellationToken token)
    {
        return await transactions.ExecuteAsync(async inner =>
        {
            await LockAsync(fileId, inner);
            var file = await Records.SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == fileId, inner);
            if (file is null || Text(file, "State") == "deleted" || await HasActiveUsageAsync(fileId, inner)) return false;
            var now = Now;
            // 文件只有接收中且存在进程 writer 时，合法 receiving lease 才阻止启动恢复。
            var legal = await Leases.AnyAsync(row => EF.Property<string>(row, "FileId") == fileId
                && EF.Property<DateTime>(row, "ExpiresAt") > now
                && (EF.Property<string>(row, "State") == "staged" || (hasWriter && EF.Property<string>(row, "State") == "receiving")), inner);
            if (legal || await HasCopySourceLeaseAsync(fileId, inner) || await Copies.AnyAsync(row => EF.Property<string>(row, "FileId") == fileId
                && EF.Property<DateTime>(row, "ExpiresAt") > now && (EF.Property<string>(row, "State") == "prepared"
                    || hasWriter && EF.Property<string>(row, "State") == "receiving"), inner)) return false;
            file["State"] = "cleanupPending";
            await MarkLeasesAsync(fileId, "cleanupPending", inner);
            return true;
        }, token);
    }

    /// <summary>持有 I/O gate 后重新短事务裁决，事务返回后才操作字节。</summary>
    public async Task<bool> MayDeleteAsync(string fileId, CancellationToken token)
    {
        return await transactions.ExecuteAsync(async inner =>
        {
            await LockAsync(fileId, inner);
            var file = await Records.AsNoTracking().SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == fileId, inner);
            return file is not null && Text(file, "State") == "cleanupPending"
                && !await HasActiveUsageAsync(fileId, inner) && !await HasLegalLeaseAsync(fileId, null, inner);
        }, token);
    }

    /// <summary>字节删除成功后保留 tombstone；confirmed lease 保持消费历史。</summary>
    public async Task MarkDeletedAsync(string fileId, CancellationToken token)
    {
        await transactions.ExecuteAsync(async inner =>
        {
            await LockAsync(fileId, inner);
            var file = await Records.SingleAsync(row => EF.Property<string>(row, "EID") == fileId, inner);
            if (Text(file, "State") == "cleanupPending" && !await HasActiveUsageAsync(fileId, inner)
                && !await HasLegalLeaseAsync(fileId, null, inner))
            {
                file["State"] = "deleted";
                await MarkLeasesAsync(fileId, "cleaned", inner);
            }
            return true;
        }, token);
    }

    // 变更有限状态的 lease 使用数据库批量更新；固定审计按本次服务端 stamp 同步。
    private async Task<int> MarkLeasesAsync(string fileId, string state, CancellationToken token)
    {
        var stamp = audit.Current();
        var changed = await Leases.Where(row => EF.Property<string>(row, "FileId") == fileId
            && EF.Property<string>(row, "State") != "confirmed" && EF.Property<string>(row, "State") != "cleaned")
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string>(row, "State"), state)
                .SetProperty(row => EF.Property<string>(row, "UpdatedBy"), stamp.ActorId)
                .SetProperty(row => EF.Property<DateTime>(row, "UpdatedAt"), stamp.OccurredAtUtc), token);
        return changed + await Copies.Where(row => EF.Property<string>(row, "FileId") == fileId
            && EF.Property<string>(row, "State") != "confirmed" && EF.Property<string>(row, "State") != "cleaned")
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => EF.Property<string>(row, "State"), state)
                .SetProperty(row => EF.Property<string>(row, "UpdatedBy"), stamp.ActorId)
                .SetProperty(row => EF.Property<DateTime>(row, "UpdatedAt"), stamp.OccurredAtUtc), token);
    }

    // SQL 排除本事务已登记释放的使用，包含尚未保存的 Added active 使用。
    internal async Task<bool> HasActiveUsageAsync(string fileId, CancellationToken token)
    {
        var local = LocalRows(UsageModel);
        var released = local.Where(row => Text(row, "FileId") == fileId && Text(row, "State") == "released")
            .Select(row => Text(row, "EID")).ToArray();
        if (local.Any(row => Text(row, "FileId") == fileId && Text(row, "State") == "active")) return true;
        return await Usages.AnyAsync(row => EF.Property<string>(row, "FileId") == fileId
            && EF.Property<string>(row, "State") == "active" && !released.Contains(EF.Property<string>(row, "EID")), token);
    }

    // 排除已被本次取消/消费的 lease；confirmed 的 TTL 不是有效未消费租约。
    internal async Task<bool> HasLegalLeaseAsync(string fileId, string? exceptUploadId, CancellationToken token)
    {
        var now = Now;
        return await Leases.AnyAsync(row => EF.Property<string>(row, "FileId") == fileId
            && (exceptUploadId == null || EF.Property<string>(row, "EID") != exceptUploadId)
            && (EF.Property<string>(row, "State") == "receiving" || EF.Property<string>(row, "State") == "staged")
            && EF.Property<DateTime>(row, "ExpiresAt") > now, token)
            || await Copies.AnyAsync(row => (EF.Property<string>(row, "FileId") == fileId || EF.Property<string>(row, "SourceFileId") == fileId)
                && (EF.Property<string>(row, "State") == "receiving" || EF.Property<string>(row, "State") == "prepared")
                && EF.Property<DateTime>(row, "ExpiresAt") > now, token);
    }

    // 同请求重复 Confirm 可读取本次登记的 tracker，独立恢复必须使用 ReadPairAsync。
    internal async Task<FileStatePair> TrackedPairAsync(string uploadId, CancellationToken token, string? actorId = null, string? ownerKey = null)
    {
        var lease = LocalRows(LeaseModel).SingleOrDefault(row => Text(row, "EID") == uploadId)
            ?? await Leases.SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == uploadId, token) ?? throw Unavailable();
        if ((actorId is not null && Text(lease, "ActorId") != actorId)
            || (ownerKey is not null && Text(lease, "UsageOwnerKey") != ownerKey)) throw Unavailable();
        var fileId = Text(lease, "FileId");
        var file = LocalRows(RecordModel).SingleOrDefault(row => Text(row, "EID") == fileId)
            ?? await Records.SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == fileId, token) ?? throw Corrupted();
        return new(file, lease);
    }

    // Files 的 UTC 截止相等即过期。
    internal bool Expired(IReadOnlyDictionary<string, object?> lease) => Now >= (DateTime)lease["ExpiresAt"]!;
    // Dictionary shared-type 的 DbSet.Local 可混入其他模型，必须先按正式 EF 模型身份限定归属。
    internal IReadOnlyList<Dictionary<string, object?>> LocalRows(string model) => db.ChangeTracker.Entries()
        .Where(entry => entry.Metadata.Name == model && entry.State != EntityState.Deleted)
        .Select(entry => (Dictionary<string, object?>)entry.Entity).ToArray();
    // 字段缺失保留明确完整性故障，不跳过该真实模型的损坏行。
    internal static string Text(IReadOnlyDictionary<string, object?> row, string field) => row.GetValueOrDefault(field) as string ?? throw Corrupted();
    // 已获业务资格后的持久身份损坏必须传播为明确故障。
    internal static InvalidOperationException Corrupted() => new("Files.PersistentAssociationCorrupted");
    // 未确认上传凭证的安全错误不披露其他 actor 的文件事实。
    internal static FileLifecycleException Unavailable() => new(404, "Files.UploadUnavailable");
    // 本人已消费 token 的冲突与不可见 token 分开，重复同目标 active 使用由 Confirm 先行处理。
    internal static FileLifecycleException AlreadyConfirmed() => new(409, "Files.UploadAlreadyConfirmed");

    // 只能在整批 actor/用途归属已通过后判状态，清理推进不抹掉本人失效 token 的冲突语义。
    internal void RequireStagedLease(IReadOnlyDictionary<string, object?> lease)
    {
        var status = Text(lease, "State");
        if (status == "confirmed") throw AlreadyConfirmed();
        if (status == "cancelled") throw new FileLifecycleException(409, "Files.UploadCancelled");
        if (status is not ("receiving" or "staged" or "cleanupPending" or "cleaned")) throw Corrupted();
        if (Expired(lease)) throw new FileLifecycleException(409, "Files.UploadExpired");
        if (status is "cleanupPending" or "cleaned") throw new FileLifecycleException(409, "Files.UploadCancelled");
        if (status != "staged") throw Unavailable();
    }

    // Confirm/Release 和 app lock 不允许悄悄启动自己的业务事务。
    internal void RequireTransaction()
    {
        if (!transactions.IsActive || db.Database.CurrentTransaction is null) throw new InvalidOperationException("Files.TransactionRequired");
    }
}

public sealed record FileReceiveIntent(string UploadId, string FileId, string StorageKey, string ActorId, string OwnerKey,
    string FileName, string ContentType, DateTime ExpiresAt);
public sealed record FileStatePair(Dictionary<string, object?> File, Dictionary<string, object?> Lease);
