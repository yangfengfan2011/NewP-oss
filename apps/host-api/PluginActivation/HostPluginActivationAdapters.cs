using System.Reflection;
using System.Runtime.Loader;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Kernel.ModuleLoading;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.PluginPlatform.Activation;
using ICP.Platform.PluginPlatform.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.HostApi.PluginActivation;

/// <summary>把 Host 的持久化编译、恢复输入和最终确认绑定到同一部署存储。</summary>
public sealed class HostPluginActivationDeployment : IPluginActivationDeployment
{
    private readonly string _storageTypeMapPath;
    private readonly PersistenceProvider _provider;
    private readonly PersistenceDeploymentStore? _deploymentStore;

    // 仅显式工厂可选择是否要求最终确认，仓内消费者没有省略策略的构造入口。
    private HostPluginActivationDeployment(string storageTypeMapPath, PersistenceProvider provider, PersistenceDeploymentStore? deploymentStore)
    {
        _storageTypeMapPath = storageTypeMapPath;
        _provider = provider;
        _deploymentStore = deploymentStore;
    }

    /// <summary>绑定当前 provider/catalog 的存储；编译、提交与活动读取共用此实例。</summary>
    public static HostPluginActivationDeployment WithDeploymentStore(
        string storageTypeMapPath, PersistenceProvider provider, PersistenceDeploymentStore deploymentStore)
    {
        ArgumentNullException.ThrowIfNull(deploymentStore);
        return new HostPluginActivationDeployment(storageTypeMapPath, provider, deploymentStore);
    }

    /// <summary>明确选择既有无部署确认分支；不扩大对应 provider 的支持边界。</summary>
    public static HostPluginActivationDeployment WithoutDeploymentConfirmation(
        string storageTypeMapPath, PersistenceProvider provider) =>
        new(storageTypeMapPath, provider, deploymentStore: null);

    /// <inheritdoc/>
    public CompiledPersistencePlan Compile(CanonicalModulePlan modulePlan)
    {
        var result = new PersistencePlanCompiler(_storageTypeMapPath).Compile(modulePlan, _provider, _deploymentStore?.ReadRecoveryCandidate());
        if (result.IsSuccess && result.Plan is not null)
        {
            return result.Plan;
        }

        throw new InvalidDataException(string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
    }

    /// <inheritdoc/>
    public void PrepareCandidate(CanonicalModulePlan modulePlan, CompiledPersistencePlan persistencePlan) =>
        _deploymentStore?.PrepareCandidate(modulePlan, persistencePlan);

    /// <inheritdoc/>
    public void CompleteCandidate(CompiledPersistencePlan persistencePlan) =>
        _deploymentStore?.CompleteCandidate(persistencePlan);

    /// <inheritdoc/>
    public bool IsSnapshotCommitted(PluginActiveSnapshot snapshot) =>
        _deploymentStore is null || _deploymentStore.IsSuccessfulDeployment(
            snapshot.SnapshotId, snapshot.PlanHash, snapshot.PersistencePlanDigest);
}

public sealed class HostPluginRuntimeProjection : IPluginRuntimeProjection
{
    private readonly RuntimeViewMaterializer _materializer;
    private readonly string _installRoot;

    public HostPluginRuntimeProjection(RuntimeViewMaterializer materializer, string installRoot)
    {
        _materializer = materializer;
        _installRoot = installRoot;
    }

    /// <inheritdoc/>
    public void Validate(CanonicalModulePlan plan) => _materializer.ValidateModelMetadata(plan);

    public IPluginRuntimeStage Stage(CanonicalModulePlan plan) =>
        new StageAdapter(_materializer.Stage(_installRoot, plan));

    private sealed class StageAdapter : IPluginRuntimeStage
    {
        private readonly RuntimeViewProjectionStage _stage;

        public StageAdapter(RuntimeViewProjectionStage stage)
        {
            _stage = stage;
        }

        public IPluginRuntimeCommit BeginCommit() => new CommitAdapter(_stage.BeginCommit());

        public void Dispose() => _stage.Dispose();
    }

    private sealed class CommitAdapter : IPluginRuntimeCommit
    {
        private readonly RuntimeViewProjectionCommit _commit;

        public CommitAdapter(RuntimeViewProjectionCommit commit)
        {
            _commit = commit;
        }

        public void Complete() => _commit.Complete();

        public void Dispose() => _commit.Dispose();
    }
}

public sealed class HostPluginMigrationExecutor : IPluginMigrationExecutor
{
    private readonly WebApplication _app;
    private readonly CompiledPersistencePlan _registeredPlan;

    public HostPluginMigrationExecutor(WebApplication app, CompiledPersistencePlan registeredPlan)
    {
        _app = app;
        _registeredPlan = registeredPlan;
    }

