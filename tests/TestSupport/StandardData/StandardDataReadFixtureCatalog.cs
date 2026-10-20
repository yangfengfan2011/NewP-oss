using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Testing;

/// <summary>仅为字段和metadata测试保留正式动作目录；这些候选不可运行，也不是authenticated builtin或判权证据。</summary>
internal static class StandardDataReadFixtureCatalog
{
    internal static TrustedBuiltInModuleSource Source()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var repository = output;
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "ICP-Platform.sln"))) repository = repository.Parent;
        if (repository is null) throw new DirectoryNotFoundException("Repository root not found.");
        var host = Path.Combine(repository.FullName, "apps", "host-api", "bin", output.Parent!.Name, output.Name);
        return TrustedBuiltInModuleSource.CreateForHostDistribution(host, Path.Combine(host, "modules"));
    }

    internal static CanonicalModulePlan Compile(ModuleCapsuleCandidate candidate, string catalogRoot)
    {
        Directory.CreateDirectory(catalogRoot);
        var catalog = new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(Source())
            .Where(module => module.DataActions.Count > 0)
            .Select(module => new ModuleCapsuleCandidate(module.ModuleId, module.Version, module.DisplayName,
                "Test-only action catalog; not a runnable builtin.", catalogRoot, null, null, null, null,
                "behavior-bindings", [], [], [], [], [], new("test-catalog", Path.Combine(catalogRoot, "catalog.json"), "/"))
            { DataActions = module.DataActions.ToArray(), BackendBehaviorKey = module.BackendBehaviorKey, DataExecutionProviders = module.DataExecutionProviders.ToArray() });
        var result = new CanonicalModulePlanCompiler().Compile("standard-data-read-fixture", catalog.Append(candidate));
        return result.Plan ?? throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Code + ": " + item.Message)));
    }
}
