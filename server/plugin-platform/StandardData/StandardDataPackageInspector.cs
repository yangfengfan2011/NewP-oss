using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.Plugin.Contracts;

namespace ICP.Platform.PluginPlatform.StandardData;

public sealed record StandardDataPackageInspectionResult(
    StandardPluginPackageManifest? Manifest,
    StandardPluginBlueprint? Blueprint,
    StandardPluginProjection? Projection,
    IReadOnlyList<PlatformApiDiagnostic> Diagnostics)
{
    public bool IsSuccess =>
        Manifest is not null
        && Blueprint is not null
        && Projection is not null
        && Diagnostics.Count == 0;
}

/// <summary>
/// Host 启动前使用 public Contracts 与 BCL 验证 standard-data.v1 包；不加载插件代码，也不连接数据库。
/// </summary>
public sealed class StandardDataPackageInspector
{
    private const string ManifestPath = "plugin.json";
    private const string AttestationPath = "attestation/platform-api.json";
    private static readonly HashSet<string> TransportFiles =
        new(["package.zip", "upload-record.json"], StringComparer.OrdinalIgnoreCase);
    private readonly PlatformApiHostCatalog _hostCatalog;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public StandardDataPackageInspector(PlatformApiHostCatalog hostCatalog)
    {
        _hostCatalog = hostCatalog;
    }

