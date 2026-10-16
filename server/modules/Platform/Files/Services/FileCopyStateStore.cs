using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Modules.Files.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace ICP.Platform.Modules.Files.Services;

public sealed partial class FileStateStore
{
    public const string CopyModel = "FileCopyLease";
    internal DbSet<Dictionary<string, object?>> Copies => db.Set<Dictionary<string, object?>>(CopyModel);

    /// <summary>request key 只在本主体及目标用途内读取；不能借它读取他人的来源。</summary>
    internal async Task<FileStatePair?> ReadCopyPairAsync(string key, string actor, string owner, bool tracking, CancellationToken token)
    {
        var query = Copies.Where(row => EF.Property<string>(row, "RequestKey") == key && EF.Property<string>(row, "ActorId") == actor
            && EF.Property<string>(row, "UsageOwnerKey") == owner);
        var copy = await (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(token);
        if (copy is null) return null;
        if (copy.GetValueOrDefault("FileId") is not string fileId)
            return Text(copy, "State") == "cancelled" ? new(new(), copy) : throw Corrupted();
        var records = Records.Where(row => EF.Property<string>(row, "EID") == fileId);
        var file = await (tracking ? records : records.AsNoTracking()).SingleOrDefaultAsync(token) ?? throw Corrupted();
        return new(file, copy);
    }

    /// <summary>持有源文件短锁核证活跃使用，再登记独立目标及明确来源；不等待字节传输。</summary>
    internal Task CreateCopyIntentAsync(FileReceiveIntent intent, string key, FileUsageReference source, CancellationToken token) =>
        transactions.ExecuteAsync(async inner =>
        {
            await LockCopyRequestAsync(key, intent.ActorId, inner);
            if (await Copies.AnyAsync(row => EF.Property<string>(row, "RequestKey") == key && EF.Property<string>(row, "ActorId") == intent.ActorId, inner))
                throw new FileLifecycleException(409, "Files.CopyNotPrepared");
            await LockAsync(source.FileId, inner);
            var usage = await Usages.AsNoTracking().SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == source.UsageId, inner)
                ?? throw new FileLifecycleException(404, "Files.UsageUnavailable");
            if (Text(usage, "State") != "active" || Text(usage, "FileId") != source.FileId || Text(usage, "OwnerKey") != source.Owner.Key
                || Text(usage, "OwnerObjectId") != source.Owner.Id) throw new FileLifecycleException(404, "Files.UsageUnavailable");
            Records.Add(new() { ["EID"] = intent.FileId, ["StorageProviderKey"] = "Local", ["StorageKey"] = intent.StorageKey,
                ["FileName"] = intent.FileName, ["ContentType"] = intent.ContentType, ["Length"] = null, ["Sha256"] = null, ["State"] = "receiving" });
            Copies.Add(new() { ["EID"] = intent.UploadId, ["FileId"] = intent.FileId, ["RequestKey"] = key, ["ActorId"] = intent.ActorId,
                ["UsageOwnerKey"] = intent.OwnerKey, ["SourceFileId"] = source.FileId, ["SourceUsageId"] = source.UsageId,
                ["SourceOwnerKey"] = source.Owner.Key, ["SourceOwnerId"] = source.Owner.Id, ["State"] = "receiving",
                ["ExpiresAt"] = intent.ExpiresAt, ["ConfirmedUsageId"] = null });
            return true;
        }, token).AsTask();

    /// <summary>不可覆盖完成后短事务核证取消/到期并公布测量事实；未知提交保留可重读状态。</summary>
    internal Task MarkCopyReadyAsync(string key, string actor, string owner, FileContentMeasurement measured, CancellationToken token) =>
        transactions.ExecuteAsync(async inner =>
        {
            var known = await ReadCopyPairAsync(key, actor, owner, false, inner) ?? throw CopyUnavailable();
            await LockAsync(Text(known.File, "EID"), inner);
            var pair = await ReadCopyPairAsync(key, actor, owner, true, inner) ?? throw CopyUnavailable();
            if (Text(pair.Lease, "State") != "receiving" || Text(pair.File, "State") != "receiving" || Expired(pair.Lease)) throw CopyUnavailable();
            pair.File["Length"] = measured.Length; pair.File["Sha256"] = measured.Sha256; pair.File["State"] = "ready";
            pair.Lease["State"] = "prepared";
            return true;
        }, token).AsTask();

    /// <summary>失败恢复只取消仍receiving的意图，显式取消也允许prepared；已确认事实不改写。</summary>
    internal Task<string?> CancelCopyIntentAsync(string key, string actor, string owner, bool onlyReceiving, string cancellationId, CancellationToken token) =>
        transactions.ExecuteAsync<string?>(async inner =>
        {
            await LockCopyRequestAsync(key, actor, inner);
            var known = await ReadCopyPairAsync(key, actor, owner, false, inner);
            if (known is null)
            {
                if (!onlyReceiving)
                {
                    if (await Copies.AnyAsync(row => EF.Property<string>(row, "RequestKey") == key && EF.Property<string>(row, "ActorId") == actor, inner)) throw CopyUnavailable();
                    Copies.Add(new() { ["EID"] = cancellationId, ["RequestKey"] = key, ["ActorId"] = actor, ["UsageOwnerKey"] = owner,
                        ["State"] = "cancelled", ["ExpiresAt"] = Now, ["FileId"] = null, ["SourceFileId"] = null,
                        ["SourceUsageId"] = null, ["SourceOwnerKey"] = null, ["SourceOwnerId"] = null, ["ConfirmedUsageId"] = null });
                }
                return null;
            }
            if (known.Lease.GetValueOrDefault("FileId") is null) return null;
            await LockAsync(Text(known.File, "EID"), inner);
            var pair = await ReadCopyPairAsync(key, actor, owner, true, inner) ?? throw CopyUnavailable();
            var status = Text(pair.Lease, "State");
            if (status == "confirmed" || onlyReceiving && status != "receiving") return null;
            if (status is "receiving" or "prepared") pair.Lease["State"] = "cancelled";
            if (Text(pair.File, "State") != "deleted" && !await HasActiveUsageAsync(Text(pair.File, "EID"), inner)) pair.File["State"] = "cleanupPending";
            return Text(pair.File, "StorageKey");
        }, token).AsTask();

    // 与同key意图登记严格串行；取消墓碑是持久事实，跨进程也不能出现“取消后才登记”的迟到准备。
    private Task LockCopyRequestAsync(string key, string actor, CancellationToken token)
    {
        RequireTransaction();
        return db.Database.IsSqlServer() ? db.Database.ExecuteSqlRawAsync(
            "DECLARE @result int; EXEC @result = sp_getapplock @Resource=@key, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @result < 0 THROW 51000, 'Files copy request lock failed.', 1;",
            [new SqlParameter("@key", "Files.CopyRequest." + actor + "." + key)], token) : Task.CompletedTask;
    }

    // source保护不依赖原usage仍活跃；删除源业务可以提交，但直到复制退出才允许回收其字节。
    internal Task<bool> HasCopySourceLeaseAsync(string fileId, CancellationToken token) => Copies.AnyAsync(row =>
        EF.Property<string>(row, "SourceFileId") == fileId && (EF.Property<string>(row, "State") == "receiving" || EF.Property<string>(row, "State") == "prepared")
        && EF.Property<DateTime>(row, "ExpiresAt") > Now, token);

    // 复制准备与上传凭据分离，错误不披露来源身份。
    internal static FileLifecycleException CopyUnavailable() => new(404, "Files.CopyUnavailable");
}
