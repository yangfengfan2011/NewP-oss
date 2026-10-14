using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Files.Contracts;
using ICP.Platform.Modules.Files.Storage;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static ICP.Platform.Modules.Files.Services.FileStateStore;

namespace ICP.Platform.Modules.Files.Services;

/// <summary>平台文件生命周期；上传短事务与使用登记的外层业务事务各自拥有提交边界。</summary>
public sealed partial class FileLifecycleService(FileStateStore state, IPlatformTransactionExecutor transactions,
    IServiceScopeFactory scopes, IFileContentStore storage, FileIoGate gate, IEntityAuditValueProvider audit,
    IPlatformObjectIdGenerator ids, IOptions<FilesOptions> options, IEnumerable<IFileOwnerPolicy> policies,
    ILogger<FileLifecycleService> logger) : IFileLifecycleService
{
    private readonly IReadOnlyDictionary<string, IFileOwnerPolicy> _policies = policies.ToDictionary(policy => policy.OwnerKey, StringComparer.Ordinal);
    public int MaxReadBatchSize => options.Value.MaxReadBatchSize;
    public long MaxUploadSizeBytes => options.Value.MaxUploadSizeBytes;

    /// <inheritdoc/>
    public async Task<StagedFileInfo> StageAsync(Stream stream, string fileName, string? contentType, string ownerKey, CancellationToken token = default)
    {
        if (transactions.IsActive) throw new InvalidOperationException("Files.StageRequiresIndependentTransaction");
        if (stream is null || !stream.CanRead) throw new FileLifecycleException(400, "Files.InvalidContent");
        var safeName = SafeFileName(fileName);
        var mime = NormalizeContentType(contentType);
        Policy(ownerKey).ValidateUpload(safeName, mime);
        var actor = Actor();
        var expires = state.Now.AddHours(options.Value.UploadLeaseHours);
        var intent = new FileReceiveIntent(ids.NewId(LeaseModel), ids.NewId(RecordModel), Guid.NewGuid().ToString("N"), actor,
            ownerKey, safeName, mime, expires);
        using var writer = gate.ReserveWriter(intent.StorageKey, token);
        try
        {
            // 即使提交结果未知也不开始写；原事务退出后的新 scope 才能裁决持久意图。
            await InScopeAsync(store => store.CreateIntentAsync(intent, writer.Token));
            using (await gate.EnterAsync(intent.StorageKey, writer.Token))
            {
                var current = await InScopeAsync(store => store.ReadPairAsync(intent.UploadId, writer.Token)) ?? throw Unavailable();
                if (Text(current.File, "State") != "receiving" || Text(current.Lease, "State") != "receiving"
                    || state.Now >= (DateTime)current.Lease["ExpiresAt"]!) throw Unavailable();
                var measured = await storage.WritePartialAsync(intent.StorageKey, stream, MaxUploadSizeBytes, writer.Token);
                await storage.CompleteAsync(intent.StorageKey, writer.Token);
                await InScopeAsync(store => store.MarkReadyAsync(intent.UploadId, measured, writer.Token));
                return new(intent.UploadId, intent.FileId, safeName, measured.Length, mime, expires);
            }
        }
        catch
        {
            // 只恢复 receiving；已 staged 的未知提交按 TTL 收敛，active 使用永远阻止清理。
            try { await InScopeAsync(store => store.RecoverReceivingAsync(intent.UploadId, CancellationToken.None)); }
            catch (Exception recovery) { logger.LogWarning(recovery, "Files receiving intent recovery deferred for {UploadId}.", intent.UploadId); }
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StagedFileInfo>> ReadOwnedStagedAsync(IReadOnlyList<string> uploadIds, string ownerKey, CancellationToken token = default)
    {
        ValidateBatch(uploadIds);
        Policy(ownerKey);
        var actor = Actor();
        foreach (var id in uploadIds) ValidateUploadId(id);
        var leases = await state.Leases.AsNoTracking().Where(row => uploadIds.Contains(EF.Property<string>(row, "EID"))
            && EF.Property<string>(row, "ActorId") == actor && EF.Property<string>(row, "UsageOwnerKey") == ownerKey).ToListAsync(token);
        if (leases.Count != uploadIds.Count) throw Unavailable();
        foreach (var lease in leases) state.RequireStagedLease(lease);
        var fileIds = leases.Select(row => Text(row, "FileId")).Distinct(StringComparer.Ordinal).ToArray();
        var files = await state.Records.AsNoTracking().Where(row => fileIds.Contains(EF.Property<string>(row, "EID"))).ToDictionaryAsync(row => Text(row, "EID"), token);
        var byUpload = leases.ToDictionary(row => Text(row, "EID"), StringComparer.Ordinal);
        return uploadIds.Select(upload =>
        {
            var lease = byUpload[upload];
            if (!files.TryGetValue(Text(lease, "FileId"), out var file)) throw Corrupted();
            RequireReady(file);
            return new StagedFileInfo(upload, Text(file, "EID"), Text(file, "FileName"), Length(file), Text(file, "ContentType"), (DateTime)lease["ExpiresAt"]!);
        }).ToArray();
    }

    /// <inheritdoc/>
    public async Task<ConfirmedFileUsage> ConfirmUsageAsync(string uploadId, FileOwnerReference owner, CancellationToken token = default)
    {
        state.RequireTransaction();
        ValidateUploadId(uploadId);
        var policy = Policy(owner.Key);
        var actor = Actor();
        var pair = await state.ReadPairAsync(uploadId, token, actor, owner.Key) ?? throw Unavailable();
        await state.LockAsync(Text(pair.File, "EID"), token);
        pair = await state.TrackedPairAsync(uploadId, token, actor, owner.Key);
        var fileId = Text(pair.File, "EID");
        if (Text(pair.Lease, "State") == "confirmed")
        {
            var usageId = Text(pair.Lease, "ConfirmedUsageId");
            var usage = state.LocalRows(UsageModel).SingleOrDefault(row => Text(row, "EID") == usageId)
                ?? await state.Usages.SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == usageId, token) ?? throw Corrupted();
            if (Text(usage, "OwnerKey") != owner.Key || Text(usage, "FileId") != fileId) throw Corrupted();
            if (Text(usage, "OwnerObjectId") != owner.Id || Text(usage, "State") == "released") throw AlreadyConfirmed();
            if (Text(usage, "State") != "active") throw Corrupted();
            RequireReady(pair.File);
            await policy.VerifyAsync(owner, fileId, usageId, FileOwnerOperation.Confirm, token);
            return new(usageId, fileId);
        }
        state.RequireStagedLease(pair.Lease);
        RequireReady(pair.File);
        await policy.VerifyAsync(owner, fileId, null, FileOwnerOperation.Confirm, token);
        if (await state.Usages.AnyAsync(row => EF.Property<string>(row, "OwnerKey") == owner.Key
            && EF.Property<string>(row, "OwnerObjectId") == owner.Id, token)
            || state.LocalRows(UsageModel).Any(row => Text(row, "OwnerKey") == owner.Key && Text(row, "OwnerObjectId") == owner.Id)) throw Unavailable();
        var id = ids.NewId(UsageModel);
        state.Usages.Add(new() { ["EID"] = id, ["FileId"] = fileId, ["OwnerKey"] = owner.Key, ["OwnerObjectId"] = owner.Id, ["State"] = "active" });
        pair.Lease["State"] = "confirmed";
        pair.Lease["ConfirmedUsageId"] = id;
        return new(id, fileId);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FileUsageMetadata>> ReadUsageMetadataAsync(IReadOnlyList<FileUsageReference> usageReferences, CancellationToken token = default)
    {
        ValidateBatch(usageReferences.Select(reference => reference.UsageId).ToArray());
        var facts = await ReadUsageFactsAsync(usageReferences, FileOwnerOperation.Read, token);
        return usageReferences.Select(reference =>
        {
            var file = facts[reference.UsageId];
            return new FileUsageMetadata(reference.UsageId, reference.FileId, Text(file, "FileName"), Length(file), Text(file, "ContentType"));
        }).ToArray();
    }

    /// <inheritdoc/>
    public async Task<FileUsageContent> OpenUsageAsync(FileUsageReference usageReference, CancellationToken token = default)
    {
        var facts = await ReadUsageFactsAsync([usageReference], FileOwnerOperation.Download, token);
        var file = facts[usageReference.UsageId];
        Stream stream;
        try { stream = await storage.OpenAsync(Text(file, "StorageKey"), token); }
        catch (FileNotFoundException exception) { throw new InvalidOperationException("Files.ContentMissing", exception); }
        catch (DirectoryNotFoundException exception) { throw new InvalidOperationException("Files.ContentMissing", exception); }
        try
        {
            if (stream.CanSeek && stream.Length != Length(file)) throw new InvalidOperationException("Files.ContentLengthMismatch");
            return new(new CancellationReadStream(stream, token), Text(file, "FileName"), Text(file, "ContentType"), Length(file));
        }
        catch { await stream.DisposeAsync(); throw; }
    }

    /// <inheritdoc/>
    public async Task CancelStageAsync(string uploadId, CancellationToken token = default)
    {
        if (transactions.IsActive) throw new InvalidOperationException("Files.CancelRequiresIndependentTransaction");
        ValidateUploadId(uploadId);
        var key = await InScopeAsync(store => store.CancelAsync(uploadId, Actor(), token));
        gate.Abort(key);
        // 不在任何 DB 事务内等待 writer；pending 已提交，调用方可重复取消。
        using (await gate.EnterAsync(key, token)) { }
    }

    /// <inheritdoc/>
    public async Task ReleaseUsageAsync(FileUsageReference usageReference, CancellationToken token = default)
    {
        state.RequireTransaction();
        await state.LockAsync(usageReference.FileId, token);
        await Policy(usageReference.Owner.Key).VerifyAsync(usageReference.Owner, usageReference.FileId, usageReference.UsageId, FileOwnerOperation.Release, token);
        var usage = state.LocalRows(UsageModel).SingleOrDefault(row => Text(row, "EID") == usageReference.UsageId)
            ?? await state.Usages.SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == usageReference.UsageId, token) ?? throw Corrupted();
        CheckUsage(usage, usageReference);
        if (Text(usage, "State") == "released") return;
        if (Text(usage, "State") != "active") throw Corrupted();
        var file = await state.Records.SingleOrDefaultAsync(row => EF.Property<string>(row, "EID") == usageReference.FileId, token) ?? throw Corrupted();
        RequireReady(file);
        var leases = await state.Leases.AsNoTracking().Where(row => EF.Property<string>(row, "ConfirmedUsageId") == usageReference.UsageId).ToListAsync(token);
        if (leases.Count != 1 || Text(leases[0], "FileId") != usageReference.FileId || Text(leases[0], "State") != "confirmed") throw Corrupted();
        usage["State"] = "released";
        if (!await state.HasActiveUsageAsync(usageReference.FileId, token) && !await state.HasLegalLeaseAsync(usageReference.FileId, null, token))
            file["State"] = "cleanupPending";
    }

    // 跨用途先批量业务资格，再一次批读各 Files 表；缺失/互换是可信关联的损坏。
    private async Task<IReadOnlyDictionary<string, Dictionary<string, object?>>> ReadUsageFactsAsync(IReadOnlyList<FileUsageReference> references,
        FileOwnerOperation operation, CancellationToken token)
    {
        ValidateBatch(references.Select(reference => reference.UsageId).ToArray());
        foreach (var group in references.GroupBy(reference => reference.Owner.Key, StringComparer.Ordinal))
            await Policy(group.Key).VerifyBatchAsync(group.ToArray(), operation, token);
        var usageIds = references.Select(reference => reference.UsageId).ToArray();
        var fileIds = references.Select(reference => reference.FileId).Distinct(StringComparer.Ordinal).ToArray();
        var usages = await state.Usages.AsNoTracking().Where(row => usageIds.Contains(EF.Property<string>(row, "EID"))).ToDictionaryAsync(row => Text(row, "EID"), token);
        var files = await state.Records.AsNoTracking().Where(row => fileIds.Contains(EF.Property<string>(row, "EID"))).ToDictionaryAsync(row => Text(row, "EID"), token);
        // 每个使用只能有一个反向 lease；多取一行损坏哨兵，不能先物化任意数量的重复关联。
        var leases = await state.Leases.AsNoTracking().Where(row => usageIds.Contains(EF.Property<string>(row, "ConfirmedUsageId")))
            .Take(usageIds.Length + 1).ToListAsync(token);
        if (leases.Count > usageIds.Length) throw Corrupted();
        var byUsage = leases.ToLookup(row => Text(row, "ConfirmedUsageId"), StringComparer.Ordinal);
        var result = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            if (!usages.TryGetValue(reference.UsageId, out var usage) || !files.TryGetValue(reference.FileId, out var file)) throw Corrupted();
            CheckUsage(usage, reference);
            if (Text(usage, "State") != "active") throw Corrupted();
            RequireReady(file);
            var related = byUsage[reference.UsageId].ToArray();
            if (related.Length != 1 || Text(related[0], "State") != "confirmed" || Text(related[0], "FileId") != reference.FileId
                || Text(related[0], "UsageOwnerKey") != reference.Owner.Key) throw Corrupted();
            result.Add(reference.UsageId, file);
        }
        return result;
    }

    // 用新 scope 完成上传/恢复短事务，不污染调用方的业务 tracker。
    private async Task<T> InScopeAsync<T>(Func<FileStateStore, Task<T>> action)
    {
        using var scope = scopes.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<FileStateStore>());
    }

    // 无返回值短事务仍在独立 scope 内退出后释放资源。
    private async Task InScopeAsync(Func<FileStateStore, Task> action)
    {
        using var scope = scopes.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<FileStateStore>());
    }

    // 未登记用途是装配故障，绝不退回默认业务类型。
    private IFileOwnerPolicy Policy(string ownerKey) => _policies.GetValueOrDefault(ownerKey) ?? throw new InvalidOperationException("Files.OwnerPolicyMissing");
    // actor 只来自统一可信审计来源。
    private string Actor()
    {
        var actor = audit.Current().ActorId;
        if (string.IsNullOrWhiteSpace(actor)) throw new FileLifecycleException(403, "Files.SubjectRequired");
        return actor;
    }

    // 当前 ID 形状沿平台生成器，不接受路径、其他模型或大小写变体。
    private static void ValidateUploadId(string id)
    {
        if (id is null || !UploadIdPattern().IsMatch(id)) throw Unavailable();
    }
    // 界限在查询前执行，包含重复保护；空文件列表可合法返回空。
    private void ValidateBatch(IReadOnlyList<string> idsToRead)
    {
        if (idsToRead.Count > MaxReadBatchSize || idsToRead.Any(string.IsNullOrWhiteSpace)
            || idsToRead.Distinct(StringComparer.Ordinal).Count() != idsToRead.Count) throw new FileLifecycleException(400, "Files.InvalidReadBatch");
    }
    // 已获 owner 资格后身份不匹配是持久损坏，不伪装成不可见目标。
    private static void CheckUsage(IReadOnlyDictionary<string, object?> usage, FileUsageReference reference)
    {
        if (Text(usage, "FileId") != reference.FileId || Text(usage, "OwnerKey") != reference.Owner.Key
            || Text(usage, "OwnerObjectId") != reference.Owner.Id) throw Corrupted();
    }
    // ready 的真实计数和大写 hash 必须齐备，不能回成功空内容。
    private static void RequireReady(IReadOnlyDictionary<string, object?> file)
    {
        if (Text(file, "State") != "ready" || Length(file) <= 0 || Text(file, "StorageProviderKey") != "Local"
            || !HashPattern().IsMatch(Text(file, "Sha256"))) throw Corrupted();
    }
    // 实际长度必须来自持久 long 值，不能把空值或客户端声明当内容事实。
    private static long Length(IReadOnlyDictionary<string, object?> file) => file.GetValueOrDefault("Length") is long value ? value : throw Corrupted();
    // 安全显示名不参与物理路径，不静默接受客户目录或控制字符。
    private static string SafeFileName(string name)
    {
        if (name is null || name.Length is < 1 or > 256 || name != name.Trim() || name is "." or ".."
            || name.Any(character => char.IsControl(character) || "/\\:<>\"|?*".Contains(character)))
            throw new FileLifecycleException(400, "Files.InvalidFileName");
        return name;
    }
    // MIME 只保留服务器规范 token；未知或非法输入使用安全通用类型。
    private static string NormalizeContentType(string? contentType) => MediaTypeHeaderValue.TryParse(contentType, out var parsed)
        && parsed.MediaType is { Length: > 0 and <= 256 } media && !media.Contains('*') ? media.ToLowerInvariant() : "application/octet-stream";

    // 仅接受当前上传 lease 的正式平台身份形状。
    [GeneratedRegex("^FileUploadLease_[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex UploadIdPattern();
    // 接收完成后必须持有单遍计算的大写 SHA256。
    [GeneratedRegex("^[0-9A-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
}
