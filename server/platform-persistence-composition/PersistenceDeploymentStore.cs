using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.Planning;

namespace ICP.Platform.Persistence.Composition;

/// <summary>保存 Host 正式编译的数据库部署资产；成功事实与首 DDL 前的恢复候选分开提交。</summary>
public sealed class PersistenceDeploymentStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _root;
    private readonly string _targetIdentity;
    private readonly PersistenceProvider _provider;
    private FileStream? _lease;

    /// <summary>绑定明确的 provider、服务器与 catalog 标识；标识不包含连接凭据。</summary>
    public PersistenceDeploymentStore(string root, PersistenceProvider provider, string targetIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetIdentity);
        _provider = provider;
        _targetIdentity = targetIdentity;
        _root = Path.Combine(Path.GetFullPath(root), Hash(Encoding.UTF8.GetBytes(provider + "|" + targetIdentity)));
    }

    /// <summary>读取最后成功部署的可信输入；未完成候选不能被旧成功事实掩盖或覆盖。</summary>
    public CompiledPersistencePlan? ReadDeployed()
    {
        AcquireLease();
        var state = ReadState();
        if (state?.Pending is { } pending)
        {
            _ = ReadAsset(pending);
            throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_PENDING: unfinished candidate requires applied-history recovery.");
        }
        return state?.Successful is { } successful ? ReadAsset(successful).Plan : null;
    }

    /// <summary>读取首 DDL 前归档的完整恢复输入；它只约束身份与可验证步骤，不证明目标已全部落库。</summary>
    public CompiledPersistencePlan? ReadRecoveryCandidate()
    {
        AcquireLease();
        var state = ReadState();
        return (state?.Pending ?? state?.Successful) is { } digest ? ReadAsset(digest).Plan : null;
    }

    /// <summary>按本存储绑定的 catalog 验证当前或上一份成功候选，未确认的 raw 活动快照不可见。</summary>
    public bool IsSuccessfulDeployment(string snapshotId, string modulePlanHash, string persistencePlanDigest)
    {
        AcquireLease();
        var state = ReadState();
        foreach (var digest in new[] { state?.Successful, state?.PreviousSuccessful }.Where(static digest => digest is not null).Distinct(StringComparer.Ordinal))
        {
            var plan = ReadAsset(digest!).Plan;
            if (plan.SnapshotId == snapshotId && plan.ModulePlanHash == modulePlanHash && plan.PlanDigest == persistencePlanDigest) return true;
        }
        return false;
    }

    /// <summary>在 Host 第一次可能提交数据库变更前原子保存全部恢复资产及 pending 引用。</summary>
    public void PrepareCandidate(CanonicalModulePlan modules, CompiledPersistencePlan plan)
    {
        AcquireLease();
        if (plan.Provider != _provider || plan.ModulePlanHash != modules.PlanHash || plan.SnapshotId != modules.SnapshotId)
            throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_CANDIDATE_MISMATCH: deployment must belong to the same formal candidate.");
        var state = ReadState() ?? new DeploymentState(null, null);
        var previous = (state.Pending ?? state.Successful) is { } previousDigest ? ReadAsset(previousDigest) : null;
        if (state.Pending is not null && previous is not null) ValidateContinuation(previous.Plan, plan);
        var identities = (previous?.Modules ?? []).Where(module => plan.RetainedModuleIds.Contains(module.ModuleId, StringComparer.Ordinal))
            .Concat(modules.Modules.Select(module => new DeploymentModule(module.ModuleId, module.Version, module.Provenance.SourceKind)))
            .OrderBy(module => module.ModuleId, StringComparer.Ordinal).ToArray();
        var images = plan.Streams.Where(stream => stream.Migrations.Count > 0).Select(stream => new DeploymentImage(
            stream.ModuleId,
            stream.AssemblyImage?.CopyBytes() ?? throw MissingAsset(stream.ModuleId),
            stream.ManifestImage?.CopyBytes() ?? throw MissingAsset(stream.ModuleId))).ToArray();
        var asset = new DeploymentAsset(_targetIdentity, plan, identities, images);
        ValidateAsset(asset);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(asset, JsonOptions);
        var digest = Hash(bytes);
        WriteAtomic(digest + ".json", bytes, immutable: true);
        WriteAtomic("state.json", JsonSerializer.SerializeToUtf8Bytes(state with { Pending = digest }, JsonOptions));
    }

    // 恢复候选只能保持可信 owner 的不可变前缀；真实应用进度仍由 SQL 锁内的两账本裁决。
    private static void ValidateContinuation(CompiledPersistencePlan previous, CompiledPersistencePlan candidate)
    {
        foreach (var stream in previous.Streams)
        {
            var current = candidate.Streams.SingleOrDefault(item => item.ModuleId == stream.ModuleId);
            if (current is null || current.Provider != stream.Provider || current.IsPlatformStream != stream.IsPlatformStream
                || current.Migrations.Count < stream.Migrations.Count
                || !current.Migrations.Take(stream.Migrations.Count).SequenceEqual(stream.Migrations))
                throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_RECOVERY_FRONTIER_MISMATCH: " + stream.ModuleId);
        }
    }

    /// <summary>只在 migration、只读检查及完整活动提交成功之后发布成功部署引用。</summary>
    public void CompleteCandidate(CompiledPersistencePlan plan)
    {
        AcquireLease();
        var state = ReadState() ?? throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_PENDING_MISSING");
        var pending = state.Pending ?? throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_PENDING_MISSING");
        if (ReadAsset(pending).Plan.PlanDigest != plan.PlanDigest)
            throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_CANDIDATE_MISMATCH");
        var previousSuccessful = state.Successful == pending ? state.PreviousSuccessful : state.Successful;
        WriteAtomic("state.json", JsonSerializer.SerializeToUtf8Bytes(new DeploymentState(pending, null, previousSuccessful), JsonOptions));
        Dispose();
    }

    // 本机部署引用与恢复资产必须串行发布；原 SQL 全局迁移锁仍负责数据库事务写入。
    private void AcquireLease()
    {
        if (_lease is not null) return;
        Directory.CreateDirectory(_root);
        PathContainment.RejectReparsePoints(_root);
        _lease = new FileStream(Path.Combine(_root, "deployment.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>释放部署发布租约；失败时保留 pending 与不可变恢复资产。</summary>
    public void Dispose()
    {
        _lease?.Dispose();
        _lease = null;
    }

    // 状态只持有不可变资产摘要；丢失引用或资产均不得退回磁盘最新包。
    private DeploymentState? ReadState()
    {
        var path = Path.Combine(_root, "state.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<DeploymentState>(PathContainment.ReadContainedFile(_root, path), JsonOptions)
            ?? throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_STATE_INVALID") : null;
    }

    // 校验内容寻址、catalog/provider、模型规范输入及已编译 manifest/assembly 后才返回保留计划。
    private DeploymentAsset ReadAsset(string digest)
    {
        if (digest.Length != 64 || digest.Any(character => !char.IsAsciiHexDigitLower(character)))
            throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_ASSET_INVALID: invalid content address.");
        byte[] bytes;
        try { bytes = PathContainment.ReadContainedFile(_root, Path.Combine(_root, digest + ".json")); }
        catch (IOException exception) { throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_ASSET_MISSING", exception); }
        if (Hash(bytes) != digest) throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_ASSET_INVALID: content digest differs.");
        var asset = JsonSerializer.Deserialize<DeploymentAsset>(bytes, JsonOptions)
            ?? throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_ASSET_INVALID");
        ValidateAsset(asset);
        var images = asset.Images.ToDictionary(image => image.ModuleId, StringComparer.Ordinal);
        return asset with { Plan = asset.Plan with { Streams = asset.Plan.Streams.Select(stream => images.TryGetValue(stream.ModuleId, out var image)
            ? stream with { AssemblyImage = new VerifiedArtifactImage(image.Assembly), ManifestImage = new VerifiedArtifactImage(image.Manifest) } : stream).ToArray() } };
    }

    // 归档不是新模型真源：必须与正式编译的 canonical bytes、owner 和可信迁移输入完全相符。
    private void ValidateAsset(DeploymentAsset asset)
    {
        var plan = asset.Plan;
        if (asset.TargetIdentity != _targetIdentity || plan.Provider != _provider || Hash(plan.CanonicalBytes) != plan.PlanDigest)
            throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_ASSET_INVALID: candidate, provider or catalog differs.");
        using var canonical = JsonDocument.Parse(plan.CanonicalBytes);
        if (canonical.RootElement.GetProperty("models").GetRawText() != JsonSerializer.Serialize(plan.Models, JsonOptions)
            || canonical.RootElement.GetProperty("modulePlanHash").GetString() != plan.ModulePlanHash)
            throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_ASSET_INVALID: models differ from formal canonical input.");
        var owners = asset.Modules.Select(module => module.ModuleId).ToHashSet(StringComparer.Ordinal);
        if (plan.Models.Any(model => !owners.Contains(model.OwnerModuleId)))
            throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_ASSET_INVALID: model owner has no source identity.");
        var moduleStreamOwners = plan.Streams.Where(stream => !stream.IsPlatformStream).Select(stream => stream.ModuleId).ToHashSet(StringComparer.Ordinal);
        foreach (var stream in plan.Streams.Where(stream => stream.Migrations.Count > 0))
        {
            var image = asset.Images.SingleOrDefault(image => image.ModuleId == stream.ModuleId) ?? throw MissingAsset(stream.ModuleId);
            if (Hash(image.Assembly) != stream.BundleChecksum) throw MissingAsset(stream.ModuleId);
            var ownerModels = plan.Models.Where(model => stream.IsPlatformStream ? !moduleStreamOwners.Contains(model.OwnerModuleId) : model.OwnerModuleId == stream.ModuleId);
            if (PersistenceModelDigest.Compute(ownerModels) != stream.ModelDigest || stream.Migrations[^1].MigrationId != stream.ExpectedFrontier)
                throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_ASSET_INVALID: model or frontier differs from the verified stream.");
            var steps = MigrationManifestIntegrity.Validate(stream.ModuleId, plan.Provider,
                MigrationManifestIntegrity.Parse(image.Manifest), image.Assembly, stream.ManifestPath, stream.ModelDigest);
            if (steps.Count != stream.Migrations.Count || !steps.Zip(stream.Migrations).All(pair =>
                pair.First.Sequence == pair.Second.Sequence && pair.First.MigrationId == pair.Second.MigrationId
                && pair.First.PreviousContentDigest == pair.Second.PreviousContentDigest && pair.First.ContentDigest == pair.Second.ContentDigest
                && pair.First.FromModelDigest == pair.Second.FromModelDigest && pair.First.ToModelDigest == pair.Second.ToModelDigest && pair.First.Compatibility == pair.Second.Compatibility))
                throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_ASSET_INVALID: stream differs from verified manifest.");
        }
    }

    // 同目录 flush + rename，先发布不可变资产再发布引用；失败留下资产而不伪造成功事实。
    private void WriteAtomic(string name, byte[] bytes, bool immutable = false)
    {
        Directory.CreateDirectory(_root);
        PathContainment.RejectReparsePoints(_root);
        var path = Path.Combine(_root, name);
        if (immutable && File.Exists(path))
        {
            if (!PathContainment.ReadContainedFile(_root, path).AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("PERSISTENCE_DEPLOYMENT_ASSET_INVALID");
            return;
        }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { file.Write(bytes); file.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: !immutable);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // 所有持久化资产使用同一内容摘要算法，避免文件路径成为身份。
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    // 缺少模型演进的可信输入时保持稳定、关闭的失败信号。
    private static InvalidDataException MissingAsset(string owner) => new("PERSISTENCE_DEPLOYMENT_ASSET_MISSING: " + owner);

    private sealed record DeploymentState(string? Successful, string? Pending, string? PreviousSuccessful = null);
    private sealed record DeploymentModule(string ModuleId, string Version, string SourceKind);
    private sealed record DeploymentImage(string ModuleId, byte[] Assembly, byte[] Manifest);
    private sealed record DeploymentAsset(string TargetIdentity, CompiledPersistencePlan Plan, IReadOnlyList<DeploymentModule> Modules, IReadOnlyList<DeploymentImage> Images);
}
