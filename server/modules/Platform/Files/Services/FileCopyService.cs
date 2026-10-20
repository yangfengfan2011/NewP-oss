using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Modules.Files.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static ICP.Platform.Modules.Files.Services.FileStateStore;

namespace ICP.Platform.Modules.Files.Services;

public sealed partial class FileLifecycleService
{
    /// <inheritdoc/>
    public async Task<FileCopyPreparation> PrepareCopyAsync(string requestKey, FileUsageReference source, string targetOwnerKey, FileOwnerInternalExecutionContext context, CancellationToken token = default)
    {
        if (transactions.IsActive) throw new InvalidOperationException("Files.CopyRequiresIndependentTransaction");
        ValidateCopyKey(requestKey); Policy(targetOwnerKey);
        var actor = Actor();
        var facts = await ReadUsageFactsAsync([source], FileOwnerOperation.CopySource, context, token);
        var original = facts[source.UsageId];
        var existing = await InScopeAsync(store => store.ReadCopyPairAsync(requestKey, actor, targetOwnerKey, false, token));
        if (existing is not null)
        {
            if (Text(existing.Lease, "State") is not ("prepared" or "confirmed")) throw new FileLifecycleException(409, "Files.CopyNotPrepared");
            RequireCopySource(existing.Lease, source);
            return CopyResult(requestKey, existing);
        }
        var intent = new FileReceiveIntent(ids.NewId(CopyModel), ids.NewId(RecordModel), Guid.NewGuid().ToString("N"), actor,
            targetOwnerKey, Text(original, "FileName"), Text(original, "ContentType"), state.Now.AddHours(options.Value.UploadLeaseHours));
        using var writer = gate.ReserveWriter(intent.StorageKey, token);
        try
        {
            await InScopeAsync(store => store.CreateCopyIntentAsync(intent, requestKey, source, writer.Token));
            using (await gate.EnterAsync(intent.StorageKey, writer.Token))
            {
                var current = await InScopeAsync(store => store.ReadCopyPairAsync(requestKey, actor, targetOwnerKey, false, writer.Token)) ?? throw CopyUnavailable();
                if (Text(current.Lease, "State") != "receiving") throw CopyUnavailable();
                await using var content = await storage.OpenAsync(Text(original, "StorageKey"), writer.Token);
                var measured = await storage.WritePartialAsync(intent.StorageKey, content, Length(original), writer.Token);
                if (measured.Length != Length(original) || !StringComparer.OrdinalIgnoreCase.Equals(measured.Sha256, Text(original, "Sha256")))
                    throw new InvalidOperationException("Files.CopyContentMismatch");
                await storage.CompleteAsync(intent.StorageKey, writer.Token);
                await InScopeAsync(store => store.MarkCopyReadyAsync(requestKey, actor, targetOwnerKey, measured, writer.Token));
                return new(requestKey, "prepared", intent.FileId, intent.FileName, intent.ContentType, measured.Length, measured.Sha256);
            }
        }
        catch
        {
            // 退出未知事务后只取消仍receiving的状态；prepared/confirmed保留，不能按失败响应盲删。
            try { await InScopeAsync(store => store.CancelCopyIntentAsync(requestKey, actor, targetOwnerKey, true, ids.NewId(CopyModel), CancellationToken.None)); }
            catch (Exception failure) { logger.LogWarning(failure, "Files copy recovery deferred for {CopyId}.", intent.UploadId); }
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<FileCopyPreparation?> ReadCopyAsync(string requestKey, string targetOwnerKey, CancellationToken token = default)
    {
        ValidateCopyKey(requestKey); Policy(targetOwnerKey);
        var pair = await InScopeAsync(store => store.ReadCopyPairAsync(requestKey, Actor(), targetOwnerKey, false, token));
        return pair is null ? null : CopyResult(requestKey, pair);
    }

    /// <inheritdoc/>
    public async Task<ConfirmedFileUsage> ConfirmCopyAsync(string requestKey, FileOwnerReference target, FileOwnerInternalExecutionContext sourceContext, FileOwnerCreationContext creation, CancellationToken token = default)
    {
        state.RequireTransaction(); ValidateCopyKey(requestKey);
        var known = await state.ReadCopyPairAsync(requestKey, Actor(), target.Key, false, token) ?? throw CopyUnavailable();
        if (Text(known.Lease, "State") is not ("prepared" or "confirmed")) throw new FileLifecycleException(409, "Files.CopyNotPrepared");
        foreach (var lockedId in new[] { Text(known.File, "EID"), Text(known.Lease, "SourceFileId") }.Order(StringComparer.Ordinal)) await state.LockAsync(lockedId, token);
        var pair = await state.ReadCopyPairAsync(requestKey, Actor(), target.Key, true, token) ?? throw CopyUnavailable();
        var fileId = Text(pair.File, "EID");
        if (Text(pair.Lease, "State") == "confirmed")
        {
            // 已确认操作只核持久source owner的上下文形状，不重读源对象或重放source权限。
            await Policy(Text(pair.Lease, "SourceOwnerKey")).VerifyBatchAsync([], FileOwnerOperation.CopySource, sourceContext, token);
            var usageId = Text(pair.Lease, "ConfirmedUsageId");
            var usage = await state.Usages.SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == usageId, token) ?? throw Corrupted();
            if (Text(usage, "FileId") != fileId || Text(usage, "OwnerKey") != target.Key || Text(usage, "OwnerObjectId") != target.Id
                || Text(usage, "State") != "active") throw new FileLifecycleException(409, "Files.CopyAlreadyConfirmed");
            await Policy(target.Key).VerifyAsync(target, fileId, usageId, FileOwnerOperation.ConfirmCopy, creation, token);
            return new(usageId, fileId);
        }
        if (Text(pair.Lease, "State") != "prepared" || state.Expired(pair.Lease)) throw new FileLifecycleException(409, "Files.CopyNotPrepared");
        RequireReady(pair.File);
        var source = new FileUsageReference(Text(pair.Lease, "SourceUsageId"), Text(pair.Lease, "SourceFileId"),
            new(Text(pair.Lease, "SourceOwnerKey"), Text(pair.Lease, "SourceOwnerId")));
        await ReadUsageFactsAsync([source], FileOwnerOperation.CopySource, sourceContext, token);
        await Policy(target.Key).VerifyAsync(target, fileId, null, FileOwnerOperation.ConfirmCopy, creation, token);
        if (await state.Usages.AnyAsync(row => EF.Property<string>(row, "OwnerKey") == target.Key && EF.Property<string>(row, "OwnerObjectId") == target.Id, token)
            || state.LocalRows(UsageModel).Any(row => Text(row, "OwnerKey") == target.Key && Text(row, "OwnerObjectId") == target.Id)) throw CopyUnavailable();
        var id = ids.NewId(UsageModel);
        state.Usages.Add(new() { ["EID"] = id, ["FileId"] = fileId, ["OwnerKey"] = target.Key, ["OwnerObjectId"] = target.Id, ["State"] = "active" });
        pair.Lease["State"] = "confirmed"; pair.Lease["ConfirmedUsageId"] = id;
        return new(id, fileId);
    }

    /// <inheritdoc/>
    public async Task CancelCopyAsync(string requestKey, string targetOwnerKey, CancellationToken token = default)
    {
        if (transactions.IsActive) throw new InvalidOperationException("Files.CopyCancelRequiresIndependentTransaction");
        ValidateCopyKey(requestKey); Policy(targetOwnerKey);
        var key = await InScopeAsync(store => store.CancelCopyIntentAsync(requestKey, Actor(), targetOwnerKey, false, ids.NewId(CopyModel), token));
        if (key is null) return;
        gate.Abort(key);
        using (await gate.EnterAsync(key, token)) { }
    }

    // 永久绑定首次真实来源；同request key不能用于另一个文件或owner。
    private static void RequireCopySource(IReadOnlyDictionary<string, object?> lease, FileUsageReference source)
    {
        if (Text(lease, "SourceUsageId") != source.UsageId || Text(lease, "SourceFileId") != source.FileId
            || Text(lease, "SourceOwnerKey") != source.Owner.Key || Text(lease, "SourceOwnerId") != source.Owner.Id) throw CopyUnavailable();
    }

    // 返回契约不含存储路径、原使用或原业务身份。
    private static FileCopyPreparation CopyResult(string key, FileStatePair pair) => new(key, Text(pair.Lease, "State"), pair.File.GetValueOrDefault("EID") as string,
        pair.File.GetValueOrDefault("FileName") as string, pair.File.GetValueOrDefault("ContentType") as string,
        pair.File.GetValueOrDefault("Length") as long?, pair.File.GetValueOrDefault("Sha256") as string);

    // key仅是可信调用方的幂等相关键，不允许无界数据库参数或借用上传token。
    private static void ValidateCopyKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(char.IsControl)) throw CopyUnavailable();
    }
}
