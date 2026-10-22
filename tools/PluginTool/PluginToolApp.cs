using System.Text.Json;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.Plugin.Sdk;

namespace ICP.Platform.PluginTool;

/// <summary>
/// `newp plugin` build-time 命令入口。
/// </summary>
public static class PluginToolApp
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// 执行静态 authoring、validate、explain、pack 或 verify，并只输出结构化结果。
    /// </summary>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            if (args.Length >= 2
                && string.Equals(args[0], "platform-api", StringComparison.OrdinalIgnoreCase)
                && string.Equals(args[1], "release", StringComparison.OrdinalIgnoreCase))
            {
                return await RunPlatformApiReleaseAsync(ParseOptions(args[2..]), output, cancellationToken);
            }

            if (args.Length < 2 || !string.Equals(args[0], "plugin", StringComparison.OrdinalIgnoreCase))
            {
                await error.WriteLineAsync("Usage: newp plugin <validate|explain|pack|verify|finalize-migration> [options]");
                return 2;
            }

            var command = args[1].ToLowerInvariant();
            var options = ParseOptions(args[2..]);
            var releasePath = RequireOption(options, "release-manifest");
            var release = LoadRelease(releasePath);
            return command switch
            {
                "validate" => await RunValidateAsync(options, release, output, cancellationToken),
                "explain" => await RunExplainAsync(options, release, output, cancellationToken),
                "pack" => await RunPackAsync(options, release, output, cancellationToken),
                "verify" => await RunVerifyAsync(options, release, output, cancellationToken),
                "finalize-migration" => await RunFinalizeMigrationAsync(options, release, output, cancellationToken),
                _ => await UnknownCommandAsync(command, error)
            };
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or JsonException)
        {
            await WriteResultAsync(
                output,
                "plugin",
                "failed",
                diagnostics:
                [
                    new PlatformApiDiagnostic(
                        PlatformApiDiagnosticCodes.PackageSchemaUnsupported,
                        "tool",
                        exception.Message)
                ],
                cancellationToken: cancellationToken);
            return 1;
        }
    }

    private static async Task<int> RunValidateAsync(
        IReadOnlyDictionary<string, string> options,
        PlatformApiReleaseManifest release,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var root = RequireOption(options, "root");
        var result = ValidateAuthoring(root, release);
        await WriteResultAsync(
            output,
            "plugin validate",
            result.Compilation is null ? "failed" : "passed",
            result.Compilation?.Blueprint.ModuleId,
            diagnostics: result.Diagnostics,
            cancellationToken: cancellationToken);
        return result.Compilation is null ? 1 : 0;
    }

    // 公开 finalize 先通过同版 release/依赖/Blueprint 检查，再派生静态 manifest；不执行数据库操作。
    private static async Task<int> RunFinalizeMigrationAsync(IReadOnlyDictionary<string, string> options,
        PlatformApiReleaseManifest release, TextWriter output, CancellationToken token)
    {
        var root = RequireOption(options, "root");
        var result = ValidateAuthoring(root, release);
        if (!result.IsSuccess)
        {
            await WriteResultAsync(output, "plugin finalize-migration", "failed", diagnostics: result.Diagnostics, cancellationToken: token);
            return 1;
        }
        try
        {
            var path = await StandardPluginMigrationAuthoring.FinalizeAsync(root, result.Compilation!.Blueprint,
                RequireOption(options, "migration"), RequireOption(options, "source"), RequireOption(options, "compatibility"), token);
            await WriteResultAsync(output, "plugin finalize-migration", "passed", result.Compilation.Blueprint.ModuleId, path, cancellationToken: token);
            return 0;
        }
        catch (ICP.Platform.Persistence.Planning.MigrationManifestValidationException exception)
        {
            await WriteResultAsync(output, "plugin finalize-migration", "failed", diagnostics:
                [new PlatformApiDiagnostic(exception.Code, "migration-authoring", exception.Message)], cancellationToken: token);
            return 1;
        }
        catch (Exception exception) when (exception is BadImageFormatException or InvalidOperationException)
        {
            await WriteResultAsync(output, "plugin finalize-migration", "failed", diagnostics:
                [new PlatformApiDiagnostic("MIGRATION_MANIFEST_AUTHORING_INVALID", "migration-authoring", exception.Message)], cancellationToken: token);
            return 1;
        }
    }

    private static async Task<int> RunPlatformApiReleaseAsync(
        IReadOnlyDictionary<string, string> options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var inspector = new PlatformApiArtifactInspector();
        var inputs = new[]
        {
            (PlatformApiArtifactKinds.Contracts, RequireOption(options, "contracts")),
            (PlatformApiArtifactKinds.BackendSdk, RequireOption(options, "backend-sdk")),
            (PlatformApiArtifactKinds.FrontendSdk, RequireOption(options, "frontend-sdk")),
            (PlatformApiArtifactKinds.Tool, RequireOption(options, "tool"))
        };
        var inspections = inputs.Select(input => inspector.Inspect(input.Item1, input.Item2)).ToArray();
        var inspectionDiagnostics = inspections.SelectMany(static result => result.Diagnostics).ToArray();
        if (inspectionDiagnostics.Length > 0)
        {
            await WriteResultAsync(
                output,
                "platform-api release",
                "failed",
                diagnostics: inspectionDiagnostics,
                cancellationToken: cancellationToken);
            return 1;
        }

        var schemas = inspector.InspectCanonicalSchemas(
            RequireOption(options, "contracts"),
            RequireOption(options, "frontend-sdk"));
        if (!schemas.IsSuccess)
        {
            await WriteResultAsync(
                output,
                "platform-api release",
                "failed",
                diagnostics: schemas.Diagnostics,
                cancellationToken: cancellationToken);
            return 1;
        }

        var build = new PlatformApiReleaseManifestBuilder().Build(
            new PlatformApiReleaseBuildRequest(
                RequireOption(options, "source-commit"),
                inspections.Select(static result => result.Artifact!).ToArray(),
                schemas.Schemas));
        if (!build.IsSuccess)
        {
            await WriteResultAsync(
                output,
                "platform-api release",
                "failed",
                diagnostics: build.Diagnostics,
                cancellationToken: cancellationToken);
            return 1;
        }

        var outputPath = Path.GetFullPath(RequireOption(options, "output"));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(
            outputPath,
            JsonSerializer.Serialize(build.Manifest, JsonOptions),
            cancellationToken);
        await WriteResultAsync(
            output,
            "platform-api release",
            "passed",
            outputPath: outputPath,
            cancellationToken: cancellationToken);
        return 0;
    }

    private static async Task<int> RunExplainAsync(
        IReadOnlyDictionary<string, string> options,
        PlatformApiReleaseManifest release,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var root = RequireOption(options, "root");
        var outputPath = Path.GetFullPath(RequireOption(options, "output"));
        var result = ValidateAuthoring(root, release);
        if (result.Compilation is null)
        {
            await WriteResultAsync(output, "plugin explain", "failed", diagnostics: result.Diagnostics, cancellationToken: cancellationToken);
            return 1;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(
            outputPath,
            JsonSerializer.Serialize(
                new
                {
                    schemaVersion = "1.0",
                    release = new { release.ReleaseVersion, release.ReleaseDigest },
                    result.Compilation.Blueprint.ModuleId,
                    result.Compilation.Blueprint.Version,
                    result.Compilation.Projection
                },
                JsonOptions),
            cancellationToken);
        await WriteResultAsync(
            output,
            "plugin explain",
            "passed",
            result.Compilation.Blueprint.ModuleId,
            outputPath,
            cancellationToken: cancellationToken);
        return 0;
    }

    private static async Task<int> RunPackAsync(
        IReadOnlyDictionary<string, string> options,
        PlatformApiReleaseManifest release,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var result = new StandardPluginPackageBuilder().Pack(
            new StandardPluginPackRequest(
                RequireOption(options, "root"),
                RequireOption(options, "output"),
                release));
        await WriteResultAsync(
            output,
            "plugin pack",
            result.IsSuccess ? "passed" : "failed",
            outputPath: result.OutputPath,
            packageSha256: result.PackageSha256,
            diagnostics: result.Diagnostics,
            cancellationToken: cancellationToken);
        return result.IsSuccess ? 0 : 1;
    }

    private static async Task<int> RunVerifyAsync(
        IReadOnlyDictionary<string, string> options,
        PlatformApiReleaseManifest release,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var result = new StandardPluginPackageVerifier().Verify(
            new StandardPluginVerifyRequest(RequireOption(options, "package"), release));
        await WriteResultAsync(
            output,
            "plugin verify",
            result.IsValid ? "passed" : "failed",
            result.Manifest?.ModuleId,
            diagnostics: result.Diagnostics,
            cancellationToken: cancellationToken);
        return result.IsValid ? 0 : 1;
    }

    private static StandardPluginCompilationResult ValidateAuthoring(
        string root,
        PlatformApiReleaseManifest release)
    {
        var releaseDiagnostics = new PlatformApiReleaseManifestValidator().Validate(release);
        var dependencyDiagnostics = new ExternalConsumerDependencyAuditor().Audit(root);
        var compilerResult = new StandardPluginBlueprintCompiler().Compile(Path.Combine(root, "newp.plugin.json"));
        var diagnostics = releaseDiagnostics
            .Concat(dependencyDiagnostics)
            .Concat(compilerResult.Diagnostics)
            .OrderBy(static diagnostic => diagnostic.Stage, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.SourcePath, StringComparer.Ordinal)
            .ToArray();
        return diagnostics.Length == 0
            ? compilerResult
            : new StandardPluginCompilationResult(null, diagnostics);
    }

    private static PlatformApiReleaseManifest LoadRelease(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return JsonSerializer.Deserialize<PlatformApiReleaseManifest>(File.ReadAllText(fullPath), JsonOptions)
            ?? throw new JsonException("Platform API release manifest is empty.");
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        if (args.Length % 2 != 0)
        {
            throw new ArgumentException("Options must use --name value pairs.");
        }

        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || args[index].Length == 2)
            {
                throw new ArgumentException($"Unexpected option '{args[index]}'.");
            }

            options[args[index][2..]] = args[index + 1];
        }

        return options;
    }

    private static string RequireOption(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required option '--{name}'.");

    private static async Task<int> UnknownCommandAsync(string command, TextWriter error)
    {
        await error.WriteLineAsync($"Unknown plugin command '{command}'.");
        return 2;
    }

    private static async Task WriteResultAsync(
        TextWriter output,
        string command,
        string status,
        string? moduleId = null,
        string? outputPath = null,
        string? packageSha256 = null,
        IReadOnlyList<PlatformApiDiagnostic>? diagnostics = null,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(
            new
            {
                schemaVersion = "1.0",
                command,
                status,
                moduleId,
                outputPath,
                packageSha256,
                diagnostics = diagnostics ?? Array.Empty<PlatformApiDiagnostic>()
            },
            JsonOptions);
        await output.WriteLineAsync(json.AsMemory(), cancellationToken);
    }
}
