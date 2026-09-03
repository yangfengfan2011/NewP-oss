using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Xml.Linq;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.Planning;
using ManifestDocument = ICP.Platform.Persistence.Planning.MigrationManifest;

namespace ICP.Platform.Tools.MigrationManifest;

/// <summary>
/// migration manifest CLI 的稳定失败，携带可用于脚本定位的错误码。
/// </summary>
public sealed class MigrationManifestToolException : InvalidOperationException
{
    /// <summary>
    /// 创建包含 module/provider/path/field 上下文的工具失败。
    /// </summary>
    public MigrationManifestToolException(string code, string message, Exception? innerException = null)
        : base($"{code}: {message}", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}

/// <summary>
/// finalize 的可复跑结果。
/// </summary>
public sealed record MigrationManifestFinalizeResult(
    bool Changed,
    string ModuleId,
    string Provider,
    string MigrationId,
    string ManifestPath);

/// <summary>
/// 全仓只读 check 的汇总结果。
/// </summary>
public sealed record MigrationManifestCheckResult(
    int CheckedBundles,
    IReadOnlyList<string> ProviderResults);

/// <summary>
/// 拥有仓库 migration manifest 显式 finalize 与只读 check 行为。
/// </summary>
public sealed class MigrationManifestApplication
{
    private static readonly string[] SupportedCompatibility =
    [
        "safe-expand",
        "data-backfill",
        "contract",
        "backup-restore-only"
    ];

    private readonly string _repositoryRoot;
    private readonly Action<string, string> _replaceFile;

    /// <summary>
    /// 在给定仓库根上创建工具；默认使用同目录临时文件原子替换 manifest。
    /// </summary>
    public MigrationManifestApplication(
        string repositoryRoot,
        Action<string, string>? replaceFile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _replaceFile = replaceFile ?? ReplaceFileAtomically;
    }

    /// <summary>
    /// 只追加并 finalize 一个新尾 migration；已完成的同一尾节点可无写入幂等重放。
    /// </summary>
    public MigrationManifestFinalizeResult Finalize(
        string moduleId,
        string providerName,
        string migrationId,
        string? compatibility)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);
        var workspace = OpenBundle(moduleId, providerName);
        var originalBytes = File.ReadAllBytes(workspace.ManifestPath);
        var manifest = ParseManifest(workspace, originalBytes);
        IReadOnlyList<MigrationManifestStep> prefix;
        try
        {
            prefix = MigrationManifestIntegrity.ValidateAuthoringPrefix(
                workspace.ModuleId,
                workspace.Provider,
                manifest,
                workspace.ManifestPath);
        }
        catch (MigrationManifestValidationException exception)
        {
            throw Failure(workspace, exception.Code, exception.Message, exception);
        }

        ValidatePublishedSources(workspace, prefix, "MIGRATION_MANIFEST_HISTORY_CHANGED");
        var availableMigrationIds = ReadMigrationIds(workspace);
        var existingIndex = prefix
            .Select((step, index) => (step, index))
            .FirstOrDefault(item => string.Equals(
                item.step.MigrationId,
                migrationId,
                StringComparison.OrdinalIgnoreCase));
        if (existingIndex.step is not null)
        {
            if (existingIndex.index != prefix.Count - 1)
            {
                throw Failure(
                    workspace,
                    "MIGRATION_MANIFEST_NON_TAIL",
                    $"migration '{migrationId}' already exists before the tail; published history cannot be overwritten.");
            }

            if (compatibility is not null
                && !string.Equals(existingIndex.step.Compatibility, compatibility, StringComparison.Ordinal))
            {
                throw Failure(
                    workspace,
                    "MIGRATION_MANIFEST_HISTORY_CHANGED",
                    $"migration '{migrationId}' compatibility is already '{existingIndex.step.Compatibility}'.");
            }

            ValidatePublishedPrefix(workspace, prefix, migrationId);
            ValidateComplete(workspace, manifest, availableMigrationIds);
            var canonicalBytes = MigrationManifestIntegrity.SerializeCanonical(manifest);
            if (!canonicalBytes.AsSpan().SequenceEqual(originalBytes))
            {
                WriteManifestAtomically(workspace.ManifestPath, canonicalBytes);
            }

            return new MigrationManifestFinalizeResult(
                !canonicalBytes.AsSpan().SequenceEqual(originalBytes),
                workspace.ModuleId,
                workspace.Provider.ToString(),
                migrationId,
                workspace.ManifestPath);
        }

