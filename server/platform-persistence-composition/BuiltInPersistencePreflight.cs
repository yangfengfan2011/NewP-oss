using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Planning;

namespace ICP.Platform.Persistence.Composition;

/// <summary>
/// 只读 Host Release 持久化编译检查结果；不会公开可交给 migration executor 的计划。
/// </summary>
public sealed record BuiltInPersistencePreflightResult(
    IReadOnlyList<ModulePlanDiagnostic> ModuleDiagnostics,
    IReadOnlyList<PersistencePlanDiagnostic> PersistenceDiagnostics)
{
    public bool IsSuccess => ModuleDiagnostics.Count == 0 && PersistenceDiagnostics.Count == 0;
}

/// <summary>
/// 使用 Host 唯一的 built-in trust 与正式 PersistencePlanCompiler 检查 Release 输出。
/// </summary>
public static class BuiltInPersistencePreflight
{
    /// <summary>
    /// 冻结 Host Release 内建资产并编译指定 provider；返回 diagnostics，不连接数据库或执行 migration。
    /// </summary>
    public static BuiltInPersistencePreflightResult Check(
        string hostReleaseRoot,
        PersistenceProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostReleaseRoot);
        var resolvedHostRoot = Path.GetFullPath(hostReleaseRoot);
        var modulesRoot = Path.Combine(resolvedHostRoot, "modules");
        var typeMapPath = Path.Combine(
            resolvedHostRoot,
            "server",
            "platform-modeling",
            "metadata",
            "storage-types.json");

        var source = TrustedBuiltInModuleSource.CreateForHostDistribution(resolvedHostRoot, modulesRoot);
        var candidates = new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(source);
        var moduleCompilation = new CanonicalModulePlanCompiler().Compile(
            "migration-manifest-preflight",
            candidates,
            source.PlatformCompositionModuleId);
        if (!moduleCompilation.IsSuccess || moduleCompilation.Plan is null)
        {
            return new BuiltInPersistencePreflightResult(
                moduleCompilation.Diagnostics,
                Array.Empty<PersistencePlanDiagnostic>());
        }

        var persistenceCompilation = new PersistencePlanCompiler(typeMapPath, resolvedHostRoot)
            .Compile(moduleCompilation.Plan, provider);
        return new BuiltInPersistencePreflightResult(
            Array.Empty<ModulePlanDiagnostic>(),
            persistenceCompilation.Diagnostics);
    }
}
