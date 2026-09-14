using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ICP.Platform.Plugin.Contracts;

namespace ICP.Platform.Plugin.Sdk;

/// <summary>
/// 把 standard-data.v1 Blueprint 编译为确定性标准投影和 release attestation。
/// </summary>
public sealed partial class StandardPluginBlueprintCompiler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// 编译指定 Blueprint；任何诊断都会拒绝整个结果。
    /// </summary>
    public StandardPluginCompilationResult Compile(string blueprintPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintPath);
        var fullPath = Path.GetFullPath(blueprintPath);
        if (!File.Exists(fullPath))
        {
            return Failure(PlatformApiDiagnosticCodes.PackageSchemaUnsupported, "schema", "Blueprint file does not exist.", fullPath);
        }

        try
        {
            var json = File.ReadAllText(fullPath);
            using var raw = JsonDocument.Parse(json);
            var forbidden = FindForbiddenRuntimeEntries(raw.RootElement, fullPath);
            if (forbidden.Count > 0)
            {
                return new StandardPluginCompilationResult(null, forbidden);
            }

            var blueprint = JsonSerializer.Deserialize<StandardPluginBlueprint>(json, JsonOptions)
                ?? throw new JsonException("Blueprint document is empty.");
            var diagnostics = Validate(blueprint, fullPath);
            if (diagnostics.Count > 0)
            {
                return new StandardPluginCompilationResult(null, diagnostics);
            }

            var moduleSlug = Slugify(blueprint.ModuleId);
            var schemaName = "np_" + SnakeCase(blueprint.ModuleId);
            var resources = blueprint.StandardData.Resources
                .OrderBy(static resource => resource.Key, StringComparer.Ordinal)
                .Select(resource => ProjectResource(blueprint.ModuleId, moduleSlug, schemaName, resource))
                .ToArray();
            var attestation = new PlatformApiAttestation(
                PlatformApiReleaseIdentity.Version,
                blueprint.Compatibility.PlatformApi.MinInclusive,
                blueprint.Compatibility.PlatformApi.MaxExclusive,
                PlatformApiReleaseIdentity.ReleaseDigest,
                blueprint.Profile,
                blueprint.Compatibility.Persistence.Contract,
                blueprint.Compatibility.Persistence.Providers.ToArray());
            return new StandardPluginCompilationResult(
                new StandardPluginCompilation(
                    blueprint,
                    attestation,
                    new StandardPluginProjection(moduleSlug, resources)),
                Array.Empty<PlatformApiDiagnostic>());
        }
        catch (JsonException exception)
        {
            return Failure(
                PlatformApiDiagnosticCodes.PackageSchemaUnsupported,
                "schema",
                exception.Message,
                fullPath);
        }
    }

    private static IReadOnlyList<PlatformApiDiagnostic> FindForbiddenRuntimeEntries(
        JsonElement root,
        string sourcePath)
    {
        var diagnostics = new List<PlatformApiDiagnostic>();
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("backend", out _))
        {
            diagnostics.Add(Diagnostic(
                PlatformApiDiagnosticCodes.StandardProfileBackendCodeForbidden,
                "profile",
                "standard-data.v1 cannot declare a backend entry or runtime callback.",
                sourcePath,
                "/backend"));
        }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("frontend", out _))
        {
            diagnostics.Add(Diagnostic(
                PlatformApiDiagnosticCodes.StandardProfileFrontendCodeForbidden,
                "profile",
                "standard-data.v1 cannot declare executable frontend code.",
                sourcePath,
                "/frontend"));
        }

        return diagnostics.OrderBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal).ToArray();
    }

    // 校验 Blueprint 的公开 profile、资源与持久化约束，任何诊断都拒绝整份编译结果。
    private static IReadOnlyList<PlatformApiDiagnostic> Validate(
        StandardPluginBlueprint blueprint,
        string sourcePath)
    {
        var diagnostics = new List<PlatformApiDiagnostic>();
        if (!string.Equals(blueprint.SchemaVersion, "1.0", StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                PlatformApiDiagnosticCodes.PackageSchemaUnsupported,
                "schema",
                $"Blueprint schema '{blueprint.SchemaVersion}' is unsupported.",
                sourcePath,
                "/schemaVersion"));
        }

        if (!string.Equals(blueprint.Profile, PlatformApiReleaseIdentity.StandardDataProfile, StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                PlatformApiDiagnosticCodes.PluginProfileUnsupported,
                "profile",
                $"Plugin profile '{blueprint.Profile}' is unsupported.",
                sourcePath,
                "/profile"));
        }

        if (!ModuleIdPattern().IsMatch(blueprint.ModuleId))
        {
            diagnostics.Add(Diagnostic(
                PlatformApiDiagnosticCodes.PackageSchemaUnsupported,
                "schema",
                "External moduleId must be a namespaced PascalCase identifier.",
                sourcePath,
                "/moduleId"));
        }

        if (!Version.TryParse(blueprint.Version, out _))
        {
            diagnostics.Add(Diagnostic(
                PlatformApiDiagnosticCodes.PackageSchemaUnsupported,
                "schema",
                "Plugin version must be SemVer X.Y.Z.",
                sourcePath,
                "/version"));
        }

        if (!string.Equals(
                blueprint.Compatibility.PlatformApi.MinInclusive,
                PlatformApiReleaseIdentity.Version,
                StringComparison.Ordinal)
            || !Version.TryParse(blueprint.Compatibility.PlatformApi.MaxExclusive, out var maxExclusive)
            || !Version.TryParse(PlatformApiReleaseIdentity.Version, out var builtWith)
            || maxExclusive <= builtWith
            || maxExclusive.Major != builtWith.Major + 1)
        {
            diagnostics.Add(Diagnostic(
                PlatformApiDiagnosticCodes.PlatformApiIncompatible,
                "compatibility",
                "Platform API minInclusive must equal builtWith and maxExclusive must be the next major.",
                sourcePath,
                "/compatibility/platformApi"));
        }

        if (!string.Equals(
                blueprint.Compatibility.Persistence.Contract,
                PlatformApiReleaseIdentity.PersistenceContract,
                StringComparison.Ordinal)
            || blueprint.Compatibility.Persistence.Providers.Count != 1
            || !string.Equals(
                blueprint.Compatibility.Persistence.Providers[0],
                PlatformApiReleaseIdentity.SqlServerProvider,
                StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                PlatformApiDiagnosticCodes.PersistenceContractUnsupported,
                "compatibility",
                "standard-data.v1 requires ef-migrations.v1 with sqlServer only.",
                sourcePath,
                "/compatibility/persistence"));
        }

        if (blueprint.StandardData.Resources.Count == 0
            || blueprint.StandardData.Resources.GroupBy(static resource => resource.Key, StringComparer.Ordinal).Any(static group => group.Count() > 1)
            || blueprint.StandardData.Resources.Any(static resource =>
                string.IsNullOrWhiteSpace(resource.Key)
                || string.IsNullOrWhiteSpace(resource.PluralName)
                || string.IsNullOrWhiteSpace(resource.DisplayField)
                || resource.Fields.Count == 0
                || resource.Fields.GroupBy(static field => field.Name, StringComparer.Ordinal).Any(static group => group.Count() > 1)
                || !resource.Fields.Any(field => string.Equals(field.Name, resource.DisplayField, StringComparison.Ordinal))))
        {
            diagnostics.Add(Diagnostic(
                PlatformApiDiagnosticCodes.PackageSchemaUnsupported,
                "schema",
                "Standard data resources and fields must be non-empty, unique and include the display field.",
                sourcePath,
                "/standardData/resources"));
        }

        for (var index = 0; index < blueprint.StandardData.Resources.Count; index++)
        {
            var detailViewKey = blueprint.StandardData.Resources[index].DetailViewKey;
            if (detailViewKey is not null && string.IsNullOrWhiteSpace(detailViewKey))
            {
                diagnostics.Add(Diagnostic(
                    PlatformApiDiagnosticCodes.PackageSchemaUnsupported,
                    "schema",
                    "Declared detailViewKey must not be empty.",
                    sourcePath,
                    $"/standardData/resources/{index}/detailViewKey"));
            }
        }

        if (blueprint.Persistence.Models.Count == 0
            || blueprint.Persistence.Models.Any(static path => !IsSafeRelativePath(path))
            || blueprint.Persistence.Migrations.Count != 1
            || !blueprint.Persistence.Migrations.TryGetValue(
                PlatformApiReleaseIdentity.SqlServerProvider,
                out var sqlServerBundle)
            || !IsSafeRelativePath(sqlServerBundle.Assembly)
            || !IsSafeRelativePath(sqlServerBundle.Manifest))
        {
            diagnostics.Add(Diagnostic(
                PlatformApiDiagnosticCodes.PersistenceContractUnsupported,
                "persistence",
                "standard-data.v1 requires contained model paths and exactly one sqlServer migration bundle.",
                sourcePath,
                "/persistence"));
        }

        return diagnostics
            .OrderBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.JsonPointer, StringComparer.Ordinal)
            .ToArray();
    }

    // 从单个资源声明派生确定性运行时投影，并保留显式详情引用。
    private static StandardDataResourceProjection ProjectResource(
        string moduleId,
        string moduleSlug,
        string schemaName,
        StandardDataResourceBlueprint resource)
    {
        var routeSegment = Slugify(resource.PluralName);
        var indexes = resource.Fields
            .Where(static field => field.Unique)
            .OrderBy(static field => field.Name, StringComparer.Ordinal)
            .Select(field => $"UX_{resource.PluralName}_{field.Name}")
            .ToArray();
        return new StandardDataResourceProjection(
            resource.Key,
            $"/api/plugins/{moduleSlug}/{routeSegment}",
            $"/plugins/{moduleSlug}/{routeSegment}",
            schemaName,
            resource.PluralName,
            indexes,
            [],
            resource.DetailViewKey);
    }

    private static StandardPluginCompilationResult Failure(
        string code,
        string stage,
        string message,
        string sourcePath) =>
        new(null, [Diagnostic(code, stage, message, sourcePath)]);

    private static PlatformApiDiagnostic Diagnostic(
        string code,
        string stage,
        string message,
        string sourcePath,
        string? pointer = null) =>
        new(code, stage, message, SourcePath: sourcePath, JsonPointer: pointer);

    private static string Slugify(string value)
    {
        var separated = PascalBoundaryPattern().Replace(value.Replace('.', '-').Replace('_', '-'), "$1-$2");
        return separated.ToLowerInvariant();
    }

    private static string SnakeCase(string value)
    {
        var separated = PascalBoundaryPattern().Replace(value.Replace('.', '_').Replace('-', '_'), "$1_$2");
        return separated.ToLowerInvariant();
    }

    private static bool IsSafeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || Path.IsPathRooted(value)
            || value.Contains('\0'))
        {
            return false;
        }

        var normalized = value.Replace('\\', '/');
        return !normalized.Split('/').Any(static segment => segment is "" or "." or "..");
    }

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*(\\.[A-Z][A-Za-z0-9]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex ModuleIdPattern();

    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex PascalBoundaryPattern();
}
