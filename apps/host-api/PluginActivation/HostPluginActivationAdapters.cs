using System.Reflection;
using ICP.Platform.HostApi.Extensions;
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.PluginPlatform.Activation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.HostApi.PluginActivation;

public sealed class HostPluginPermissionCatalogValidator : IPluginPermissionCatalogValidator
{
    public void Validate(CanonicalModulePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in plan.Modules)
        {
            foreach (var code in module.PermissionCodes)
            {
                if (string.IsNullOrWhiteSpace(code))
                {
                    throw new InvalidDataException($"Module '{module.ModuleId}' declares an empty permission code.");
                }

                if (owners.TryGetValue(code, out var owner))
                {
                    throw new InvalidDataException(
                        $"Permission '{code}' is declared by both '{owner}' and '{module.ModuleId}'.");
                }

                owners[code] = module.ModuleId;
            }
        }
    }
}

public sealed class HostPluginPersistencePlanCompiler : IPluginPersistencePlanCompiler
{
    private readonly string _storageTypeMapPath;
    private readonly PersistenceProvider _provider;

    public HostPluginPersistencePlanCompiler(string storageTypeMapPath, PersistenceProvider provider)
    {
        _storageTypeMapPath = storageTypeMapPath;
        _provider = provider;
    }

    public CompiledPersistencePlan Compile(CanonicalModulePlan modulePlan)
    {
        var result = new PersistencePlanCompiler(_storageTypeMapPath).Compile(modulePlan, _provider);
        if (result.IsSuccess && result.Plan is not null)
        {
            return result.Plan;
        }

        throw new InvalidDataException(string.Join(
            Environment.NewLine,
            result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
    }
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

public sealed class CanonicalModuleAssemblyLoader
{
    private readonly string _distributionRoot;

    public CanonicalModuleAssemblyLoader(string distributionRoot)
    {
        _distributionRoot = Path.GetFullPath(distributionRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public IReadOnlyList<Assembly> Load(CanonicalModulePlan plan)
    {
        return plan.Modules
            .Where(static module => !string.IsNullOrWhiteSpace(module.BackendEntry))
            .Select(LoadModule)
            .DistinctBy(static assembly => assembly.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private Assembly LoadModule(CanonicalModulePlanModule module)
    {
        var isBuiltIn = string.Equals(module.Provenance.SourceKind, "built-in", StringComparison.Ordinal);
        var boundary = isBuiltIn
            ? _distributionRoot
            : Path.GetFullPath(Path.Combine(module.PackageRoot, "backend"));
        var path = Path.GetFullPath(Path.Combine(
            isBuiltIn ? module.PackageRoot : boundary,
            module.BackendEntry!));
        if (!IsContainedPath(boundary, path)
            || !File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Canonical backend entry for '{module.ModuleId}' is missing or outside its load boundary.",
                path);
        }

        var assembly = Assembly.LoadFrom(path);
        ValidateModuleContract(module, assembly);
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

    private static bool IsContainedPath(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relativePath)
            && !string.Equals(relativePath, "..", StringComparison.Ordinal)
            && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}

/// <summary>
/// standard-data 模块没有可执行程序集；该投影只把活动身份与权限放入 Host 注册表。
/// </summary>
public sealed class ProjectedStandardDataModule : IModule, IPermissionProvider
{
    private readonly IReadOnlyList<PermissionDescriptor> _permissions;

    public ProjectedStandardDataModule(CanonicalModulePlanModule module)
    {
        ModuleId = module.ModuleId;
        DisplayName = module.DisplayName;
        Version = module.Version;
        _permissions = module.PermissionCodes
            .Select(code => new PermissionDescriptor
            {
                Code = code,
                DisplayName = code,
                Description = "由 standard-data.v1 活动投影声明。",
                ModuleId = module.ModuleId,
                Group = "标准数据"
            })
            .ToArray();
    }

    public string ModuleId { get; }

    public string DisplayName { get; }

    public string Version { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        _ = services;
    }

    public IReadOnlyList<PermissionDescriptor> GetPermissions() => _permissions;
}
