namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>
/// 由 Host composition root 签发的内建模块根；普通库调用方不能把任意目录提升为内建来源。
/// </summary>
internal sealed class TrustedBuiltInModuleSource
{
    private readonly Action<string>? _beforeArtifactOpen;

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

    internal VerifiedModuleArtifactSnapshot CaptureModuleSnapshot(string moduleRoot)
    {
        ValidateModuleTree(moduleRoot);
        return VerifiedModuleArtifactSnapshot.Capture(
            moduleRoot,
            relativePath => _beforeArtifactOpen?.Invoke($"module:{relativePath}"));
    }

    internal byte[] CaptureDistributionFile(string path)
    {
        var resolvedPath = Path.GetFullPath(path);
        _beforeArtifactOpen?.Invoke($"distribution:{resolvedPath}");
        return PathContainment.ReadContainedFile(DistributionRoot, resolvedPath);
    }

    internal void ValidateModuleTree(string moduleRoot)
    {
        var resolvedModuleRoot = Path.GetFullPath(moduleRoot);
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
