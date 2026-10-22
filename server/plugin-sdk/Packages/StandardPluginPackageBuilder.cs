using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.Plugin.Contracts;

namespace ICP.Platform.Plugin.Sdk;

/// <summary>
/// standard-data.v1 打包输入。
/// </summary>
public sealed record StandardPluginPackRequest(
    string AuthoringRoot,
    string OutputPath,
    PlatformApiReleaseManifest ReleaseManifest);

/// <summary>
/// `.npp` 打包结果。
/// </summary>
public sealed record StandardPluginPackResult(
    string? OutputPath,
    string? PackageSha256,
    IReadOnlyList<PlatformApiDiagnostic> Diagnostics)
{
    public bool IsSuccess => OutputPath is not null && PackageSha256 is not null && Diagnostics.Count == 0;
}

/// <summary>
/// 从外仓 Blueprint 与受控 migration bundle 产生确定性 `.npp`。
/// </summary>
public sealed class StandardPluginPackageBuilder
{
    private const string BlueprintEntry = "authoring/newp.plugin.json";
    private const string AttestationEntry = "attestation/platform-api.json";
    private const string ProjectionEntry = "projections/standard-data.json";
    private const string ExplainEntry = "provenance/explain.json";
    private const string ChecksumsEntry = "checksums/sha256sums.json";
    private static readonly DateTimeOffset DeterministicTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// 验证外仓依赖、Blueprint、发布证明和显式包内容后写出 `.npp`。
    /// </summary>
    public StandardPluginPackResult Pack(StandardPluginPackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var authoringRoot = Path.GetFullPath(request.AuthoringRoot);
        var outputPath = Path.GetFullPath(request.OutputPath);
        var dependencyDiagnostics = new ExternalConsumerDependencyAuditor().Audit(authoringRoot);
        if (dependencyDiagnostics.Count > 0)
        {
            return Failure(dependencyDiagnostics);
        }

        var runtimeDiagnostics = FindForbiddenRuntimeArtifacts(authoringRoot);
        if (runtimeDiagnostics.Count > 0)
        {
            return Failure(runtimeDiagnostics);
        }

        var blueprintPath = Path.Combine(authoringRoot, "newp.plugin.json");
        var compilationResult = new StandardPluginBlueprintCompiler().Compile(blueprintPath);
        if (!compilationResult.IsSuccess)
        {
            return Failure(compilationResult.Diagnostics);
        }

        var compilation = compilationResult.Compilation!;
        var releaseDiagnostic = ValidateRelease(request.ReleaseManifest, compilation);
        if (releaseDiagnostic is not null)
        {
            return Failure([releaseDiagnostic]);
        }

        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [BlueprintEntry] = File.ReadAllBytes(blueprintPath),
            [AttestationEntry] = JsonSerializer.SerializeToUtf8Bytes(compilation.Attestation, JsonOptions),
            [ProjectionEntry] = JsonSerializer.SerializeToUtf8Bytes(compilation.Projection, JsonOptions),
            [ExplainEntry] = JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    schemaVersion = "1.0",
                    source = "newp.plugin.json",
                    release = new
                    {
                        request.ReleaseManifest.ReleaseVersion,
                        request.ReleaseManifest.ReleaseDigest,
                        request.ReleaseManifest.SourceCommit
                    },
                    compilation.Blueprint.ModuleId,
                    compilation.Blueprint.Version,
                    compilation.Projection
                },
                JsonOptions)
        };

        var contentDiagnostic = AddDeclaredContent(authoringRoot, compilation.Blueprint, files);
        if (contentDiagnostic is not null)
        {
            return Failure([contentDiagnostic]);
        }

        var packageManifest = new StandardPluginPackageManifest(
            1,
            compilation.Blueprint.Profile,
            compilation.Blueprint.ModuleId,
            compilation.Blueprint.DisplayName,
            compilation.Blueprint.Version,
            compilation.Blueprint.Description,
            compilation.Attestation,
            new StandardPluginPackageArtifacts(BlueprintEntry, ProjectionEntry, ExplainEntry),
            compilation.Blueprint.Persistence,
            new StandardPluginPackageChecksums(ChecksumsEntry));
        files["plugin.json"] = JsonSerializer.SerializeToUtf8Bytes(packageManifest, JsonOptions);

        var checksums = new StandardPluginChecksumManifest(
            "1.0",
            files.ToDictionary(
                static item => item.Key,
                static item => Sha256(item.Value),
                StringComparer.Ordinal));
        files[ChecksumsEntry] = JsonSerializer.SerializeToUtf8Bytes(checksums, JsonOptions);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
                entry.LastWriteTime = DeterministicTimestamp;
                using var entryStream = entry.Open();
                entryStream.Write(file.Value);
            }
        }

        return new StandardPluginPackResult(outputPath, Sha256(File.ReadAllBytes(outputPath)), Array.Empty<PlatformApiDiagnostic>());
    }

    private static PlatformApiDiagnostic? AddDeclaredContent(
        string authoringRoot,
        StandardPluginBlueprint blueprint,
        IDictionary<string, byte[]> files)
    {
        var declaredPaths = blueprint.Persistence.Models
            .Concat(blueprint.Persistence.Migrations.Values.SelectMany(static bundle => new[] { bundle.Assembly, bundle.Manifest }))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        foreach (var relativePath in declaredPaths)
        {
            var fullPath = ResolveContainedPath(authoringRoot, relativePath);
            if (fullPath is null || !File.Exists(fullPath))
            {
                return new PlatformApiDiagnostic(
                    PlatformApiDiagnosticCodes.PackageSchemaUnsupported,
                    "pack",
                    $"Declared package artifact '{relativePath}' is missing or unsafe.",
                    ModuleId: blueprint.ModuleId,
                    Version: blueprint.Version,
                    Profile: blueprint.Profile,
                    SourcePath: relativePath);
            }

            files[NormalizeEntryPath(relativePath)] = File.ReadAllBytes(fullPath);
        }

        foreach (var rootName in new[] { "metadata", "localization", "resources" })
        {
            var contentRoot = Path.Combine(authoringRoot, rootName);
            if (!Directory.Exists(contentRoot))
            {
                continue;
            }

            foreach (var fullPath in Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories)
                         .OrderBy(static path => path, StringComparer.Ordinal))
            {
                var relativePath = Path.GetRelativePath(authoringRoot, fullPath);
                files[NormalizeEntryPath(relativePath)] = File.ReadAllBytes(fullPath);
            }
        }

        return null;
    }

    private static IReadOnlyList<PlatformApiDiagnostic> FindForbiddenRuntimeArtifacts(string authoringRoot)
    {
        var diagnostics = new List<PlatformApiDiagnostic>();
        var backendRoot = Path.Combine(authoringRoot, "backend");
        if (Directory.Exists(backendRoot) && Directory.EnumerateFiles(backendRoot, "*", SearchOption.AllDirectories).Any())
        {
            diagnostics.Add(new PlatformApiDiagnostic(
                PlatformApiDiagnosticCodes.StandardProfileBackendCodeForbidden,
                "pack",
                "standard-data.v1 cannot package a backend runtime entry.",
                SourcePath: backendRoot));
        }

        var frontendRoot = Path.Combine(authoringRoot, "frontend");
        if (Directory.Exists(frontendRoot) && Directory.EnumerateFiles(frontendRoot, "*", SearchOption.AllDirectories).Any())
        {
            diagnostics.Add(new PlatformApiDiagnostic(
                PlatformApiDiagnosticCodes.StandardProfileFrontendCodeForbidden,
                "pack",
                "standard-data.v1 cannot package executable frontend assets.",
                SourcePath: frontendRoot));
        }

        return diagnostics.OrderBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal).ToArray();
    }

    private static PlatformApiDiagnostic? ValidateRelease(
        PlatformApiReleaseManifest release,
        StandardPluginCompilation compilation)
    {
        var validArtifacts = release.Artifacts.Count == PlatformApiArtifactKinds.Required.Count
            && PlatformApiArtifactKinds.Required.All(kind => release.Artifacts.Count(artifact =>
                string.Equals(artifact.Kind, kind, StringComparison.Ordinal)
                && string.Equals(artifact.PackageId, PlatformApiArtifactKinds.PackageIdFor(kind), StringComparison.Ordinal)
                && string.Equals(artifact.Version, release.ReleaseVersion, StringComparison.Ordinal)
                && string.Equals(artifact.ReleaseDigest, release.ReleaseDigest, StringComparison.Ordinal)) == 1);
        if (validArtifacts
            && string.Equals(release.ReleaseVersion, compilation.Attestation.BuiltWith, StringComparison.Ordinal)
            && string.Equals(release.ReleaseDigest, compilation.Attestation.ReleaseDigest, StringComparison.Ordinal))
        {
            return null;
        }

        return new PlatformApiDiagnostic(
            PlatformApiDiagnosticCodes.PlatformApiReleaseSkew,
            "pack",
            "The release manifest and all four artifacts must match the SDK build identity.",
            ModuleId: compilation.Blueprint.ModuleId,
            Version: compilation.Blueprint.Version,
            Profile: compilation.Blueprint.Profile,
            BuiltWith: compilation.Attestation.BuiltWith,
            SupportedRelease: release.ReleaseVersion);
    }

    private static string? ResolveContainedPath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        return candidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : null;
    }

    private static string NormalizeEntryPath(string path) => path.Replace('\\', '/');

    internal static string Sha256(byte[] bytes) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static StandardPluginPackResult Failure(IReadOnlyList<PlatformApiDiagnostic> diagnostics) =>
        new(null, null, diagnostics);
}