    public bool DatabaseAhead { get; private set; }

    public async Task ExecuteAsync(CompiledPersistencePlan plan, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(plan, _registeredPlan))
        {
            throw new InvalidOperationException("Host DI is not bound to the activation session persistence plan instance.");
        }

        await _app.ApplyPlatformPersistenceAsync(
            cancellationToken,
            databaseAhead => DatabaseAhead = databaseAhead);
    }
}

public sealed class HostPluginPermissionSynchronizer : IPluginPermissionSynchronizer
{
    private readonly Func<CancellationToken, Task> _synchronize;

    public HostPluginPermissionSynchronizer(Func<CancellationToken, Task> synchronize)
    {
        _synchronize = synchronize;
    }

    public Task SynchronizeAsync(CanonicalModulePlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return _synchronize(cancellationToken);
    }
}

public sealed class TrustedDefaultModuleAssemblyRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, TrustedDefaultModuleAssembly> _assemblies =
        new(StringComparer.OrdinalIgnoreCase);

    internal Assembly? LoadAtTrustedComposition(
        string owner,
        VerifiedAssemblyIdentity identity,
        byte[] verifiedImage)
    {
        var name = identity.AssemblyName.Name
            ?? throw new InvalidDataException($"Canonical backend assembly for '{owner}' has no name.");
        lock (_gate)
        {
            if (_assemblies.TryGetValue(name, out var registered))
            {
                if (!registered.Image.AsSpan().SequenceEqual(verifiedImage)
                    || !CanonicalModuleAssemblyLoader.MatchesIdentity(registered.Assembly, identity))
                {
                    throw new InvalidDataException(
                        $"Trusted Default module registry contains conflicting bytes for '{owner}'.");
                }

                return registered.Assembly;
            }

            if (AssemblyLoadContext.Default.Assemblies.Any(assembly =>
                    CanonicalModuleAssemblyLoader.MatchesIdentity(assembly, identity)))
            {
                // Registry 没有亲自从当前签发字节装载的 Default 主体没有内存映像证明；
                // 即使 identity/MVID/Location 都一致也只能退回隔离装载，绝不执行该预载主体。
                return null;
            }

            Assembly? observedSubject = null;
            var loadingThread = Environment.CurrentManagedThreadId;
            AssemblyLoadEventHandler observer = (_, args) =>
            {
                if (Environment.CurrentManagedThreadId == loadingThread
                    && CanonicalModuleAssemblyLoader.MatchesIdentity(args.LoadedAssembly, identity))
                {
                    observedSubject = args.LoadedAssembly;
                }
            };
            AppDomain.CurrentDomain.AssemblyLoad += observer;
            try
            {
                using var stream = new MemoryStream(verifiedImage, writable: false);
                var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
                if (!ReferenceEquals(assembly, observedSubject)
                    || !CanonicalModuleAssemblyLoader.MatchesIdentity(assembly, identity))
                {
                    throw new InvalidDataException(
                        $"Trusted Default module registry could not prove the loaded image for '{owner}'.");
                }

                _assemblies.Add(name, new TrustedDefaultModuleAssembly(assembly, verifiedImage.ToArray()));
                return assembly;
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyLoad -= observer;
            }
        }
    }

    private sealed record TrustedDefaultModuleAssembly(Assembly Assembly, byte[] Image);
}

public sealed class CanonicalModuleAssemblyLoader
{
    private readonly string _distributionRoot;
    private readonly TrustedDefaultModuleAssemblyRegistry? _trustedDefaultRegistry;

