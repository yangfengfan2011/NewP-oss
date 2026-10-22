using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.PluginPlatform.ComponentBundles;

/// <summary>受控平台运行时发行；业务提交和组件版本不参与此身份。</summary>
public sealed record PlatformRuntimeRelease(string Version, string ContentDigest);

/// <summary>由实际资产生成的单组件索引；自有内容不包含成员资产。</summary>
public sealed record ComponentBundleMember(string ModuleId, string Version, string ContentDigest, string Root,
    IReadOnlyDictionary<string, string> Files);

/// <summary>内容寻址的交付索引，不表达安装、信任、选版或活动状态。</summary>
public sealed record ComponentBundleManifest(int SchemaVersion, string RootModuleId, PlatformRuntimeRelease PlatformRuntime,
    string ContentDigest, IReadOnlyList<ComponentBundleMember> Components);

/// <summary>正式构建得到的物理产物；程序集位置由 MSBuild TargetPath 提供。</summary>
public sealed record ComponentBuiltArtifacts(string? BackendAssembly, string? FrontendRoot,
    IReadOnlyDictionary<string, string> MigrationAssemblies, IReadOnlyDictionary<string, string> MigrationSources);

/// <summary>完整核验后的消费输入；候选来源描述不授予原生信任。</summary>
public sealed record ComponentBundleInspection(ComponentBundleManifest Manifest, IReadOnlyList<ModuleCapsuleCandidate> Components);
