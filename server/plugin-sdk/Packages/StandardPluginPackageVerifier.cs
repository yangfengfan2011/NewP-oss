using System.IO.Compression;
using System.Text.Json;
using ICP.Platform.Plugin.Contracts;

namespace ICP.Platform.Plugin.Sdk;

/// <summary>
/// `.npp` 独立验证输入。
/// </summary>
public sealed record StandardPluginVerifyRequest(
    string PackagePath,
    PlatformApiReleaseManifest ReleaseManifest);

/// <summary>
/// `.npp` 独立验证结果。
/// </summary>
public sealed record StandardPluginVerifyResult(
    bool IsValid,
    StandardPluginPackageManifest? Manifest,
    IReadOnlyList<PlatformApiDiagnostic> Diagnostics);

/// <summary>
/// 在隔离目录中验证 package schema、attestation、文件边界和完整性。
/// </summary>
public sealed class StandardPluginPackageVerifier
{
    private const string ChecksumsEntry = "checksums/sha256sums.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 验证 `.npp`；任何失败都不返回部分可激活 package。
    /// </summary>
    public StandardPluginVerifyResult Verify(StandardPluginVerifyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var packagePath = Path.GetFullPath(request.PackagePath);
        if (!File.Exists(packagePath))
        {
            return Failure(PlatformApiDiagnosticCodes.PackageSchemaUnsupported, "Package file does not exist.");
        }

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var entries = archive.Entries.Where(static entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
            if (entries.Any(static entry => !IsSafeEntry(entry.FullName))
                || entries.GroupBy(static entry => entry.FullName, StringComparer.OrdinalIgnoreCase).Any(static group => group.Count() > 1))
            {
                return Failure(PlatformApiDiagnosticCodes.PackageSchemaUnsupported, "Package contains an unsafe or duplicate path.");
            }

            var runtimeDiagnostic = FindForbiddenRuntimeEntry(entries);
            if (runtimeDiagnostic is not null)
            {
                return new StandardPluginVerifyResult(false, null, [runtimeDiagnostic]);
            }

            var byPath = entries.ToDictionary(static entry => entry.FullName, StringComparer.Ordinal);
            if (!byPath.TryGetValue("plugin.json", out var manifestEntry)
                || !byPath.TryGetValue("attestation/platform-api.json", out var attestationEntry)
                || !byPath.TryGetValue(ChecksumsEntry, out var checksumsEntry))
            {
                return Failure(PlatformApiDiagnosticCodes.PackageSchemaUnsupported, "Package is missing a required manifest.");
            }

            var manifest = ReadJson<StandardPluginPackageManifest>(manifestEntry);
            var attestation = ReadJson<PlatformApiAttestation>(attestationEntry);
            if (!AttestationEquals(manifest.Compatibility, attestation)
                || !string.Equals(attestation.BuiltWith, request.ReleaseManifest.ReleaseVersion, StringComparison.Ordinal)
                || !string.Equals(attestation.ReleaseDigest, request.ReleaseManifest.ReleaseDigest, StringComparison.Ordinal))
            {
                return Failure(
                    PlatformApiDiagnosticCodes.PlatformApiAttestationMismatch,
                    "Package attestation does not match the supplied Platform API release.",
                    manifest);
            }

            var releaseDiagnostics = new PlatformApiReleaseManifestValidator().Validate(request.ReleaseManifest);
            if (releaseDiagnostics.Count > 0)
            {
                return new StandardPluginVerifyResult(false, null, releaseDiagnostics);
            }

            var checksumManifest = ReadJson<StandardPluginChecksumManifest>(checksumsEntry);
            var contentEntries = entries
                .Where(static entry => !string.Equals(entry.FullName, ChecksumsEntry, StringComparison.Ordinal))
                .OrderBy(static entry => entry.FullName, StringComparer.Ordinal)
                .ToArray();
            if (checksumManifest.Files.Count != contentEntries.Length
                || contentEntries.Any(entry =>
                    !checksumManifest.Files.TryGetValue(entry.FullName, out var expected)
                    || !string.Equals(expected, StandardPluginPackageBuilder.Sha256(ReadBytes(entry)), StringComparison.Ordinal)))
            {
                return Failure(
                    PlatformApiDiagnosticCodes.PlatformApiAttestationMismatch,
                    "Package checksum manifest does not match the package bytes.",
                    manifest);
            }

            var requiredArtifacts = new[]
            {
                manifest.Artifacts.Blueprint,
                manifest.Artifacts.StandardProjection,
                manifest.Artifacts.Explain
            };
            if (manifest.SchemaVersion != 1
                || !string.Equals(manifest.Profile, PlatformApiReleaseIdentity.StandardDataProfile, StringComparison.Ordinal)
                || requiredArtifacts.Any(path => !byPath.ContainsKey(path)))
            {
                return Failure(PlatformApiDiagnosticCodes.PackageSchemaUnsupported, "Package manifest is unsupported or incomplete.", manifest);
            }

            return new StandardPluginVerifyResult(true, manifest, Array.Empty<PlatformApiDiagnostic>());
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            return Failure(PlatformApiDiagnosticCodes.PackageSchemaUnsupported, exception.Message);
        }
    }

