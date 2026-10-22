using System.Text.Json;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>
/// 由 Host composition root 签发的内建模块根；普通库调用方不能把任意目录提升为内建来源。
/// </summary>
internal sealed class TrustedBuiltInModuleSource
{
    private readonly Action<string>? _beforeArtifactOpen;
    private readonly Dictionary<string, VerifiedModuleArtifactSnapshot> _acceptedNativeModules = new(StringComparer.OrdinalIgnoreCase);

    private TrustedBuiltInModuleSource(
        string modulesRoot,
        string installationRoot,
        Action<string>? beforeArtifactOpen)
    {
        ModulesRoot = modulesRoot;
        DistributionRoot = installationRoot;
        _beforeArtifactOpen = beforeArtifactOpen;
    }

    internal string ModulesRoot { get; }

    internal string DistributionRoot { get; }

    internal string? PlatformCompositionModuleId { get; private init; }

    // 正式 Host 与只读发布物检查从同一受控工件读取角色；缺失工件不可退回无基础约束。
    internal static TrustedBuiltInModuleSource CreateForHostDistribution(string installationRoot, string validatedModulesRoot)
    {
        var source = CreateForHost(installationRoot, validatedModulesRoot);
        using var document = JsonDocument.Parse(source.CaptureDistributionFile(
            Path.Combine(source.DistributionRoot, "module-composition.json")));
        if (!document.RootElement.TryGetProperty("platformCompositionModuleId", out var role)
            || role.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(role.GetString()))
            throw new JsonException("Distribution module-composition.json requires a platformCompositionModuleId.");
        return new TrustedBuiltInModuleSource(source.ModulesRoot, source.DistributionRoot, beforeArtifactOpen: null)
        { PlatformCompositionModuleId = role.GetString() };
    }

    internal static TrustedBuiltInModuleSource CreateForHost(
        string installationRoot,
        string validatedModulesRoot) =>
        CreateForHost(installationRoot, validatedModulesRoot, beforeArtifactOpen: null);

    internal static TrustedBuiltInModuleSource CreateForHost(
        string installationRoot,
        string validatedModulesRoot,
        Action<string>? beforeArtifactOpen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(validatedModulesRoot);

        var resolvedInstallationRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationRoot));
        var resolvedModulesRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(validatedModulesRoot));
        if (!Directory.Exists(resolvedInstallationRoot))
        {
            throw new DirectoryNotFoundException($"Host installation root not found: {resolvedInstallationRoot}");
        }

        if (!Directory.Exists(resolvedModulesRoot))
        {
            throw new DirectoryNotFoundException($"Built-in module root not found: {resolvedModulesRoot}");
        }

        if (!PathContainment.Contains(resolvedInstallationRoot, resolvedModulesRoot, allowRoot: false))
        {
            throw new InvalidOperationException(
                $"Built-in module root must be contained by the Host installation root: {resolvedModulesRoot}");
        }

        PathContainment.RejectReparsePoints(resolvedInstallationRoot);
        PathContainment.RejectReparsePoints(resolvedModulesRoot);
        ValidateTree(resolvedModulesRoot);
        return new TrustedBuiltInModuleSource(
            resolvedModulesRoot,
            resolvedInstallationRoot,
            beforeArtifactOpen);
    }

    // 内建根首次捕获，原生根复用接纳器已验证的固定字节。
    internal VerifiedModuleArtifactSnapshot CaptureModuleSnapshot(string moduleRoot)
    {
        ValidateModuleTree(moduleRoot);
        if (_acceptedNativeModules.TryGetValue(Path.GetFullPath(moduleRoot), out var accepted)) return accepted;
        return VerifiedModuleArtifactSnapshot.Capture(
            moduleRoot,
            relativePath => _beforeArtifactOpen?.Invoke($"module:{relativePath}"));
    }

    // 只有 Host 的原生库存适配器在核对本机接纳 receipt 与固定字节后登记，公开候选不能调用。
    internal void RegisterAcceptedNativeModule(string moduleRoot, VerifiedModuleArtifactSnapshot snapshot)
    {
        var root = Path.GetFullPath(moduleRoot);
        if (!string.Equals(root, snapshot.ModuleRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Native snapshot does not match its accepted root.");
        _acceptedNativeModules[root] = snapshot;
    }

    // 来源能力和前端运输方式分离；此事实仅由受控库存登记拥有。
    internal bool IsAcceptedNativeRoot(string moduleRoot) => _acceptedNativeModules.ContainsKey(Path.GetFullPath(moduleRoot));

    internal byte[] CaptureDistributionFile(string path)
    {
        var resolvedPath = Path.GetFullPath(path);
        _beforeArtifactOpen?.Invoke($"distribution:{resolvedPath}");
        return PathContainment.ReadContainedFile(DistributionRoot, resolvedPath);
    }

    // 原生根只能来自已签发登记；其他根继续受发行根直接子目录及链接约束保护。
    internal void ValidateModuleTree(string moduleRoot)
    {
        var resolvedModuleRoot = Path.GetFullPath(moduleRoot);
        if (_acceptedNativeModules.ContainsKey(resolvedModuleRoot)) return;
        if (!PathContainment.Contains(ModulesRoot, resolvedModuleRoot, allowRoot: false)
            || !string.Equals(
                Path.GetDirectoryName(resolvedModuleRoot),
                ModulesRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Built-in module capsule is outside the trusted module root: {resolvedModuleRoot}");
        }

        ValidateTree(resolvedModuleRoot);
    }

    internal VerifiedModuleArtifactSnapshot GetSnapshotForConsumption(CanonicalModulePlanModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (!ReferenceEquals(module.Provenance.GetAuthenticatedBuiltInSource(module), this)
            || module.Provenance.GetAuthenticatedBuiltInSnapshot(module) is not { } snapshot)
        {
            throw new InvalidOperationException(
                $"Built-in module '{module.ModuleId}' is not authenticated by this Host trust source.");
        }

        return snapshot;
    }

    internal string ValidateDistributionFile(string path)
    {
        var resolvedPath = Path.GetFullPath(path);
        if (!PathContainment.Contains(DistributionRoot, resolvedPath)
            || !File.Exists(resolvedPath))
        {
            throw new FileNotFoundException(
                "Built-in module file is missing or outside the Host installation root.",
                resolvedPath);
        }

        PathContainment.RejectReparsePoints(resolvedPath);
        return resolvedPath;
    }

    private static void ValidateTree(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            PathContainment.RejectReparsePoints(current);
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                PathContainment.RejectReparsePoints(entry);
                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                }
            }
        }
    }

}