    public CanonicalModuleAssemblyLoader(
        string distributionRoot,
        TrustedDefaultModuleAssemblyRegistry? trustedDefaultRegistry = null)
    {
        _distributionRoot = Path.GetFullPath(distributionRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _trustedDefaultRegistry = trustedDefaultRegistry;
    }

    public IReadOnlyList<Assembly> Load(CanonicalModulePlan plan)
    {
        var images = plan.Modules
            .Where(static module => !string.IsNullOrWhiteSpace(module.BackendEntry))
            .Select(ReadModuleImage)
            .ToArray();
        var context = new VerifiedModuleAssemblyLoadContext(images, _distributionRoot);
        return images
            .Select(image => LoadModule(image, context))
            .DistinctBy(static assembly => assembly.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    // 主体只消费计划签发字节；原生包边界独立于 Host 发行目录，不能退回路径重读。
    private VerifiedModuleImage ReadModuleImage(CanonicalModulePlanModule module)
    {
        var isBuiltIn = module.Provenance.AuthenticatesBuiltIn(module);
        var nativePackage = isBuiltIn && module.Provenance.GetAuthenticatedBuiltInSource(module)!.IsAcceptedNativeRoot(module.PackageRoot);
        var boundary = isBuiltIn && !nativePackage
            ? _distributionRoot
            : nativePackage ? module.PackageRoot : Path.GetFullPath(Path.Combine(module.PackageRoot, "backend"));
        var path = Path.GetFullPath(Path.Combine(
            isBuiltIn ? module.PackageRoot : boundary,
            module.BackendEntry!));
        if (!PathContainment.Contains(boundary, path)
            || (!isBuiltIn && !File.Exists(path)))
        {
            throw new FileNotFoundException(
                $"Canonical backend entry for '{module.ModuleId}' is missing or outside its load boundary.",
                path);
        }

        var image = isBuiltIn
            ? module.Provenance.GetAuthenticatedBuiltInSnapshot(module)!
                .ReadDistributionFile(module.BackendEntry!)
            : PathContainment.ReadContainedFile(boundary, path);
        return new VerifiedModuleImage(module, image, isBuiltIn);
    }

    private Assembly LoadModule(
        VerifiedModuleImage image,
        VerifiedModuleAssemblyLoadContext context)
    {
        var identity = VerifiedAssemblyIdentityReader.Read(image.Bytes);
        var assembly = image.IsBuiltIn
            ? _trustedDefaultRegistry?.LoadAtTrustedComposition(
                image.Module.ModuleId,
                identity,
                image.Bytes)
                ?? context.LoadVerifiedImage(image.Module.ModuleId, image.Bytes)
            : context.LoadVerifiedImage(image.Module.ModuleId, image.Bytes);
        ValidateModuleContract(image.Module, assembly);
        return assembly;
    }

    private static void ValidateModuleContract(CanonicalModulePlanModule planned, Assembly assembly)
    {
        var moduleTypes = assembly.GetTypes()
            .Where(type => typeof(IModule).IsAssignableFrom(type)
                && !type.IsInterface
                && !type.IsAbstract)
            .ToArray();
        if (moduleTypes.Length != 1
            || Activator.CreateInstance(moduleTypes[0]) is not IModule actual)
        {
            throw new InvalidDataException(
                $"Canonical backend assembly for '{planned.ModuleId}' must expose exactly one constructible IModule.");
        }

        if (!string.Equals(actual.ModuleId, planned.ModuleId, StringComparison.Ordinal)
            || !string.Equals(actual.Version, planned.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Canonical backend assembly identity '{actual.ModuleId}@{actual.Version}' does not match '{planned.ModuleId}@{planned.Version}'.");
        }

    }

    private sealed class VerifiedModuleAssemblyLoadContext
        : AssemblyLoadContext
    {
        private readonly IReadOnlyDictionary<string, VerifiedDependencyImage> _verifiedImages;
        private readonly string _distributionRoot;

        internal VerifiedModuleAssemblyLoadContext(
            IReadOnlyList<VerifiedModuleImage> images,
            string distributionRoot)
            : base(isCollectible: false)
        {
            _distributionRoot = distributionRoot;
            _verifiedImages = images
                .Select(image => new
                {
                    Identity = VerifiedAssemblyIdentityReader.Read(image.Bytes),
                    image.Bytes,
                })
                .GroupBy(static image => image.Identity.AssemblyName.Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static group => group.Key,
                    group => group
                        .Select(static image => new VerifiedDependencyImage(image.Identity, image.Bytes))
                        .Distinct(VerifiedDependencyImageComparer.Instance)
                        .Single(),
                    StringComparer.OrdinalIgnoreCase);
        }

        internal Assembly LoadVerifiedImage(string owner, byte[] image)
        {
            var identity = VerifiedAssemblyIdentityReader.Read(image);
            var existing = Assemblies.FirstOrDefault(assembly =>
                MatchesIdentity(assembly, identity));
            if (existing is not null)
            {
                return existing;
            }

            // 主体只从 Canonical Plan 已固定的字节装载。Default ALC 中同 identity/MVID 的主体
            // 可能来自更早的恶意预载，事后读取 Location 不能证明内存中的实际 IL。
            using var loadStream = new MemoryStream(image, writable: false);
            var assembly = LoadFromStream(loadStream);
            if (!MatchesIdentity(assembly, identity))
            {
                throw new InvalidDataException(
                    $"Canonical backend assembly for '{owner}' differs from its verified artifact image.");
            }

            return assembly;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is { } name && _verifiedImages.TryGetValue(name, out var image))
            {
                if (!VerifiedAssemblyIdentityPolicy.MatchesExact(image.Identity.AssemblyName, assemblyName))
                {
                    throw new FileLoadException($"Verified module dependency '{assemblyName.FullName}' does not match the planned image identity.");
                }

                return LoadVerifiedImage(name, image.Bytes);
            }

            if (!VerifiedAssemblyIdentityPolicy.IsHostPlatformSharedDependency(assemblyName))
            {
                throw new FileLoadException($"Module dependency '{assemblyName.FullName}' is outside the Host/platform shared dependency boundary.");
            }

            return ResolveSharedDependency(assemblyName);
        }

        private Assembly ResolveSharedDependency(AssemblyName assemblyName)
        {
            var loaded = Default.Assemblies.FirstOrDefault(candidate =>
                VerifiedAssemblyIdentityPolicy.MatchesExact(candidate.GetName(), assemblyName));
            if (loaded is not null)
            {
                return loaded;
            }

            try
            {
                var resolved = Default.LoadFromAssemblyName(assemblyName);
                if (!VerifiedAssemblyIdentityPolicy.MatchesExact(resolved.GetName(), assemblyName))
                {
                    throw new FileLoadException($"Resolved Host/platform shared dependency '{assemblyName.FullName}' has a different identity.");
                }

                return resolved;
            }
            catch (FileNotFoundException)
            {
                // 非 shared-framework 平台程序集继续从受控 distribution root 的字节装载。
            }

            var path = Path.GetFullPath(Path.Combine(_distributionRoot, $"{assemblyName.Name}.dll"));
            if (!PathContainment.Contains(_distributionRoot, path) || !File.Exists(path))
            {
                throw new FileLoadException($"Exact Host/platform shared dependency '{assemblyName.FullName}' is unavailable.");
            }

            var bytes = PathContainment.ReadContainedFile(_distributionRoot, path);
            var identity = VerifiedAssemblyIdentityReader.Read(bytes);
            if (!VerifiedAssemblyIdentityPolicy.MatchesExact(identity.AssemblyName, assemblyName))
            {
                throw new FileLoadException($"Host/platform shared dependency '{assemblyName.FullName}' differs from its distribution image.");
            }

            using var stream = new MemoryStream(bytes, writable: false);
            var assembly = Default.LoadFromStream(stream);
            if (!VerifiedAssemblyIdentityPolicy.MatchesExact(assembly.GetName(), assemblyName)
                || assembly.ManifestModule.ModuleVersionId != identity.Mvid)
            {
                throw new FileLoadException($"Loaded Host/platform shared dependency '{assemblyName.FullName}' differs from its distribution image.");
            }

            return assembly;
        }
    }

    internal static bool MatchesIdentity(Assembly assembly, VerifiedAssemblyIdentity expected) =>
        VerifiedAssemblyIdentityPolicy.MatchesExact(assembly.GetName(), expected.AssemblyName)
        && assembly.ManifestModule.ModuleVersionId == expected.Mvid;

    private sealed record VerifiedDependencyImage(VerifiedAssemblyIdentity Identity, byte[] Bytes);

    private sealed record VerifiedModuleImage(CanonicalModulePlanModule Module, byte[] Bytes, bool IsBuiltIn);

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        internal static ByteArrayComparer Instance { get; } = new();

        public bool Equals(byte[]? left, byte[]? right) =>
            left is not null && right is not null && left.AsSpan().SequenceEqual(right);

        public int GetHashCode(byte[] value) =>
            value.Length == 0 ? 0 : HashCode.Combine(value.Length, value[0], value[^1]);
    }

    private sealed class VerifiedDependencyImageComparer : IEqualityComparer<VerifiedDependencyImage>
    {
        internal static VerifiedDependencyImageComparer Instance { get; } = new();

        public bool Equals(VerifiedDependencyImage? left, VerifiedDependencyImage? right) =>
            left is not null
            && right is not null
            && VerifiedAssemblyIdentityPolicy.MatchesExact(left.Identity.AssemblyName, right.Identity.AssemblyName)
            && left.Identity.Mvid == right.Identity.Mvid
            && ByteArrayComparer.Instance.Equals(left.Bytes, right.Bytes);

        public int GetHashCode(VerifiedDependencyImage value) =>
            ByteArrayComparer.Instance.GetHashCode(value.Bytes);
    }

}

/// <summary>
/// standard-data 模块没有可执行程序集；该投影只把活动身份放入 Host 注册表。
/// </summary>
public sealed class ProjectedStandardDataModule : IModule
{

    public ProjectedStandardDataModule(CanonicalModulePlanModule module)
    {
        ModuleId = module.ModuleId;
        DisplayName = module.DisplayName;
        Version = module.Version;
    }

    public string ModuleId { get; }

    public string DisplayName { get; }

    public string Version { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        _ = services;
    }

}
