using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class PersistenceDeploymentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "newp-deployment-262-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void DeployedRealPart_CannotChangeItsTechnicalIdentityOnReactivation()
    {
        var deployed = Compile(ReadFormalCandidate());
        var root = CopyFormalHost();
        RenameModel(root, "Part", "Part", "RenamedPart");
        var current = ReadFormalCandidate(hostRoot: root);
        var result = Compiler().Compile(current, PersistenceProvider.SqlServer, deployed);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "PERSISTENCE_DEPLOYED_MODEL_IDENTITY_CHANGED" && diagnostic.ModuleId == "Part" && diagnostic.CanonicalKey == "Part");
    }

    [Fact]
    public void RetainedRealPart_ReservesItsGlobalNameAndPhysicalTargetAgainstAnotherOwner()
    {
        var deployed = Compile(ReadFormalCandidate());
        var root = CopyFormalHost();
        RenameModel(root, "Document", "Document", "Part");
        var current = ReadFormalCandidate(candidate => candidate.ModuleId is "Workspace" or "Part" ? candidate with { Enabled = false } : candidate, root);
        var result = Compiler().Compile(current, PersistenceProvider.SqlServer, deployed);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "PERSISTENCE_DEPLOYED_IDENTITY_CONFLICT" && diagnostic.CanonicalKey == "Part");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "PERSISTENCE_PHYSICAL_OWNER_CONFLICT" && diagnostic.CanonicalKey == "dbo.Part");
    }

    [Fact]
    public void FormalDeployment_PreservesVerifiedInputsAndBlocksUnfinishedOverwrite()
    {
        var modules = ReadFormalCandidate();
        var plan = Compile(modules);
        using (var store = new PersistenceDeploymentStore(_root, plan.Provider, "SERVER|CATALOG"))
        {
            Assert.Null(store.ReadDeployed());
            store.PrepareCandidate(modules, plan);
            Assert.Contains("PERSISTENCE_DEPLOYMENT_PENDING", Assert.Throws<InvalidDataException>(() => store.ReadDeployed()).Message);
            Assert.Equal(plan.PlanDigest, store.ReadRecoveryCandidate()!.PlanDigest);
            store.PrepareCandidate(modules, plan);
            using var competing = new PersistenceDeploymentStore(_root, plan.Provider, "SERVER|CATALOG");
            Assert.Throws<IOException>(() => competing.ReadDeployed());
            Assert.Throws<IOException>(() => competing.CompleteCandidate(plan));
            store.CompleteCandidate(plan);
        }
        using var restarted = new PersistenceDeploymentStore(_root, plan.Provider, "SERVER|CATALOG");
        var deployed = restarted.ReadDeployed()!;
        Assert.Equal(plan.PlanDigest, deployed.PlanDigest);
        Assert.Equal(plan.Models.Select(model => (model.Name, model.OwnerModuleId, model.SchemaName, model.TableName)), deployed.Models.Select(model => (model.Name, model.OwnerModuleId, model.SchemaName, model.TableName)));
        Assert.All(deployed.Streams, stream =>
        {
            Assert.Equal(stream.BundleChecksum, stream.AssemblyImage!.Sha256);
            Assert.NotNull(stream.ManifestImage);
        });
        using var otherCatalog = new PersistenceDeploymentStore(_root, plan.Provider, "SERVER|OTHER_CATALOG");
        Assert.Null(otherCatalog.ReadDeployed());
    }

    [Fact]
    public void SuccessfulDeploymentConfirmation_IsCatalogBoundAndKeepsPreviousSuccess()
    {
        var modules = ReadFormalCandidate();
        var first = Compile(modules);
        using var store = new PersistenceDeploymentStore(_root, first.Provider, "SERVER|CATALOG");
        store.PrepareCandidate(modules, first);
        Assert.False(store.IsSuccessfulDeployment(first.SnapshotId, first.ModulePlanHash, first.PlanDigest));
        store.CompleteCandidate(first);
        Assert.True(store.IsSuccessfulDeployment(first.SnapshotId, first.ModulePlanHash, first.PlanDigest));
        var masked = ReadFormalCandidate(candidate => candidate.ModuleId == "InforCenter" ? candidate with { Enabled = false } : candidate);
        var second = Compile(masked, store.ReadRecoveryCandidate());
        store.PrepareCandidate(masked, second);
        Assert.False(store.IsSuccessfulDeployment(second.SnapshotId, second.ModulePlanHash, second.PlanDigest));
        Assert.True(store.IsSuccessfulDeployment(first.SnapshotId, first.ModulePlanHash, first.PlanDigest));
        store.CompleteCandidate(second);
        Assert.True(store.IsSuccessfulDeployment(first.SnapshotId, first.ModulePlanHash, first.PlanDigest));
        Assert.True(store.IsSuccessfulDeployment(second.SnapshotId, second.ModulePlanHash, second.PlanDigest));
        Assert.False(store.IsSuccessfulDeployment(first.SnapshotId, first.ModulePlanHash, second.PlanDigest));
        using var otherCatalog = new PersistenceDeploymentStore(_root, first.Provider, "SERVER|OTHER");
        Assert.False(otherCatalog.IsSuccessfulDeployment(second.SnapshotId, second.ModulePlanHash, second.PlanDigest));
    }

    [Fact]
    public void ArchivedFormalAssets_TamperingCannotReplaceTrustedStorageInputs()
    {
        var modules = ReadFormalCandidate();
        var plan = Compile(modules);
        using (var store = new PersistenceDeploymentStore(_root, plan.Provider, "SERVER|CATALOG"))
        {
            store.PrepareCandidate(modules, plan);
            store.CompleteCandidate(plan);
        }
        var path = Assert.Single(Directory.GetFiles(_root, "state.json", SearchOption.AllDirectories));
        var state = JsonNode.Parse(File.ReadAllText(path))!;
        var asset = Path.Combine(Path.GetDirectoryName(path)!, state["successful"]!.GetValue<string>() + ".json");
        File.AppendAllText(asset, " ");
        using var restarted = new PersistenceDeploymentStore(_root, plan.Provider, "SERVER|CATALOG");
        Assert.Contains("PERSISTENCE_DEPLOYMENT_ASSET_INVALID", Assert.Throws<InvalidDataException>(() => restarted.ReadDeployed()).Message);
    }

    [Fact]
    public void MaskedRealWorkspace_UsesRetainedStorageForeignKeysWithoutPublishingItsCapabilities()
    {
        var deployed = Compile(ReadFormalCandidate());
        var masked = ReadFormalCandidate(candidate => candidate.ModuleId == "InforCenter" ? candidate with { Enabled = false } : candidate);
        var storage = Compile(masked, deployed);
        Assert.DoesNotContain(masked.Modules, module => module.ModuleId is "Workspace" or "Part" or "Document");
        Assert.Equal(new[] { "Document", "Part", "Workspace" }, storage.RetainedModuleIds);
        Assert.Equal(deployed.Models.Select(model => (model.Name, model.OwnerModuleId, model.TableName)), storage.Models.Select(model => (model.Name, model.OwnerModuleId, model.TableName)));
        Assert.NotEmpty(Assert.Single(storage.Models, model => model.Name == "WorkspaceFolderContent").ForeignKeys);
        Assert.Equal(deployed.Streams.Single(stream => stream.ModuleId == "Workspace").ModelDigest, storage.Streams.Single(stream => stream.ModuleId == "Workspace").ModelDigest);
        Assert.DoesNotContain(masked.FeatureCatalog, point => point.ModuleId == "Workspace");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoveredRealWorkspace_ValidatesRetainedMigrationsWithoutPublishingItsCapabilities(bool maskParent)
    {
        var modules = ReadFormalCandidate();
        var deployed = Compile(modules);
        using (var store = new PersistenceDeploymentStore(_root, deployed.Provider, "SERVER|CATALOG"))
        {
            store.PrepareCandidate(modules, deployed);
            store.CompleteCandidate(deployed);
        }
        using var restarted = new PersistenceDeploymentStore(_root, deployed.Provider, "SERVER|CATALOG");
        var recovered = restarted.ReadRecoveryCandidate()!;
        var link = Assert.Single(recovered.Models, model => model.Name == "WorkspaceFolderContent");
        // 正式恢复只保存持久化声明，不依赖活动查询目录或重新发布已停用模块的能力。
        Assert.Null(link.LinkQuery);
        Assert.Empty(link.LinkedTypes);
        Assert.NotEmpty(link.ForeignKeys);
        var selected = ReadFormalCandidate(candidate =>
            (maskParent ? candidate.ModuleId == "InforCenter" : candidate.ModuleId is "Workspace" or "Document")
                ? candidate with { Enabled = false } : candidate);
        var retained = Compile(selected, recovered);
        Assert.DoesNotContain(selected.Modules, module => module.ModuleId is "Workspace" or "Document");
        Assert.Equal(!maskParent, selected.Modules.Any(module => module.ModuleId == "Part"));
        Assert.DoesNotContain(selected.FeatureCatalog, point => point.ModuleId == "Workspace");
        Assert.Equal(maskParent ? new[] { "Document", "Part", "Workspace" } : new[] { "Document", "Workspace" }, retained.RetainedModuleIds);
        Assert.Equal(deployed.Models.Select(model => (model.Name, model.OwnerModuleId, model.TableName)), retained.Models.Select(model => (model.Name, model.OwnerModuleId, model.TableName)));
        Assert.Equal(deployed.Streams.Single(stream => stream.ModuleId == "Workspace").Migrations,
            retained.Streams.Single(stream => stream.ModuleId == "Workspace").Migrations);
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .UseSqlServer("Server=invalid;Database=never-connect;Integrated Security=True").Options;
        using var activeDb = new EfPlatformDbContext(options, deployed);
        using var retainedDb = new EfPlatformDbContext(options, retained);
        var activeHistory = new ModuleMigrationExecutor(activeDb, deployed).ValidateBeforeConnection();
        var retainedHistory = new ModuleMigrationExecutor(retainedDb, retained).ValidateBeforeConnection();
        Assert.Equal(activeHistory.Select(entry => entry with { PlanDigest = "" }),
            retainedHistory.Select(entry => entry with { PlanDigest = "" }));
        var workspaceHistory = retainedHistory.Where(entry => entry.OwnerModuleId == "Workspace").ToArray();
        Assert.Equal(6, workspaceHistory.Length);
        Assert.Equal("20261016054922_Workspace_000006_ScopeNeutral", workspaceHistory[^1].MigrationId);
    }

    // 从真实 Host 发布物读取、签发并编译本次声明，不用手工 Canonical 或持久化 plan 代替 formal 链。
    private static CanonicalModulePlan ReadFormalCandidate(Func<ModuleCapsuleCandidate, ModuleCapsuleCandidate>? transform = null, string? hostRoot = null)
    {
        hostRoot ??= HostRoot();
        var source = TrustedBuiltInModuleSource.CreateForHostDistribution(hostRoot, Path.Combine(hostRoot, "modules"));
        var candidates = new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(source);
        var result = new CanonicalModulePlanCompiler().Compile("retained-deployment", transform is null ? candidates : candidates.Select(transform), source.PlatformCompositionModuleId);
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        return result.Plan!;
    }

    private static CompiledPersistencePlan Compile(CanonicalModulePlan modules, CompiledPersistencePlan? deployed = null)
    {
        var result = Compiler().Compile(modules, PersistenceProvider.SqlServer, deployed);
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        return result.Plan!;
    }

    private static PersistencePlanCompiler Compiler() => new(Path.Combine(HostRoot(), "server", "platform-modeling", "metadata", "storage-types.json"));

    private string CopyFormalHost()
    {
        var root = Path.Combine(_root, "host");
        Directory.CreateDirectory(root);
        File.Copy(Path.Combine(HostRoot(), "module-composition.json"), Path.Combine(root, "module-composition.json"));
        foreach (var file in Directory.GetFiles(HostRoot(), "*.dll")) File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        foreach (var file in Directory.GetFiles(Path.Combine(HostRoot(), "modules"), "*", SearchOption.AllDirectories))
        {
            var path = Path.Combine(root, Path.GetRelativePath(HostRoot(), file));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(file, path);
        }
        return root;
    }

    // 只改隔离的真实模块 authoring；原已发布 manifest 保持，错误身份须在任何 DDL 前拒绝。
    private static void RenameModel(string root, string owner, string previous, string current)
    {
        var path = Path.Combine(root, "modules", owner, "metadata", "models", previous + ".model.json");
        var model = JsonNode.Parse(File.ReadAllText(path))!;
        model[current] = model[previous]!.DeepClone();
        model.AsObject().Remove(previous);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, current + ".model.json"), model.ToJsonString());
        File.Delete(path);
        var capsulePath = Path.Combine(root, "modules", owner, "plugin.json");
        var capsule = JsonNode.Parse(File.ReadAllText(capsulePath))!;
        capsule["persistence"]!["models"] = new JsonArray("metadata/models/" + current + ".model.json");
        File.WriteAllText(capsulePath, capsule.ToJsonString());
    }

    private static string HostRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ICP-Platform.sln"))) root = root.Parent;
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        return Path.Combine(root!.FullName, "apps", "host-api", "bin", output.Parent!.Name, output.Name);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
