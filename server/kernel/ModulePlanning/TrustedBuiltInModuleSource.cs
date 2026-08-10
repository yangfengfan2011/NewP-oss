namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>
/// 由 Host composition root 签发的内建模块根；普通库调用方不能把任意目录提升为内建来源。
/// </summary>
internal sealed class TrustedBuiltInModuleSource
{
    private TrustedBuiltInModuleSource(string modulesRoot, string installationRoot)
    {
        ModulesRoot = modulesRoot;
        DistributionRoot = installationRoot;
    }

    internal string ModulesRoot { get; }

    internal string DistributionRoot { get; }

    internal static TrustedBuiltInModuleSource CreateForHost(
        string installationRoot,
        string validatedModulesRoot)
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
        return new TrustedBuiltInModuleSource(resolvedModulesRoot, resolvedInstallationRoot);
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

    /// <summary>
    /// 在已签发计划实际消费内建模块前，重新验证 capability 绑定与完整模块树。
    /// </summary>
    internal void ValidateForConsumption(CanonicalModulePlanModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (!ReferenceEquals(module.Provenance.GetAuthenticatedBuiltInSource(module), this))
        {
            throw new InvalidOperationException(
                $"Built-in module '{module.ModuleId}' is not authenticated by this Host trust source.");
        }

        ValidateModuleTree(module.PackageRoot);
    }

    /// <summary>
    /// 在读取模块内文件的同一消费入口复验整树、路径边界与最终文件路径链。
    /// </summary>
    internal string ValidateModuleFileForConsumption(
        CanonicalModulePlanModule module,
        string relativePath)
    {
        ValidateForConsumption(module);
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException(
                $"Built-in module artifact path '{relativePath}' must be package-relative.");
        }

        var moduleRoot = Path.GetFullPath(module.PackageRoot);
        var resolvedPath = Path.GetFullPath(Path.Combine(moduleRoot, relativePath));
        if (!PathContainment.Contains(moduleRoot, resolvedPath) || !File.Exists(resolvedPath))
        {
            throw new FileNotFoundException(
                "Built-in module artifact is missing or outside the trusted module root.",
                resolvedPath);
        }

        PathContainment.RejectReparsePoints(resolvedPath);
        return resolvedPath;
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