    private static PlatformApiDiagnostic? FindForbiddenRuntimeEntry(IReadOnlyList<ZipArchiveEntry> entries)
    {
        var backend = entries.FirstOrDefault(static entry =>
            entry.FullName.StartsWith("backend/", StringComparison.OrdinalIgnoreCase)
            || entry.FullName.EndsWith("ICP.Platform.Plugin.Contracts.dll", StringComparison.OrdinalIgnoreCase)
            || entry.FullName.EndsWith("ICP.Platform.Plugin.Sdk.dll", StringComparison.OrdinalIgnoreCase)
            || entry.FullName.EndsWith("ICP.Platform.Plugin.Tool.dll", StringComparison.OrdinalIgnoreCase)
            || entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                && !entry.FullName.StartsWith("migrations/sqlserver/", StringComparison.OrdinalIgnoreCase));
        if (backend is not null)
        {
            return new PlatformApiDiagnostic(
                PlatformApiDiagnosticCodes.StandardProfileBackendCodeForbidden,
                "verify",
                "standard-data.v1 contains a forbidden backend/runtime assembly.",
                SourcePath: backend.FullName);
        }

        var frontend = entries.FirstOrDefault(static entry =>
            entry.FullName.StartsWith("frontend/", StringComparison.OrdinalIgnoreCase)
            || entry.FullName.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || entry.FullName.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase)
            || entry.FullName.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase)
            || entry.FullName.EndsWith(".cjs", StringComparison.OrdinalIgnoreCase)
            || entry.FullName.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase));
        return frontend is null
            ? null
            : new PlatformApiDiagnostic(
                PlatformApiDiagnosticCodes.StandardProfileFrontendCodeForbidden,
                "verify",
                "standard-data.v1 contains executable frontend code.",
                SourcePath: frontend.FullName);
    }

    private static bool IsSafeEntry(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\\'))
        {
            return false;
        }

        return !path.Split('/').Any(static segment => segment is "" or "." or "..");
    }

    private static bool AttestationEquals(PlatformApiAttestation left, PlatformApiAttestation right) =>
        string.Equals(left.BuiltWith, right.BuiltWith, StringComparison.Ordinal)
        && string.Equals(left.MinInclusive, right.MinInclusive, StringComparison.Ordinal)
        && string.Equals(left.MaxExclusive, right.MaxExclusive, StringComparison.Ordinal)
        && string.Equals(left.ReleaseDigest, right.ReleaseDigest, StringComparison.Ordinal)
        && string.Equals(left.Profile, right.Profile, StringComparison.Ordinal)
        && string.Equals(left.PersistenceContract, right.PersistenceContract, StringComparison.Ordinal)
        && left.PersistenceProviders.SequenceEqual(right.PersistenceProviders, StringComparer.Ordinal);

    private static T ReadJson<T>(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new JsonException($"Package entry '{entry.FullName}' is empty.");
    }

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static StandardPluginVerifyResult Failure(
        string code,
        string message,
        StandardPluginPackageManifest? manifest = null) =>
        new(
            false,
            manifest,
            [new PlatformApiDiagnostic(code, "verify", message, ModuleId: manifest?.ModuleId, Version: manifest?.Version, Profile: manifest?.Profile)]);
}