        ValidateCompatibility(workspace, compatibility);
        var undeclaredMigrationIds = availableMigrationIds
            .Except(prefix.Select(static step => step.MigrationId), StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        if (undeclaredMigrationIds.Length != 1
            || !string.Equals(undeclaredMigrationIds[0], migrationId, StringComparison.Ordinal))
        {
            throw Failure(
                workspace,
                "MIGRATION_MANIFEST_NON_TAIL",
                $"expected exactly one new tail migration '{migrationId}', but bundle contains undeclared [{string.Join(", ", undeclaredMigrationIds)}].");
        }

        var publishedBaseline = ValidatePublishedPrefix(workspace, prefix, migrationId);
        var sourcePath = ResolveMigrationSource(workspace, migrationId);
        var contentDigest = MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(sourcePath));
        var previous = prefix[^1];
        var step = new MigrationManifestStep(
            (prefix.Count + 1).ToString("D6", System.Globalization.CultureInfo.InvariantCulture),
            migrationId,
            previous.ContentDigest,
            contentDigest,
            previous.ToModelDigest,
            workspace.ModelDigest,
            compatibility!);
        var finalized = manifest with
        {
            ModelDigest = workspace.ModelDigest,
            BundleChecksum = MigrationManifestIntegrity.ComputeBundleChecksum(File.ReadAllBytes(workspace.BundlePath)),
            Migrations = [.. prefix, step]
        };
        ValidatePublishedSteps(workspace, finalized.Migrations, publishedBaseline);
        ValidateComplete(workspace, finalized, availableMigrationIds);
        var bytes = MigrationManifestIntegrity.SerializeCanonical(finalized);
        WriteManifestAtomically(workspace.ManifestPath, bytes);
        return new MigrationManifestFinalizeResult(
            true,
            workspace.ModuleId,
            workspace.Provider.ToString(),
            migrationId,
            workspace.ManifestPath);
    }

    /// <summary>
    /// 枚举全部 capsule-declared bundle，并在已有 Release 资产上完成零构建、零数据库的只读 preflight。
    /// </summary>
    public MigrationManifestCheckResult CheckAll(bool noBuild)
    {
        if (!noBuild)
        {
            throw new MigrationManifestToolException(
                "MIGRATION_MANIFEST_NO_BUILD_REQUIRED",
                "check 当前只支持 --all --no-build；请先显式完成 Release build。");
        }

        var workspaces = DiscoverBundles();
        foreach (var workspace in workspaces)
        {
            var manifest = ParseManifest(workspace, File.ReadAllBytes(workspace.ManifestPath));
            ValidateComplete(workspace, manifest, ReadMigrationIds(workspace));
            ValidatePublishedSources(workspace, manifest.Migrations, "MIGRATION_MANIFEST_SOURCE_DRIFT");
            ValidateHostCopies(workspace);
        }

        foreach (var providerGroup in workspaces.GroupBy(static workspace => workspace.Provider))
        {
            ValidateFormalCompiler(providerGroup.Key);
        }

        var providerResults = Enum.GetValues<PersistenceProvider>()
            .Select(provider =>
            {
                var count = workspaces.Count(workspace => workspace.Provider == provider);
                return count == 0
                    ? $"{provider}: no declared bundle"
                    : $"{provider}: {count} bundle checked";
            })
            .ToArray();
        return new MigrationManifestCheckResult(workspaces.Count, providerResults);
    }

    // 从源码 capsule、模型和确定性 Release 项目解析一个 bundle 的全部只读事实。
    private BundleWorkspace OpenBundle(string moduleId, string providerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        if (!Enum.TryParse<PersistenceProvider>(providerName, ignoreCase: true, out var provider))
        {
            throw new MigrationManifestToolException(
                "MIGRATION_MANIFEST_PROVIDER_UNSUPPORTED",
                $"module='{moduleId}', provider='{providerName}': supported providers are {string.Join(", ", Enum.GetNames<PersistenceProvider>())}.");
        }

        var capsulePath = Directory.GetFiles(
                Path.Combine(_repositoryRoot, "modules"),
                "newp.plugin.json",
                SearchOption.AllDirectories)
            .SingleOrDefault(path => string.Equals(
                ReadRequiredString(JsonDocument.Parse(File.ReadAllBytes(path)).RootElement, "moduleId", path),
                moduleId,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new MigrationManifestToolException(
                "MIGRATION_MANIFEST_MODULE_NOT_FOUND",
                $"module='{moduleId}', provider='{provider}': no authoring capsule was found.");
        return OpenBundle(capsulePath, provider);
    }

    // 枚举实际声明，而不是从项目名或目录名推断 bundle。
    private IReadOnlyList<BundleWorkspace> DiscoverBundles()
    {
        var modulesRoot = Path.Combine(_repositoryRoot, "modules");
        if (!Directory.Exists(modulesRoot))
        {
            throw new DirectoryNotFoundException($"Module authoring root not found: {modulesRoot}");
        }

        var workspaces = new List<BundleWorkspace>();
        foreach (var capsulePath in Directory.GetFiles(
                     modulesRoot,
                     "newp.plugin.json",
                     SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(capsulePath));
            var moduleId = ReadRequiredString(document.RootElement, "moduleId", capsulePath);
            if (!document.RootElement.TryGetProperty("persistence", out var persistence)
                || !persistence.TryGetProperty("migrations", out var migrations))
            {
                continue;
            }

            foreach (var bundle in migrations.EnumerateObject().OrderBy(static item => item.Name, StringComparer.Ordinal))
            {
                if (!Enum.TryParse<PersistenceProvider>(bundle.Name, ignoreCase: true, out var provider))
                {
                    throw new MigrationManifestToolException(
                        "MIGRATION_MANIFEST_PROVIDER_UNSUPPORTED",
                        $"module='{moduleId}', provider='{bundle.Name}', path='{capsulePath}', field='/persistence/migrations': provider is outside the current runtime matrix.");
                }

                workspaces.Add(OpenBundle(capsulePath, provider));
            }
        }

        return workspaces
            .OrderBy(static workspace => workspace.ModuleId, StringComparer.Ordinal)
            .ThenBy(static workspace => workspace.Provider)
            .ToArray();
    }

    // 把 capsule 的包内引用解析到源码 manifest、migration 项目与 Host Release 副本。
    private BundleWorkspace OpenBundle(string capsulePath, PersistenceProvider provider)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(capsulePath));
        var root = document.RootElement;
        var moduleId = ReadRequiredString(root, "moduleId", capsulePath);
        if (!root.TryGetProperty("persistence", out var persistence)
            || !persistence.TryGetProperty("migrations", out var migrations))
        {
            throw Failure(moduleId, provider, capsulePath, "/persistence/migrations", "MIGRATION_MANIFEST_BUNDLE_NOT_DECLARED", "no migration bundle is declared.");
        }

        var property = migrations.EnumerateObject().SingleOrDefault(item => string.Equals(
            item.Name,
            provider.ToString(),
            StringComparison.OrdinalIgnoreCase));
        if (property.Value.ValueKind != JsonValueKind.Object)
        {
            throw Failure(moduleId, provider, capsulePath, "/persistence/migrations", "MIGRATION_MANIFEST_BUNDLE_NOT_DECLARED", "provider has no declared bundle.");
        }

        var moduleRoot = Path.GetDirectoryName(capsulePath)!;
        var assemblyRelativePath = ReadRequiredString(property.Value, "assembly", capsulePath);
        var manifestRelativePath = ReadRequiredString(property.Value, "manifest", capsulePath);
        var manifestPath = ResolveContained(moduleRoot, manifestRelativePath, moduleId, provider, "manifest");
        if (!File.Exists(manifestPath))
        {
            throw Failure(moduleId, provider, manifestPath, "manifest", "MIGRATION_MANIFEST_RELEASE_ASSET_MISSING", "declared authoring manifest is missing.");
        }

        var project = FindMigrationProject(Path.GetFileNameWithoutExtension(assemblyRelativePath), moduleId, provider);
        var targetFramework = ReadRequiredProjectProperty(project, "TargetFramework", moduleId, provider);
        ValidateDeterministicProject(project, moduleId, provider);
        var bundlePath = Path.Combine(
            Path.GetDirectoryName(project)!,
            "bin",
            "Release",
            targetFramework,
            Path.GetFileName(assemblyRelativePath));
        EnsureReleaseAsset(bundlePath, project, moduleId, provider);

        var modelPaths = persistence.TryGetProperty("models", out var models)
            ? models.EnumerateArray()
                .Select(item => ResolveContained(
                    moduleRoot,
                    item.GetString() ?? string.Empty,
                    moduleId,
                    provider,
                    "model"))
                .ToArray()
            : Array.Empty<string>();
        var modelDigest = ComputeModelDigest(modelPaths, moduleId, provider);
        var hostRoot = ResolveHostReleaseRoot();
        return new BundleWorkspace(
            moduleId,
            provider,
            capsulePath,
            moduleRoot,
            project,
            bundlePath,
            manifestPath,
            modelDigest,
            Path.Combine(hostRoot, "modules", moduleId, assemblyRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            Path.Combine(hostRoot, "modules", moduleId, manifestRelativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    // 模型摘要复用 runtime 的 loader/projector/validator/digest，built-in authoring 固定投射到 dbo。
    private string ComputeModelDigest(
        IReadOnlyList<string> modelPaths,
        string moduleId,
        PersistenceProvider provider)
    {
        var typeMapPath = Path.Combine(_repositoryRoot, "server", "platform-modeling", "metadata", "storage-types.json");
        var typeMap = new JsonStorageTypeMapLoader().LoadFromFile(typeMapPath);
        var loader = new JsonModelMetadataDocumentLoader();
        var projector = new ModelMetadataProjector();
        var validator = new ModelDefinitionValidator();
        var definitions = new List<ModelDefinition>();
        foreach (var modelPath in modelPaths.Order(StringComparer.Ordinal))
        {
            try
            {
                var document = loader.LoadFromFile(modelPath);
                if (!string.Equals(document.ModuleId, moduleId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"model owner is '{document.ModuleId}'.");
                }

                var definition = projector.Project(document, typeMap, provider.ToString()) with
                {
                    OwnerModuleId = moduleId,
                    SchemaName = "dbo"
                };
                validator.Validate(definition);
                definitions.Add(definition);
            }
            catch (Exception exception)
            {
                throw Failure(moduleId, provider, modelPath, "model", "MIGRATION_MANIFEST_MODEL_INVALID", exception.Message, exception);
            }
        }

        return PersistenceModelDigest.Compute(definitions);
    }

    // 完整校验复用共享内核，并额外确认所有 manifest id 都存在于 Release assembly。
    private static void ValidateComplete(
        BundleWorkspace workspace,
        ManifestDocument manifest,
        IReadOnlyList<string> availableMigrationIds)
    {
        if (!string.Equals(manifest.ModelDigest, workspace.ModelDigest, StringComparison.Ordinal))
        {
            throw Failure(workspace, "MIGRATION_MANIFEST_MODEL_DRIFT", $"field='modelDigest', path='{workspace.ManifestPath}': expected '{workspace.ModelDigest}', actual '{manifest.ModelDigest}'.");
        }

        IReadOnlyList<MigrationManifestStep> steps;
        try
        {
            steps = MigrationManifestIntegrity.Validate(
                workspace.ModuleId,
                workspace.Provider,
                manifest,
                File.ReadAllBytes(workspace.BundlePath),
                workspace.ManifestPath,
                workspace.ModelDigest);
            MigrationManifestIntegrity.ValidateMigrationTypes(
                steps.Select(static step => step.MigrationId),
                availableMigrationIds);
        }
        catch (MigrationManifestValidationException exception)
        {
            throw Failure(workspace, exception.Code, exception.Message, exception);
        }
    }

    // 发布过的节点逐一回算 authoring source，历史内容变化不能被新的 finalize 吸收。
    private static void ValidatePublishedSources(
        BundleWorkspace workspace,
        IEnumerable<MigrationManifestStep> steps,
        string failureCode)
    {
        foreach (var step in steps)
        {
            var sourcePath = ResolveMigrationSource(workspace, step.MigrationId);
            var actual = MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(sourcePath));
            if (!string.Equals(actual, step.ContentDigest, StringComparison.Ordinal))
            {
                throw Failure(
                    workspace,
                    failureCode,
                    $"migration='{step.MigrationId}', field='contentDigest', path='{sourcePath}': expected '{step.ContentDigest}', actual '{actual}'.");
            }
        }
    }

    // 普通仓库以 HEAD 中 tracked manifest 为已发布事实；隔离根使用预先冻结的 Host manifest 副本。
    private PublishedManifestBaseline ValidatePublishedPrefix(
        BundleWorkspace workspace,
        IReadOnlyList<MigrationManifestStep> currentSteps,
        string migrationId)
    {
        var baseline = ReadPublishedBaseline(workspace);
        if (currentSteps.Count < baseline.Steps.Count)
        {
            var isSingleTailReplay = currentSteps.Count == baseline.Steps.Count - 1
                && string.Equals(
                    baseline.Steps[currentSteps.Count].MigrationId,
                    migrationId,
                    StringComparison.Ordinal);
            if (!isSingleTailReplay)
            {
                throw Failure(
                    workspace.ModuleId,
                    workspace.Provider,
                    workspace.ManifestPath,
                    "/migrations",
                    "MIGRATION_MANIFEST_HISTORY_CHANGED",
                    $"published baseline '{baseline.Source}' contains {baseline.Steps.Count} steps, but authoring contains {currentSteps.Count}; only one matching isolated tail replay is allowed.");
            }
        }

        ValidatePublishedSteps(workspace, currentSteps, baseline);
        return baseline;
    }

    // baseline 覆盖到的每个字段都必须逐值相同，不能靠重新串成自洽链掩盖历史改写。
    private static void ValidatePublishedSteps(
        BundleWorkspace workspace,
        IReadOnlyList<MigrationManifestStep> currentSteps,
        PublishedManifestBaseline baseline)
    {
        var comparisonCount = Math.Min(currentSteps.Count, baseline.Steps.Count);
        for (var index = 0; index < comparisonCount; index++)
        {
            var expected = baseline.Steps[index];
            var actual = currentSteps[index];
            var difference = FindHistoricalDifference(expected, actual);
            if (difference is null)
            {
                continue;
            }

            throw Failure(
                workspace.ModuleId,
                workspace.Provider,
                workspace.ManifestPath,
                $"/migrations/{index}/{difference.Value.Field}",
                "MIGRATION_MANIFEST_HISTORY_CHANGED",
                $"published baseline '{baseline.Source}' expects '{FormatHistoricalValue(difference.Value.Expected)}', actual '{FormatHistoricalValue(difference.Value.Actual)}'.");
        }
    }

    private PublishedManifestBaseline ReadPublishedBaseline(BundleWorkspace workspace)
    {
        var gitMarker = Path.Combine(_repositoryRoot, ".git");
        var hasLocalGitBoundary = Directory.Exists(gitMarker) || File.Exists(gitMarker);
        if (hasLocalGitBoundary)
        {
            var gitRoot = RunGit("rev-parse", "--show-toplevel");
            if (gitRoot.ExitCode != 0
                || !string.Equals(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(gitRoot.Output.Trim())),
                    Path.TrimEndingDirectorySeparator(_repositoryRoot),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw Failure(
                    workspace.ModuleId,
                    workspace.Provider,
                    workspace.ManifestPath,
                    "publishedPrefix",
                    "MIGRATION_MANIFEST_PUBLISHED_BASELINE_MISSING",
                    $"repository has a local Git boundary but HEAD could not be resolved for that root: {gitRoot.Error.Trim()}");
            }

            var relativeManifestPath = Path.GetRelativePath(_repositoryRoot, workspace.ManifestPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            var gitManifest = RunGit("show", $"HEAD:{relativeManifestPath}");
            if (gitManifest.ExitCode != 0)
            {
                throw Failure(
                    workspace.ModuleId,
                    workspace.Provider,
                    workspace.ManifestPath,
                    "publishedPrefix",
                    "MIGRATION_MANIFEST_PUBLISHED_BASELINE_MISSING",
                    $"HEAD does not contain tracked manifest '{relativeManifestPath}': {gitManifest.Error.Trim()}");
            }

            return ParsePublishedBaseline(
                workspace,
                System.Text.Encoding.UTF8.GetBytes(gitManifest.Output),
                $"git:HEAD:{relativeManifestPath}");
        }

        if (!File.Exists(workspace.HostManifestPath))
        {
            throw Failure(
                workspace.ModuleId,
                workspace.Provider,
                workspace.HostManifestPath,
                "publishedPrefix",
                "MIGRATION_MANIFEST_PUBLISHED_BASELINE_MISSING",
                "repository root is not a standalone Git worktree and its frozen Host Release manifest baseline is missing; copy the published Host manifest before finalize.");
        }

        return ParsePublishedBaseline(
            workspace,
            File.ReadAllBytes(workspace.HostManifestPath),
            workspace.HostManifestPath);
    }

    private static PublishedManifestBaseline ParsePublishedBaseline(
        BundleWorkspace workspace,
        byte[] bytes,
        string source)
    {
        try
        {
            var manifest = MigrationManifestIntegrity.Parse(bytes);
            var steps = MigrationManifestIntegrity.ValidateAuthoringPrefix(
                workspace.ModuleId,
                workspace.Provider,
                manifest,
                source);
            return new PublishedManifestBaseline(source, steps);
        }
        catch (Exception exception) when (exception is JsonException or MigrationManifestValidationException)
        {
            throw Failure(
                workspace.ModuleId,
                workspace.Provider,
                source,
                "publishedPrefix",
                "MIGRATION_MANIFEST_PUBLISHED_BASELINE_INVALID",
                exception.Message,
                exception);
        }
    }

    private GitCommandResult RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = _repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git while resolving the published manifest baseline.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new GitCommandResult(process.ExitCode, output, error);
    }

    private static HistoricalDifference? FindHistoricalDifference(
        MigrationManifestStep expected,
        MigrationManifestStep actual)
    {
        if (!string.Equals(expected.Sequence, actual.Sequence, StringComparison.Ordinal))
        {
            return new HistoricalDifference("sequence", expected.Sequence, actual.Sequence);
        }

        if (!string.Equals(expected.MigrationId, actual.MigrationId, StringComparison.Ordinal))
        {
            return new HistoricalDifference("migrationId", expected.MigrationId, actual.MigrationId);
        }

        if (!string.Equals(expected.PreviousContentDigest, actual.PreviousContentDigest, StringComparison.Ordinal))
        {
            return new HistoricalDifference("previousContentDigest", expected.PreviousContentDigest, actual.PreviousContentDigest);
        }

        if (!string.Equals(expected.ContentDigest, actual.ContentDigest, StringComparison.Ordinal))
        {
            return new HistoricalDifference("contentDigest", expected.ContentDigest, actual.ContentDigest);
        }

        if (!string.Equals(expected.FromModelDigest, actual.FromModelDigest, StringComparison.Ordinal))
        {
            return new HistoricalDifference("fromModelDigest", expected.FromModelDigest, actual.FromModelDigest);
        }

        if (!string.Equals(expected.ToModelDigest, actual.ToModelDigest, StringComparison.Ordinal))
        {
            return new HistoricalDifference("toModelDigest", expected.ToModelDigest, actual.ToModelDigest);
        }

        return !string.Equals(expected.Compatibility, actual.Compatibility, StringComparison.Ordinal)
            ? new HistoricalDifference("compatibility", expected.Compatibility, actual.Compatibility)
            : null;
    }

    private static string FormatHistoricalValue(string? value) => value ?? "<null>";

    // Host 必须消费与源码 Release/manifest 字节完全一致的副本。
    private static void ValidateHostCopies(BundleWorkspace workspace)
    {
        EnsureReleaseAsset(workspace.HostBundlePath, workspace.ProjectPath, workspace.ModuleId, workspace.Provider);
        EnsureReleaseAsset(workspace.HostManifestPath, workspace.ProjectPath, workspace.ModuleId, workspace.Provider);
        if (!File.ReadAllBytes(workspace.BundlePath).AsSpan().SequenceEqual(File.ReadAllBytes(workspace.HostBundlePath)))
        {
            throw Failure(workspace, "MIGRATION_MANIFEST_HOST_COPY_DRIFT", $"field='assembly', source='{workspace.BundlePath}', host='{workspace.HostBundlePath}'.");
        }

        if (!File.ReadAllBytes(workspace.ManifestPath).AsSpan().SequenceEqual(File.ReadAllBytes(workspace.HostManifestPath)))
        {
            throw Failure(workspace, "MIGRATION_MANIFEST_HOST_COPY_DRIFT", $"field='manifest', source='{workspace.ManifestPath}', host='{workspace.HostManifestPath}'.");
        }
    }

    // 正式 compiler 只返回 diagnostics；preflight 不取得、公开或执行可迁移数据库的 plan。
    private void ValidateFormalCompiler(PersistenceProvider provider)
    {
        BuiltInPersistencePreflightResult result;
        try
        {
            result = BuiltInPersistencePreflight.Check(ResolveHostReleaseRoot(), provider);
        }
        catch (Exception exception)
        {
            throw new MigrationManifestToolException(
                "MIGRATION_MANIFEST_COMPILER_FAILED",
                $"module='*', provider='{provider}', path='{ResolveHostReleaseRoot()}', field='PersistencePlanCompiler': {exception.Message}",
                exception);
        }

        if (!result.IsSuccess)
        {
            var details = result.ModuleDiagnostics
                .Select(diagnostic => $"{diagnostic.Code} module='{diagnostic.ModuleId}' path='{diagnostic.SourcePath}' field='{diagnostic.CanonicalKey}': {diagnostic.Message}")
                .Concat(result.PersistenceDiagnostics.Select(diagnostic => $"{diagnostic.Code} module='{diagnostic.ModuleId}' path='{diagnostic.SourcePath}' field='{diagnostic.CanonicalKey}': {diagnostic.Message}"));
            throw new MigrationManifestToolException(
                "MIGRATION_MANIFEST_COMPILER_FAILED",
                $"module='*', provider='{provider}', field='PersistencePlanCompiler': {string.Join(" | ", details)}");
        }
    }

    // 只反射 attribute metadata，不实例化 migration，不创建连接或执行 Up/Down。
    private static IReadOnlyList<string> ReadMigrationIds(BundleWorkspace workspace)
    {
        try
        {
            using var context = new MigrationInspectionLoadContext(workspace.BundlePath);
            using var assemblyStream = new MemoryStream(File.ReadAllBytes(workspace.BundlePath), writable: false);
            var assembly = context.LoadFromStream(assemblyStream);
            return assembly.GetTypes()
                .SelectMany(static type => type.CustomAttributes
                    .Where(attribute => string.Equals(
                        attribute.AttributeType.FullName,
                        "Microsoft.EntityFrameworkCore.Migrations.MigrationAttribute",
                        StringComparison.Ordinal))
                    .Select(attribute => attribute.ConstructorArguments.SingleOrDefault().Value as string))
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id!)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception)
        {
            throw Failure(workspace, "MIGRATION_MANIFEST_ASSEMBLY_INVALID", $"field='migrationTypes', path='{workspace.BundlePath}': {exception.Message}", exception);
        }
    }

    private string FindMigrationProject(
        string assemblyName,
        string moduleId,
        PersistenceProvider provider)
    {
        var projects = Directory.GetFiles(Path.Combine(_repositoryRoot, "server"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => string.Equals(
                ReadProjectProperty(path, "AssemblyName") ?? Path.GetFileNameWithoutExtension(path),
                assemblyName,
                StringComparison.Ordinal))
            .ToArray();
        if (projects.Length != 1)
        {
            throw Failure(moduleId, provider, _repositoryRoot, "assembly", "MIGRATION_MANIFEST_PROJECT_INVALID", $"expected one project for assembly '{assemblyName}', found {projects.Length}.");
        }

        return projects[0];
    }

    private static void ValidateDeterministicProject(
        string projectPath,
        string moduleId,
        PersistenceProvider provider)
    {
        var deterministic = ReadProjectProperty(projectPath, "Deterministic");
        var pathMap = ReadProjectProperty(projectPath, "PathMap");
        var sourceRevision = ReadProjectProperty(projectPath, "IncludeSourceRevisionInInformationalVersion");
        if (!string.Equals(deterministic, "true", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(pathMap)
            || !string.Equals(sourceRevision, "false", StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                moduleId,
                provider,
                projectPath,
                "Deterministic/PathMap/IncludeSourceRevisionInInformationalVersion",
                "MIGRATION_MANIFEST_BUILD_NOT_DETERMINISTIC",
                "Release migration project must pin deterministic output, stable PathMap, and exclude source revision metadata.");
        }
    }

    private static string ReadRequiredProjectProperty(
        string projectPath,
        string propertyName,
        string moduleId,
        PersistenceProvider provider) =>
        ReadProjectProperty(projectPath, propertyName)
        ?? throw Failure(moduleId, provider, projectPath, propertyName, "MIGRATION_MANIFEST_PROJECT_INVALID", $"project property '{propertyName}' is missing.");

    private static string? ReadProjectProperty(string projectPath, string propertyName) =>
        XDocument.Load(projectPath)
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, propertyName, StringComparison.Ordinal))
            ?.Value.Trim();

    private static void EnsureReleaseAsset(
        string path,
        string projectPath,
        string moduleId,
        PersistenceProvider provider)
    {
        if (!File.Exists(path))
        {
            throw Failure(
                moduleId,
                provider,
                path,
                "ReleaseAsset",
                "MIGRATION_MANIFEST_RELEASE_ASSET_MISSING",
                $"required Release asset is missing. Run: dotnet build \"{projectPath}\" -c Release");
        }
    }

    private static ManifestDocument ParseManifest(BundleWorkspace workspace, byte[] bytes)
    {
        try
        {
            return MigrationManifestIntegrity.Parse(bytes);
        }
        catch (Exception exception)
        {
            throw Failure(workspace, "MIGRATION_MANIFEST_JSON_INVALID", $"field='manifest', path='{workspace.ManifestPath}': {exception.Message}", exception);
        }
    }

    private static string ResolveMigrationSource(BundleWorkspace workspace, string migrationId)
    {
        var migrationsRoot = Path.Combine(Path.GetDirectoryName(workspace.ProjectPath)!, "Migrations");
        var matches = Directory.Exists(migrationsRoot)
            ? Directory.GetFiles(migrationsRoot, migrationId + ".cs", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();
        if (matches.Length != 1)
        {
            throw Failure(workspace, "MIGRATION_MANIFEST_SOURCE_MISSING", $"migration='{migrationId}', path='{migrationsRoot}': expected one canonical migration source, found {matches.Length}.");
        }

        return matches[0];
    }

    private static string ResolveContained(
        string root,
        string relativePath,
        string moduleId,
        PersistenceProvider provider,
        string field)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw Failure(moduleId, provider, root, field, "MIGRATION_MANIFEST_PATH_INVALID", "path must be package-relative.");
        }

        var resolvedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var resolved = Path.GetFullPath(Path.Combine(resolvedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(resolvedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(moduleId, provider, resolved, field, "MIGRATION_MANIFEST_PATH_INVALID", "path escapes the module root.");
        }

        return resolved;
    }

    private static string ReadRequiredString(JsonElement root, string propertyName, string path)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new MigrationManifestToolException(
                "MIGRATION_MANIFEST_CAPSULE_INVALID",
                $"module='?', provider='?', path='{path}', field='{propertyName}': required string is missing.");
        }

        return value.GetString()!;
    }

    private string ResolveHostReleaseRoot()
    {
        var hostProject = Path.Combine(_repositoryRoot, "apps", "host-api", "HostApi.csproj");
        var targetFramework = ReadRequiredProjectProperty(hostProject, "TargetFramework", "*", PersistenceProvider.SqlServer);
        var root = Path.Combine(_repositoryRoot, "apps", "host-api", "bin", "Release", targetFramework);
        if (!Directory.Exists(root))
        {
            throw new MigrationManifestToolException(
                "MIGRATION_MANIFEST_RELEASE_ASSET_MISSING",
                $"module='*', provider='*', path='{root}', field='HostRelease': required output is missing. Run: dotnet build \"{hostProject}\" -c Release");
        }

        return root;
    }

    private static void ValidateCompatibility(BundleWorkspace workspace, string? compatibility)
    {
        if (compatibility is null)
        {
            throw Failure(workspace, "MIGRATION_MANIFEST_COMPATIBILITY_REQUIRED", "field='compatibility': a new tail requires explicit --compatibility.");
        }

        if (!SupportedCompatibility.Contains(compatibility, StringComparer.Ordinal))
        {
            throw Failure(workspace, "MIGRATION_MANIFEST_COMPATIBILITY_INVALID", $"field='compatibility': '{compatibility}' is not one of [{string.Join(", ", SupportedCompatibility)}].");
        }
    }

    // 临时文件与目标同目录；只有全部校验完成后才进入 replace，任何失败均清理临时文件。
    private void WriteManifestAtomically(string manifestPath, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(manifestPath)!;
        var temporaryPath = Path.Combine(directory, $".migration-manifest-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            _replaceFile(temporaryPath, manifestPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    // File.Replace 在同一 volume 内提供替换语义，避免 delete/create 暴露半写窗口。
    private static void ReplaceFileAtomically(string temporaryPath, string manifestPath) =>
        File.Replace(temporaryPath, manifestPath, destinationBackupFileName: null);

    private static MigrationManifestToolException Failure(
        BundleWorkspace workspace,
        string code,
        string detail,
        Exception? exception = null) =>
        Failure(
            workspace.ModuleId,
            workspace.Provider,
            workspace.ManifestPath,
            "manifest",
            code,
            detail,
            exception);

    private static MigrationManifestToolException Failure(
        string moduleId,
        PersistenceProvider provider,
        string path,
        string field,
        string code,
        string detail,
        Exception? exception = null) =>
        new(
            code,
            $"module='{moduleId}', provider='{provider}', path='{path}', field='{field}': {detail}",
            exception);

    private sealed record BundleWorkspace(
        string ModuleId,
        PersistenceProvider Provider,
        string CapsulePath,
        string ModuleRoot,
        string ProjectPath,
        string BundlePath,
        string ManifestPath,
        string ModelDigest,
        string HostBundlePath,
        string HostManifestPath);

    private sealed record PublishedManifestBaseline(
        string Source,
        IReadOnlyList<MigrationManifestStep> Steps);

    private readonly record struct HistoricalDifference(
        string Field,
        string? Expected,
        string? Actual);

    private readonly record struct GitCommandResult(
        int ExitCode,
        string Output,
        string Error);

    private sealed class MigrationInspectionLoadContext : AssemblyLoadContext, IDisposable
    {
        private readonly AssemblyDependencyResolver _resolver;

        internal MigrationInspectionLoadContext(string assemblyPath)
            : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(assemblyPath);
        }

        // 依赖优先复用当前工具已验证加载的共享框架；其余只从 bundle 输出目录解析。
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var shared = Default.Assemblies.FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
            if (shared is not null)
            {
                return shared;
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        public void Dispose() => Unload();
    }
}
