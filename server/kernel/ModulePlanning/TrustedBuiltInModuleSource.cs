namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>
/// 由 Host composition root 签发的内建模块根；普通库调用方不能把任意目录提升为内建来源。
/// </summary>
internal sealed class TrustedBuiltInModuleSource
{
    private TrustedBuiltInModuleSource(string modulesRoot, string distributionRoot)
    {
        ModulesRoot = modulesRoot;
        DistributionRoot = distributionRoot;
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

        RejectReparsePoint(resolvedModulesRoot);
        foreach (var moduleRoot in Directory.EnumerateDirectories(resolvedModulesRoot))
        {
            RejectReparsePoint(moduleRoot);
        }

        var distributionRoot = Path.GetDirectoryName(resolvedModulesRoot)
            ?? throw new InvalidOperationException("Built-in module root must have a distribution parent.");
        return new TrustedBuiltInModuleSource(resolvedModulesRoot, distributionRoot);
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException($"Built-in module root cannot traverse a reparse point: {path}");
        }
    }
}
