using ICP.Platform.HostApi.PluginActivation;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.PluginPlatform.Activation;
using ICP.Platform.PluginPlatform.Lifecycle;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class HostPluginActivationDeploymentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "newp-activation-deployment-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SqlDeployment_BindsRecoveryAndConfirmationToTheSameCatalogAcrossRestart()
    {
        var modules = ReadFormalCandidate();
        CompiledPersistencePlan first;
        CompiledPersistencePlan second;
        var masked = ReadFormalCandidate(maskBusinessComposition: true);
        using (var store = new PersistenceDeploymentStore(Path.Combine(_root, "persistence"), PersistenceProvider.SqlServer, "SERVER|CATALOG"))
        {
            IPluginActivationDeployment deployment = HostPluginActivationDeployment.WithDeploymentStore(StorageTypeMap(), PersistenceProvider.SqlServer, store);
            var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(_root, deployment));
            first = deployment.Compile(modules);
            deployment.PrepareCandidate(modules, first);
            manager.CommitActive([], Snapshot(first, modules));
            Assert.Null(manager.Resolve([]).ActiveSnapshot);
            deployment.CompleteCandidate(first);
            Assert.Equal(first.SnapshotId, manager.Resolve([]).ActiveSnapshot!.SnapshotId);

            // 屏蔽真实业务组合时，恢复输入仍来自该 adapter 的同一 catalog，而不是另拼编译器。
            second = deployment.Compile(masked);
            Assert.Equal(new[] { "Document", "Part", "Workspace" }, second.RetainedModuleIds);
            Assert.Equal(first.Models.Select(model => (model.Name, model.OwnerModuleId, model.TableName)),
                second.Models.Select(model => (model.Name, model.OwnerModuleId, model.TableName)));
            deployment.PrepareCandidate(masked, second);
            manager.CommitActive([], Snapshot(second, masked));
            Assert.Null(manager.Resolve([]).ActiveSnapshot);
            Assert.Equal(first.SnapshotId, manager.Resolve([]).LastKnownGoodSnapshot!.SnapshotId);
            deployment.CompleteCandidate(second);
        }

        using var restartedStore = new PersistenceDeploymentStore(Path.Combine(_root, "persistence"), PersistenceProvider.SqlServer, "SERVER|CATALOG");
        IPluginActivationDeployment restarted = HostPluginActivationDeployment.WithDeploymentStore(StorageTypeMap(), PersistenceProvider.SqlServer, restartedStore);
        var state = new PluginLifecycleManager(new PluginLifecycleStateStore(_root, restarted)).Resolve([]);
        Assert.Equal(second.SnapshotId, state.ActiveSnapshot!.SnapshotId);
        Assert.Equal(first.SnapshotId, state.LastKnownGoodSnapshot!.SnapshotId);
        Assert.Equal(second.PlanDigest, restarted.Compile(masked).PlanDigest);

        using var otherStore = new PersistenceDeploymentStore(Path.Combine(_root, "persistence"), PersistenceProvider.SqlServer, "SERVER|OTHER_CATALOG");
        IPluginActivationDeployment other = HostPluginActivationDeployment.WithDeploymentStore(StorageTypeMap(), PersistenceProvider.SqlServer, otherStore);
        var otherState = new PluginLifecycleManager(new PluginLifecycleStateStore(_root, other)).Resolve([]);
        Assert.Null(otherState.ActiveSnapshot);
        Assert.Null(otherState.LastKnownGoodSnapshot);
        Assert.Empty(other.Compile(masked).RetainedModuleIds);
    }

    [Fact]
    public void WithoutDeploymentConfirmation_ExplicitlyPreservesRawLifecycleSemantics()
    {
        var modules = ReadFormalCandidate();
        IPluginActivationDeployment deployment = HostPluginActivationDeployment.WithoutDeploymentConfirmation(StorageTypeMap(), PersistenceProvider.SqlServer);
        var plan = deployment.Compile(modules);
        var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(_root, deployment));
        deployment.PrepareCandidate(modules, plan);
        manager.CommitActive([], Snapshot(plan, modules));
        deployment.CompleteCandidate(plan);

        Assert.Equal(plan.SnapshotId, manager.Resolve([]).ActiveSnapshot!.SnapshotId);
        Assert.Contains(plan.Models, model => model.OwnerModuleId == "Workspace");
        Assert.False(Directory.Exists(Path.Combine(_root, "persistence")));
    }

    private static PluginActiveSnapshot Snapshot(CompiledPersistencePlan plan, CanonicalModulePlan modules) =>
        new(plan.SnapshotId, plan.ModulePlanHash, plan.PlanDigest,
            modules.Modules.Select(module => new PluginActiveModule(module.ModuleId, module.Version, module.Provenance.SourceKind)).ToArray());

    // 读取正式 Release 发布物的 Workspace、Part、Document authoring，不构造替代业务模块。
    private static CanonicalModulePlan ReadFormalCandidate(bool maskBusinessComposition = false)
    {
        var hostRoot = HostRoot();
        var source = TrustedBuiltInModuleSource.CreateForHostDistribution(hostRoot, Path.Combine(hostRoot, "modules"));
        var candidates = new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(source);
        var result = new CanonicalModulePlanCompiler().Compile("activation-deployment",
            maskBusinessComposition ? candidates.Select(candidate => candidate.ModuleId == "InforCenter" ? candidate with { Enabled = false } : candidate) : candidates,
            source.PlatformCompositionModuleId);
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        return result.Plan!;
    }

    private static string StorageTypeMap() => Path.Combine(HostRoot(), "server/platform-modeling/metadata/storage-types.json");

    private static string HostRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ICP-Platform.sln"))) root = root.Parent;
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        return Path.Combine(root!.FullName, "apps/host-api/bin", output.Parent!.Name, output.Name);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
