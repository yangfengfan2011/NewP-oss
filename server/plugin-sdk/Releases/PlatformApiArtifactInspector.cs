using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using ICP.Platform.Plugin.Contracts;

namespace ICP.Platform.Plugin.Sdk;

/// <summary>
/// 实际发布 archive 的身份与内容 hash 检查结果。
/// </summary>
public sealed record PlatformApiArtifactInspectionResult(
    PlatformApiArtifactDescriptor? Artifact,
    IReadOnlyList<PlatformApiDiagnostic> Diagnostics)
{
    public bool IsSuccess => Artifact is not null && Diagnostics.Count == 0;
}

/// <summary>
/// Contracts 与 frontend SDK 中 canonical schema 的交叉检查结果。
/// </summary>
public sealed record PlatformApiSchemaInspectionResult(
    IReadOnlyDictionary<string, string>? Schemas,
    IReadOnlyList<PlatformApiDiagnostic> Diagnostics)
{
    public bool IsSuccess => Schemas is not null && Diagnostics.Count == 0;
}

/// <summary>
/// 从 nupkg/npm tgz 内部读取真实 package identity 与共享 release identity。
/// </summary>
public sealed class PlatformApiArtifactInspector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// 检查指定类型的 archive；文件名不参与 package identity 判定。
    /// </summary>
    public PlatformApiArtifactInspectionResult Inspect(string kind, string artifactPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
        var fullPath = Path.GetFullPath(artifactPath);
        if (!File.Exists(fullPath))
        {
            return Failure(kind, fullPath, "Release artifact does not exist.");
        }

        try
        {
            string packageId;
            string version;
            ReleaseIdentity identity;
            if (string.Equals(kind, PlatformApiArtifactKinds.FrontendSdk, StringComparison.Ordinal))
            {
                (packageId, version, identity) = ReadNpm(fullPath);
            }
            else
            {
                var nuget = ReadNuGet(fullPath);
                packageId = nuget.PackageId;
                version = nuget.Version;
                identity = nuget.Identity;
                if (string.Equals(kind, PlatformApiArtifactKinds.BackendSdk, StringComparison.Ordinal)
                    && !string.Equals(nuget.ContractsDependencyVersion, $"[{identity.Version}]", StringComparison.Ordinal))
                {
                    return Failure(
                        kind,
                        fullPath,
                        $"Backend SDK must depend on ICP.Platform.Plugin.Contracts with exact version '[{identity.Version}]'.");
                }
            }
            var expectedId = PlatformApiArtifactKinds.PackageIdFor(kind);
            if (!string.Equals(packageId, expectedId, StringComparison.Ordinal)
                || !string.Equals(version, identity.Version, StringComparison.Ordinal)
                || !string.Equals(identity.SchemaVersion, "1.0", StringComparison.Ordinal)
                || !string.Equals(identity.Profile, PlatformApiReleaseIdentity.StandardDataProfile, StringComparison.Ordinal)
                || !string.Equals(identity.PersistenceContract, PlatformApiReleaseIdentity.PersistenceContract, StringComparison.Ordinal))
            {
                return Failure(kind, fullPath, "Archive package identity does not match its embedded Platform API identity.");
            }

            return new PlatformApiArtifactInspectionResult(
                new PlatformApiArtifactDescriptor(
                    kind,
                    packageId,
                    version,
                    identity.ReleaseDigest,
                    fullPath,
                    StandardPluginPackageBuilder.Sha256(File.ReadAllBytes(fullPath))),
                Array.Empty<PlatformApiDiagnostic>());
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or System.Xml.XmlException)
        {
            return Failure(kind, fullPath, exception.Message);
        }
    }

    /// <summary>
    /// 比较 Contracts nupkg 与 frontend SDK tgz 中的 canonical schema bytes。
    /// </summary>
    public PlatformApiSchemaInspectionResult InspectCanonicalSchemas(
        string contractsPath,
        string frontendSdkPath)
    {
        try
        {
            var contracts = ReadNuGetSchemas(Path.GetFullPath(contractsPath));
            var frontend = ReadNpmSchemas(Path.GetFullPath(frontendSdkPath));
            if (contracts.Count == 0
                || contracts.Count != frontend.Count
                || contracts.Any(item => !frontend.TryGetValue(item.Key, out var hash)
                    || !string.Equals(item.Value, hash, StringComparison.Ordinal)))
            {
                return SchemaFailure("Contracts and frontend SDK canonical schemas differ.");
            }

            return new PlatformApiSchemaInspectionResult(
                new SortedDictionary<string, string>(contracts, StringComparer.Ordinal),
                Array.Empty<PlatformApiDiagnostic>());
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return SchemaFailure(exception.Message);
        }
    }

    private static NuGetArtifactIdentity ReadNuGet(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var nuspec = archive.Entries.SingleOrDefault(static entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("NuGet artifact is missing its nuspec.");
        var identityEntries = archive.Entries
            .Where(static entry => entry.FullName.EndsWith("platform-api-release.identity.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (identityEntries.Length == 0)
        {
            throw new InvalidDataException("NuGet artifact is missing the Platform API identity.");
        }
        using var nuspecStream = nuspec.Open();
        var document = XDocument.Load(nuspecStream);
        var packageId = document.Descendants().Single(element => element.Name.LocalName == "id").Value;
        var version = document.Descendants().Single(element => element.Name.LocalName == "version").Value;
        var contractsDependencyVersion = document.Descendants()
            .Where(static element => element.Name.LocalName == "dependency")
            .Where(static element => string.Equals(
                element.Attribute("id")?.Value,
                "ICP.Platform.Plugin.Contracts",
                StringComparison.Ordinal))
            .Select(static element => element.Attribute("version")?.Value)
            .SingleOrDefault();
        var identities = identityEntries
            .Select(static entry => ReadIdentity(entry.Open()))
            .Distinct()
            .ToArray();
        if (identities.Length != 1)
        {
            throw new InvalidDataException("NuGet artifact contains conflicting Platform API identities.");
        }

        return new NuGetArtifactIdentity(packageId, version, identities[0], contractsDependencyVersion);
    }

    private static (string PackageId, string Version, ReleaseIdentity Identity) ReadNpm(string path)
    {
        string? packageJson = null;
        string? identityJson = null;
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            if (entry.DataStream is null)
            {
                continue;
            }

            if (string.Equals(entry.Name, "package/package.json", StringComparison.Ordinal))
            {
                packageJson = ReadText(entry.DataStream);
            }
            else if (string.Equals(entry.Name, "package/platform-api-release.identity.json", StringComparison.Ordinal))
            {
                identityJson = ReadText(entry.DataStream);
            }
        }

        if (packageJson is null || identityJson is null)
        {
            throw new InvalidDataException("npm artifact is missing package.json or Platform API identity.");
        }

        using var package = JsonDocument.Parse(packageJson);
        var packageId = package.RootElement.GetProperty("name").GetString()
            ?? throw new JsonException("npm package name is empty.");
        var version = package.RootElement.GetProperty("version").GetString()
            ?? throw new JsonException("npm package version is empty.");
        return (packageId, version, JsonSerializer.Deserialize<ReleaseIdentity>(identityJson, JsonOptions)
            ?? throw new JsonException("npm Platform API identity is empty."));
    }

    private static Dictionary<string, string> ReadNuGetSchemas(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return archive.Entries
            .Where(static entry => entry.FullName.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                static entry => Path.GetFileName(entry.FullName),
                static entry => StandardPluginPackageBuilder.Sha256(ReadBytes(entry.Open())),
                StringComparer.Ordinal);
    }

    private static Dictionary<string, string> ReadNpmSchemas(string path)
    {
        var schemas = new Dictionary<string, string>(StringComparer.Ordinal);
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            if (entry.DataStream is null
                || !entry.Name.StartsWith("package/schemas/", StringComparison.Ordinal)
                || !entry.Name.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            schemas.Add(
                Path.GetFileName(entry.Name),
                StandardPluginPackageBuilder.Sha256(ReadBytes(entry.DataStream)));
        }

        return schemas;
    }

    private static ReleaseIdentity ReadIdentity(Stream stream)
    {
        using (stream)
        {
            return JsonSerializer.Deserialize<ReleaseIdentity>(stream, JsonOptions)
                ?? throw new JsonException("Platform API identity is empty.");
        }
    }

    private static string ReadText(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static byte[] ReadBytes(Stream stream)
    {
        using (stream)
        using (var memory = new MemoryStream())
        {
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }

    private static PlatformApiArtifactInspectionResult Failure(string kind, string path, string message) =>
        new(
            null,
            [
                new PlatformApiDiagnostic(
                    PlatformApiDiagnosticCodes.PlatformApiReleaseSkew,
                    "release-inspection",
                    message,
                    SourcePath: path,
                    ResolvedPackageId: PlatformApiArtifactKinds.Required.Contains(kind, StringComparer.Ordinal)
                        ? PlatformApiArtifactKinds.PackageIdFor(kind)
                        : kind)
            ]);

    private static PlatformApiSchemaInspectionResult SchemaFailure(string message) =>
        new(
            null,
            [
                new PlatformApiDiagnostic(
                    PlatformApiDiagnosticCodes.PlatformApiReleaseSkew,
                    "release-schema-inspection",
                    message)
            ]);

    private sealed record ReleaseIdentity(
        string SchemaVersion,
        string Version,
        string ReleaseDigest,
        string Profile,
        string PersistenceContract);

    private sealed record NuGetArtifactIdentity(
        string PackageId,
        string Version,
        ReleaseIdentity Identity,
        string? ContractsDependencyVersion);
}