    public StandardDataPackageInspectionResult Inspect(string packageRoot)
    {
        try
        {
            var resolvedRoot = Path.GetFullPath(packageRoot);
            if (!Directory.Exists(resolvedRoot))
            {
                return Failure(
                    PlatformApiDiagnosticCodes.PackageSchemaUnsupported,
                    $"Package root does not exist: {resolvedRoot}");
            }

            var relativeFiles = EnumeratePackageFiles(resolvedRoot);
            var pathCollision = relativeFiles
                .GroupBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(static group => group.Count() > 1);
            if (pathCollision is not null)
            {
                return Failure(
                    PlatformApiDiagnosticCodes.PackageSchemaUnsupported,
                    $"Package contains a case-insensitive path collision: {string.Join(", ", pathCollision)}");
            }

            if (relativeFiles.Any(static path =>
                    path.StartsWith("backend/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("frontend/", StringComparison.OrdinalIgnoreCase)))
            {
                return Failure(
                    PlatformApiDiagnosticCodes.PackageSchemaUnsupported,
                    "standard-data.v1 package cannot contain backend or frontend runtime entries.");
            }

            var manifest = ReadJson<StandardPluginPackageManifest>(resolvedRoot, ManifestPath);
            if (manifest.SchemaVersion != 1)
            {
                return Failure(
                    PlatformApiDiagnosticCodes.PackageSchemaUnsupported,
                    $"Package schema version '{manifest.SchemaVersion}' is unsupported.",
                    manifest);
            }

            var requiredFiles = new[]
            {
                ManifestPath,
                AttestationPath,
                manifest.Artifacts.Blueprint,
                manifest.Artifacts.StandardProjection,
                manifest.Artifacts.Explain,
                manifest.Checksums.Sha256Manifest
            }
                .Concat(manifest.Persistence.Models)
                .Concat(manifest.Persistence.Migrations.Values.SelectMany(static bundle =>
                    new[] { bundle.Assembly, bundle.Manifest }))
                .ToArray();
            foreach (var path in requiredFiles)
            {
                ResolveContainedFile(resolvedRoot, path);
            }

            var checksumManifest = ReadJson<StandardPluginChecksumManifest>(
                resolvedRoot,
                manifest.Checksums.Sha256Manifest);
            var checksummedFiles = relativeFiles
                .Where(path => !TransportFiles.Contains(path))
                .Where(path => !string.Equals(
                    path,
                    NormalizePath(manifest.Checksums.Sha256Manifest),
                    StringComparison.Ordinal))
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray();
            if (!string.Equals(checksumManifest.SchemaVersion, "1.0", StringComparison.Ordinal)
                || checksumManifest.Files.Count != checksummedFiles.Length
                || checksummedFiles.Any(path =>
                    !checksumManifest.Files.TryGetValue(path, out var expected)
                    || !string.Equals(
                        expected,
                        Sha256(File.ReadAllBytes(ResolveContainedFile(resolvedRoot, path))),
                        StringComparison.Ordinal)))
            {
                return Failure(
                    PlatformApiDiagnosticCodes.PlatformApiAttestationMismatch,
                    "Package checksum manifest does not match the installed package bytes.",
                    manifest);
            }

            var attestation = ReadJson<PlatformApiAttestation>(resolvedRoot, AttestationPath);
            if (!AttestationEquals(manifest.Compatibility, attestation))
            {
                return Failure(
                    PlatformApiDiagnosticCodes.PlatformApiAttestationMismatch,
                    "Package manifest and release attestation do not match.",
                    manifest);
            }

            var compatibilityDiagnostic = ValidateCompatibility(attestation, manifest);
            if (compatibilityDiagnostic is not null)
            {
                return new StandardDataPackageInspectionResult(
                    null,
                    null,
                    null,
                    [compatibilityDiagnostic]);
            }

            var blueprint = ReadJson<StandardPluginBlueprint>(resolvedRoot, manifest.Artifacts.Blueprint);
            var projection = ReadJson<StandardPluginProjection>(resolvedRoot, manifest.Artifacts.StandardProjection);
            if (!string.Equals(blueprint.Profile, manifest.Profile, StringComparison.Ordinal)
                || !string.Equals(blueprint.ModuleId, manifest.ModuleId, StringComparison.Ordinal)
                || !string.Equals(blueprint.Version, manifest.Version, StringComparison.Ordinal)
                || !string.Equals(blueprint.DisplayName, manifest.DisplayName, StringComparison.Ordinal)
                || !string.Equals(blueprint.Compatibility.PlatformApi.MinInclusive, attestation.MinInclusive, StringComparison.Ordinal)
                || !string.Equals(blueprint.Compatibility.PlatformApi.MaxExclusive, attestation.MaxExclusive, StringComparison.Ordinal)
                || !string.Equals(blueprint.Compatibility.Persistence.Contract, attestation.PersistenceContract, StringComparison.Ordinal)
                || !blueprint.Compatibility.Persistence.Providers.SequenceEqual(attestation.PersistenceProviders, StringComparer.Ordinal)
                || !PersistenceEquals(blueprint.Persistence, manifest.Persistence)
                || projection.Resources.Count != blueprint.StandardData.Resources.Count
                || projection.Resources.Select(static item => item.ResourceKey)
                    .Except(blueprint.StandardData.Resources.Select(static item => item.Key), StringComparer.Ordinal)
                    .Any())
            {
                return Failure(
                    PlatformApiDiagnosticCodes.PlatformApiAttestationMismatch,
                    "Package manifest, Blueprint and standard projection do not describe the same package.",
                    manifest);
            }

            return new StandardDataPackageInspectionResult(
                manifest,
                blueprint,
                projection,
                Array.Empty<PlatformApiDiagnostic>());
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or JsonException
                                           or InvalidDataException
                                           or ArgumentException
                                           or NotSupportedException)
        {
            return Failure(
                PlatformApiDiagnosticCodes.PackageSchemaUnsupported,
                exception.Message);
        }
    }

    private PlatformApiDiagnostic? ValidateCompatibility(
        PlatformApiAttestation attestation,
        StandardPluginPackageManifest manifest)
    {
        if (!string.Equals(manifest.Profile, PlatformApiReleaseIdentity.StandardDataProfile, StringComparison.Ordinal)
            || !_hostCatalog.SupportedProfiles.Contains(attestation.Profile, StringComparer.Ordinal))
        {
            return Diagnostic(
                PlatformApiDiagnosticCodes.PluginProfileUnsupported,
                $"Plugin profile '{attestation.Profile}' is not supported.",
                manifest);
        }

        if (!string.Equals(attestation.PersistenceContract, PlatformApiReleaseIdentity.PersistenceContract, StringComparison.Ordinal)
            || attestation.PersistenceProviders.Count != 1
            || !string.Equals(attestation.PersistenceProviders[0], PlatformApiReleaseIdentity.SqlServerProvider, StringComparison.Ordinal))
        {
            return Diagnostic(
                PlatformApiDiagnosticCodes.PersistenceContractUnsupported,
                "standard-data.v1 requires the SQL Server ef-migrations.v1 contract.",
                manifest);
        }

        if (!Version.TryParse(attestation.BuiltWith, out var builtWith)
            || !Version.TryParse(attestation.MinInclusive, out var minInclusive)
            || !Version.TryParse(attestation.MaxExclusive, out var maxExclusive)
            || !Version.TryParse(_hostCatalog.CurrentRelease, out var current)
            || builtWith.Major != current.Major
            || builtWith > current
            || current < minInclusive
            || current >= maxExclusive)
        {
            return Diagnostic(
                PlatformApiDiagnosticCodes.PlatformApiIncompatible,
                $"Plugin built with '{attestation.BuiltWith}' is incompatible with Host release '{_hostCatalog.CurrentRelease}'.",
                manifest);
        }

        var release = _hostCatalog.Releases.SingleOrDefault(item =>
            string.Equals(item.Version, attestation.BuiltWith, StringComparison.Ordinal));
        if (release is null)
        {
            return Diagnostic(
                PlatformApiDiagnosticCodes.PlatformApiIncompatible,
                $"Host release catalog does not contain '{attestation.BuiltWith}'.",
                manifest);
        }

        return string.Equals(release.ReleaseDigest, attestation.ReleaseDigest, StringComparison.Ordinal)
            ? null
            : Diagnostic(
                PlatformApiDiagnosticCodes.PlatformApiAttestationMismatch,
                "Plugin release digest does not match the Host release catalog.",
                manifest);
    }

    private T ReadJson<T>(string root, string relativePath)
    {
        var path = ResolveContainedFile(root, relativePath);
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), _jsonOptions)
            ?? throw new JsonException($"Package file '{relativePath}' deserialized to null.");
    }

    private static IReadOnlyList<string> EnumeratePackageFiles(string root)
    {
        var rootDirectory = new DirectoryInfo(root);
        if ((rootDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Package root cannot be a reparse point.");
        }

        var files = new List<string>();
        foreach (var directory in rootDirectory.EnumerateDirectories("*", SearchOption.AllDirectories))
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Package directory cannot be a reparse point: {directory.FullName}");
            }
        }

        foreach (var file in rootDirectory.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Package file cannot be a reparse point: {file.FullName}");
            }

            files.Add(NormalizePath(Path.GetRelativePath(root, file.FullName)));
        }

