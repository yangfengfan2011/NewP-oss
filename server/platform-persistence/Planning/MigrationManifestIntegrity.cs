using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ICP.Platform.Persistence.Planning;

/// <summary>
/// migration bundle 的可提交清单。
/// </summary>
public sealed record MigrationManifest(
    string SchemaVersion,
    string Provider,
    string ModuleId,
    string ModelDigest,
    string BundleChecksum,
    IReadOnlyList<MigrationManifestStep> Migrations);

/// <summary>
/// migration 清单中的单个不可变链节点。
/// </summary>
public sealed record MigrationManifestStep(
    string Sequence,
    string MigrationId,
    string? PreviousContentDigest,
    string ContentDigest,
    string FromModelDigest,
    string ToModelDigest,
    string Compatibility);

/// <summary>
/// migration manifest 静态完整性校验的稳定失败。
/// </summary>
public sealed class MigrationManifestValidationException : InvalidOperationException
{
    /// <summary>
    /// 创建携带稳定失败码与可选 migration id 的校验异常。
    /// </summary>
    public MigrationManifestValidationException(string code, string message, string? canonicalKey = null)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        CanonicalKey = canonicalKey;
    }

    public string Code { get; }

    public string? CanonicalKey { get; }
}

/// <summary>
/// 统一拥有 migration manifest 的解析、摘要与无 I/O 完整性规则。
/// </summary>
public static partial class MigrationManifestIntegrity
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions CanonicalManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// 从 UTF-8 JSON 解析 migration manifest，不读取或修改任何外部状态。
    /// </summary>
    public static MigrationManifest Parse(ReadOnlySpan<byte> utf8Json) =>
        JsonSerializer.Deserialize<MigrationManifest>(utf8Json, ManifestJsonOptions)
        ?? throw new InvalidOperationException("Migration manifest is empty.");

    /// <summary>
    /// 把 manifest 写成属性顺序固定、两空格缩进、CRLF 结尾的 UTF-8 JSON。
    /// </summary>
    public static byte[] SerializeCanonical(MigrationManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var json = JsonSerializer.Serialize(manifest, CanonicalManifestJsonOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "\r\n", StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(json + "\r\n");
    }

    /// <summary>
    /// 先把源码换行规范化为 LF，再计算小写 SHA-256 内容摘要。
    /// </summary>
    public static string ComputeCanonicalContentDigest(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var canonicalContent = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return ComputeBundleChecksum(Encoding.UTF8.GetBytes(canonicalContent));
    }

    /// <summary>
    /// 对确定的 bundle 字节计算小写 SHA-256 checksum。
    /// </summary>
    public static string ComputeBundleChecksum(ReadOnlySpan<byte> bundleBytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bundleBytes));

    /// <summary>
    /// 校验 owner、provider、bundle、模型摘要及 migration 线性链，并返回按 sequence 排序的节点。
    /// </summary>
    public static IReadOnlyList<MigrationManifestStep> Validate(
        string moduleId,
        PersistenceProvider provider,
        MigrationManifest manifest,
        ReadOnlySpan<byte> assemblyBytes,
        string manifestPath,
        string modelDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDigest);

        ValidateOwner(moduleId, provider, manifest, manifestPath);

        var checksum = ComputeBundleChecksum(assemblyBytes);
        if (!string.Equals(checksum, manifest.BundleChecksum, StringComparison.Ordinal))
        {
            throw new MigrationManifestValidationException(
                "PERSISTENCE_BUNDLE_CHECKSUM_MISMATCH",
                $"Migration bundle checksum mismatch for '{moduleId}'.");
        }

        if (!string.Equals(manifest.ModelDigest, modelDigest, StringComparison.Ordinal))
        {
            throw new MigrationManifestValidationException(
                "PERSISTENCE_MODEL_MISMATCH",
                $"Migration manifest model digest does not match compiled model '{moduleId}'.");
        }

        var migrations = ValidateLinearChain(moduleId, manifest);
        if (!string.Equals(migrations[^1].ToModelDigest, modelDigest, StringComparison.Ordinal))
        {
            throw new MigrationManifestValidationException(
                "PERSISTENCE_MODEL_MISMATCH",
                $"Migration frontier does not target the compiled model digest for '{moduleId}'.");
        }

        return migrations;
    }

    /// <summary>
    /// 校验 finalize 输入中已经发布的 owner 与不可变线性前缀，不要求新 bundle 或当前模型已写回。
    /// </summary>
    public static IReadOnlyList<MigrationManifestStep> ValidateAuthoringPrefix(
        string moduleId,
        PersistenceProvider provider,
        MigrationManifest manifest,
        string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        ValidateOwner(moduleId, provider, manifest, manifestPath);
        // 尚未发布的新 stream 可从空前缀开始；runtime Validate 仍要求完整非空链。
        return manifest.Migrations.Count == 0 ? [] : ValidateLinearChain(moduleId, manifest);
    }

    // owner/provider 在 runtime 与 authoring 两条入口中保持同一失败语义。
    private static void ValidateOwner(
        string moduleId,
        PersistenceProvider provider,
        MigrationManifest manifest,
        string manifestPath)
    {
        if (!string.Equals(manifest.ModuleId, moduleId, StringComparison.Ordinal)
            || !string.Equals(manifest.Provider, provider.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new MigrationManifestValidationException(
                "PERSISTENCE_MIGRATION_OWNER_MISMATCH",
                $"Migration manifest '{manifestPath}' does not match {moduleId}/{provider}.");
        }
    }

    // 线性链校验不读取 bundle/current model，供 runtime 完整校验与 finalize 前缀校验复用。
    private static MigrationManifestStep[] ValidateLinearChain(
        string moduleId,
        MigrationManifest manifest)
    {
        var migrations = manifest.Migrations
            .OrderBy(static migration => migration.Sequence, StringComparer.Ordinal)
            .ToArray();
        if (migrations.Length == 0)
        {
            throw new MigrationManifestValidationException(
                "PERSISTENCE_MIGRATION_REQUIRED",
                $"Migration stream '{moduleId}' is empty.");
        }

        var duplicateMigrationId = migrations
            .GroupBy(static migration => migration.MigrationId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicateMigrationId is not null)
        {
            throw new MigrationManifestValidationException(
                PersistenceMigrationFailureCodes.MigrationIdConflict,
                $"Migration id '{duplicateMigrationId}' is repeated in module stream '{moduleId}'.",
                duplicateMigrationId);
        }

        var startsFromZeroDigest = string.Equals(
            migrations[0].FromModelDigest,
            new string('0', 64),
            StringComparison.Ordinal);
        if (!startsFromZeroDigest)
        {
            throw new MigrationManifestValidationException(
                "PERSISTENCE_MIGRATION_CHAIN_INVALID",
                $"Migration stream '{moduleId}' must start from the zero model digest.");
        }

        string? previousDigest = null;
        for (var index = 0; index < migrations.Length; index++)
        {
            var migration = migrations[index];
            var expectedSequence = (index + 1).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            if (!string.Equals(migration.Sequence, expectedSequence, StringComparison.Ordinal)
                || !string.Equals(migration.PreviousContentDigest, previousDigest, StringComparison.Ordinal)
                || (index > 0
                    && !string.Equals(
                        migration.FromModelDigest,
                        migrations[index - 1].ToModelDigest,
                        StringComparison.Ordinal))
                || !DigestPattern().IsMatch(migration.ContentDigest)
                || !DigestPattern().IsMatch(migration.FromModelDigest)
                || !DigestPattern().IsMatch(migration.ToModelDigest)
                || migration.Compatibility is not (
                    "safe-expand"
                    or "data-backfill"
                    or "contract"
                    or "backup-restore-only"))
            {
                throw new MigrationManifestValidationException(
                    "PERSISTENCE_MIGRATION_CHAIN_INVALID",
                    $"Migration stream '{moduleId}' is not linear and immutable at sequence '{migration.Sequence}'.");
            }

            previousDigest = migration.ContentDigest;
        }

        return migrations;
    }

    /// <summary>
    /// 确认 manifest 与 bundle migration 类型集合精确匹配。
    /// </summary>
    public static void ValidateMigrationTypes(
        IEnumerable<string> declaredMigrationIds,
        IEnumerable<string> availableMigrationIds)
    {
        ArgumentNullException.ThrowIfNull(declaredMigrationIds);
        ArgumentNullException.ThrowIfNull(availableMigrationIds);

        var declared = declaredMigrationIds.ToArray();
        var available = availableMigrationIds.ToHashSet(StringComparer.Ordinal);
        var missingMigrationId = declared.FirstOrDefault(id => !available.Contains(id));
        if (missingMigrationId is not null)
        {
            throw new MigrationManifestValidationException(
                PersistenceMigrationFailureCodes.MigrationMissing,
                $"Migration type '{missingMigrationId}' is missing from the bundle.",
                missingMigrationId);
        }

        var declaredSet = declared.ToHashSet(StringComparer.Ordinal);
        var undeclaredMigrationId = available
            .Where(id => !declaredSet.Contains(id))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        if (undeclaredMigrationId is not null)
        {
            throw new MigrationManifestValidationException(
                PersistenceMigrationFailureCodes.MigrationUndeclared,
                $"Migration type '{undeclaredMigrationId}' is not declared in the manifest.",
                undeclaredMigrationId);
        }
    }

    // manifest 摘要固定为小写 64 位十六进制，避免跨入口接受不同表示。
    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();
}