        return files;
    }

    private static string ResolveContainedFile(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"Package path is missing or unsafe: {relativePath}");
        }

        var resolvedRoot = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(resolvedRoot, relativePath));
        if (!path.StartsWith(resolvedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path))
        {
            throw new InvalidDataException($"Package file is missing or unsafe: {relativePath}");
        }

        return path;
    }

    private static bool AttestationEquals(PlatformApiAttestation left, PlatformApiAttestation right) =>
        string.Equals(left.BuiltWith, right.BuiltWith, StringComparison.Ordinal)
        && string.Equals(left.MinInclusive, right.MinInclusive, StringComparison.Ordinal)
        && string.Equals(left.MaxExclusive, right.MaxExclusive, StringComparison.Ordinal)
        && string.Equals(left.ReleaseDigest, right.ReleaseDigest, StringComparison.Ordinal)
        && string.Equals(left.Profile, right.Profile, StringComparison.Ordinal)
        && string.Equals(left.PersistenceContract, right.PersistenceContract, StringComparison.Ordinal)
        && left.PersistenceProviders.SequenceEqual(right.PersistenceProviders, StringComparer.Ordinal);

    private static bool PersistenceEquals(
        StandardPersistenceBlueprint left,
        StandardPersistenceBlueprint right)
    {
        return left.Models.SequenceEqual(right.Models, StringComparer.Ordinal)
            && left.Migrations.Count == right.Migrations.Count
            && left.Migrations.All(item =>
                right.Migrations.TryGetValue(item.Key, out var other)
                && string.Equals(item.Value.Assembly, other.Assembly, StringComparison.Ordinal)
                && string.Equals(item.Value.Manifest, other.Manifest, StringComparison.Ordinal));
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static string Sha256(byte[] bytes) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static StandardDataPackageInspectionResult Failure(
        string code,
        string message,
        StandardPluginPackageManifest? manifest = null) =>
        new(
            null,
            null,
            null,
            [Diagnostic(code, message, manifest)]);

    private static PlatformApiDiagnostic Diagnostic(
        string code,
        string message,
        StandardPluginPackageManifest? manifest) =>
        new(
            code,
            "activation-preflight",
            message,
            ModuleId: manifest?.ModuleId,
            Version: manifest?.Version,
            Profile: manifest?.Profile,
            BuiltWith: manifest?.Compatibility.BuiltWith,
            SupportedRelease: PlatformApiReleaseIdentity.Version);
}
