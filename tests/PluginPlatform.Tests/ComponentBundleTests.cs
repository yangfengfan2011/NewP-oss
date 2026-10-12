using System.Text.Json.Nodes;
using System.Text.Json;
using System.Security.Cryptography;
using System.IO.Compression;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.PluginPlatform.ComponentBundles;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Plugin.Contracts;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.PluginPlatform.StandardData;
using Xunit;
using ICP.Platform.Testing;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ICP.Platform.Kernel.ModuleLoading;

namespace ICP.Platform.PluginPlatform.Tests;

public sealed class ComponentBundleTests
{
    [Fact]
    public void RealWorkspace_SharedReferencesMatchControlledReleaseAndExistingHostBoundary()
    {
        using var release = JsonDocument.Parse(PlatformRuntimeReleaseIdentity.Descriptor);
        var shared = release.RootElement.GetProperty("backend").GetProperty("assemblies");
        var expected = new HashSet<string>(StringComparer.Ordinal)
            { "Microsoft.Data.SqlClient", "PlatformAuthorization", "PlatformQuery", "Authorization.Contracts", "Microsoft.EntityFrameworkCore.Abstractions" };
        // PlatformQuery 是 Workspace 使用的 PlatformCrud 的真实传递共享引用。
        foreach (var assembly in new[] { "WorkspaceModule.dll", "PlatformCrud.dll", "Workspace.SqlServerMigrations.dll" })
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, assembly));
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            foreach (var handle in metadata.AssemblyReferences)
            {
                var reference = metadata.GetAssemblyReference(handle);
                var name = metadata.GetString(reference.Name);
                if (!expected.Remove(name)) continue;
                var identity = new AssemblyName(name) { Version = reference.Version };
                identity.SetPublicKeyToken(metadata.GetBlobBytes(reference.PublicKeyOrToken));
                Assert.True(VerifiedAssemblyIdentityPolicy.IsHostPlatformSharedDependency(identity), name);
                Assert.True(shared.TryGetProperty(name, out var declared), name);
                Assert.Equal(reference.Version.ToString(), declared.GetString());
            }
        }
        Assert.Empty(expected);
    }

    [Fact]
    public void OrdinaryInstall_AndDescriptiveSourceFieldsCannotGrantNativeTrust()
    {
        using var input = new ComponentBundleTestInput();
        var capsule = JsonNode.Parse(File.ReadAllText(input.Capsule))!;
        capsule["sourceKind"] = "built-in";
        capsule["profile"] = "native-component.v1";
        File.WriteAllText(input.Capsule, capsule.ToJsonString());
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var candidate = Assert.Single(new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(input.Output, "modules")));
        var plan = new CanonicalModulePlanCompiler().Compile("untrusted-source-fields", [candidate]).Plan!;
        Assert.False(Assert.Single(plan.Modules).Provenance.AuthenticatesBuiltIn(Assert.Single(plan.Modules)));
        Assert.Empty(plan.RequiredModuleIds);
        var installRoot = input.Output + "-ordinary";
        var store = new InstalledPluginStateStore(installRoot);
        var result = new UnifiedPluginPackageInstaller(store).Install(Path.Combine(input.Output, "modules", "InforCenter"), installRoot, pruneOldVersions: false);
        Assert.Equal("Failed", result.Status);
        Assert.Null(NativeComponentInventory.ReadReceipt(installRoot, "InforCenter", "1.0.0"));
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void NativeInstall_RejectsRuntimeMismatchAndDistributionRoleBeforeInventoryCommit()
    {
        using var input = new ComponentBundleTestInput();
        var manifest = new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var installRoot = input.Output + "-installed";
        var store = new InstalledPluginStateStore(installRoot);
        var installer = new NativeComponentPackageInstaller(installRoot, store);
        var role = Assert.Throws<InvalidDataException>(() => installer.Install(input.Output, "InforCenter"));
        Assert.Contains("COMPONENT_BUNDLE_PLATFORM_ROLE_FORBIDDEN", role.Message);
        Assert.Empty(store.LoadAll());
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        File.WriteAllBytes(Path.Combine(input.Output, "component-bundle.json"), JsonSerializer.SerializeToUtf8Bytes(manifest with { PlatformRuntime = manifest.PlatformRuntime with { Version = "9.0.0" } }, options));
        var mismatch = Assert.Throws<InvalidDataException>(() => installer.Install(input.Output));
        Assert.Contains("COMPONENT_BUNDLE_RUNTIME_MISMATCH", mismatch.Message);
        Assert.Empty(store.LoadAll());
        Assert.False(File.Exists(Path.Combine(installRoot, "native-install.pending")));
    }

    [Fact]
    public void NativeInstall_SameVersionDifferentContentPreservesInventoryAndSelection()
    {
        using var input = new ComponentBundleTestInput();
        var builder = new ComponentBundleBuilder();
        builder.Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var installRoot = input.Output + "-installed";
        var store = new InstalledPluginStateStore(installRoot);
        var installer = new NativeComponentPackageInstaller(installRoot, store);
        installer.Install(input.Output);
        var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(installRoot, new TestActivationDeployment(false)));
        Assert.True(manager.RequestEnable(InstalledPluginInventoryAdapter.Create(store), "InforCenter", "1.0.0").IsSuccess);
        var inventoryBytes = File.ReadAllBytes(Path.Combine(installRoot, "state", "InforCenter.json"));
        var desiredBytes = File.ReadAllBytes(Path.Combine(installRoot, "lifecycle", "v2", "state.json"));
        var capsule = JsonNode.Parse(File.ReadAllText(input.Capsule))!;
        capsule["description"] = "different released content";
        File.WriteAllText(input.Capsule, capsule.ToJsonString());
        builder.Stage(input.Repository, "InforCenter", input.Output + "-changed", input.Artifacts);
        Assert.Throws<InvalidDataException>(() => installer.Install(input.Output + "-changed"));
        Assert.Equal(inventoryBytes, File.ReadAllBytes(Path.Combine(installRoot, "state", "InforCenter.json")));
        Assert.Equal(desiredBytes, File.ReadAllBytes(Path.Combine(installRoot, "lifecycle", "v2", "state.json")));
        installer.Install(input.Output);
        Assert.Equal(desiredBytes, File.ReadAllBytes(Path.Combine(installRoot, "lifecycle", "v2", "state.json")));
    }

    [Fact]
    public void NativeInstall_OrdinaryWriteFailureRollsBackAllMemberInventory()
    {
        using var input = new ComponentBundleTestInput();
        input.IncludePart();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var installRoot = input.Output + "-installed";
        var saves = 0;
        var store = new InstalledPluginStateStore(installRoot, (source, target, _) => File.Move(source, target, true),
            (source, target) => { if (++saves == 2) throw new IOException("controlled inventory write failure"); File.Move(source, target); });
        Assert.Throws<IOException>(() => new NativeComponentPackageInstaller(installRoot, store).Install(input.Output));
        Assert.Empty(store.LoadAll());
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "InforCenter", "1.0.0")));
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages", "Part", "1.0.0")));
        Assert.False(File.Exists(Path.Combine(installRoot, "native-install.pending")));
    }

    [Fact]
    public void NativeInstall_RetryRecoversInterruptedRollbackWithoutChangingOtherState()
    {
        using var input = new ComponentBundleTestInput();
        input.IncludePart();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var root = input.Output + "-installed";
        var store = new InstalledPluginStateStore(root);
        Assert.True(store.TrySave(new InstalledPluginRecord
        {
            ModuleId = "Unrelated", Version = "2.0.0", Status = InstalledPluginLifecycleState.Installed,
            PackageRoot = Path.Combine(root, "packages", "Unrelated", "2.0.0"), InstalledVersions = ["2.0.0"],
            CurrentEnabledVersion = "2.0.0", IsDisabled = false
        }, out _));
        var protectedFiles = new Dictionary<string, byte[]>
        {
            ["state/Unrelated.json"] = File.ReadAllBytes(Path.Combine(root, "state", "Unrelated.json")),
            ["lifecycle/v2/state.json"] = "original desired and active"u8.ToArray(),
            ["persistence/catalog/state.json"] = "original pending and successful"u8.ToArray(),
            ["persistence/catalog/assets/recovery.bin"] = "retained recovery bytes"u8.ToArray()
        };
        foreach (var item in protectedFiles)
        {
            var path = Path.Combine(root, item.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, item.Value);
        }
        FileStream? lockedReceipt = null;
        var saves = 0;
        var failingStore = new InstalledPluginStateStore(root, (source, target, _) => File.Move(source, target, true),
            (source, target) =>
            {
                if (++saves == 2) throw new IOException("controlled inventory failure");
                File.Move(source, target);
                lockedReceipt = new FileStream(NativeComponentInventory.ReceiptPath(root, "InforCenter", "1.0.0"),
                    FileMode.Open, FileAccess.Read, FileShare.None);
            });
        try
        {
            Assert.ThrowsAny<IOException>(() => new NativeComponentPackageInstaller(root, failingStore).Install(input.Output));
            Assert.True(File.Exists(Path.Combine(root, "native-install.pending")));
            Assert.Throws<InvalidDataException>(() => NativeComponentInventory.EnsureComplete(root));
            Assert.Throws<InvalidDataException>(() => InstalledPluginInventoryAdapter.Create(store));
        }
        finally { lockedReceipt?.Dispose(); }
        new NativeComponentPackageInstaller(root, store).Install(input.Output);
        new NativeComponentPackageInstaller(root, store).Install(input.Output);
        Assert.Equal(["InforCenter", "Part", "Unrelated"], store.LoadAll().Select(item => item.ModuleId).Order().ToArray());
        Assert.False(File.Exists(Path.Combine(root, "native-install.pending")));
        foreach (var item in protectedFiles) Assert.Equal(item.Value, File.ReadAllBytes(Path.Combine(root, item.Key)));
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("directory")]
    public void NativeInstall_FileBoundaryFailurePreservesOriginalInventory(string boundary)
    {
        using var input = new ComponentBundleTestInput();
        var capsule = JsonNode.Parse(File.ReadAllText(input.Capsule))!;
        capsule["version"] = "0.9.0";
        File.WriteAllText(input.Capsule, capsule.ToJsonString());
        var builder = new ComponentBundleBuilder();
        builder.Stage(input.Repository, "InforCenter", input.Output + "-old", input.Artifacts);
        var root = input.Output + "-installed";
        var store = new InstalledPluginStateStore(root);
        new NativeComponentPackageInstaller(root, store).Install(input.Output + "-old");
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        capsule["version"] = "1.0.0";
        File.WriteAllText(input.Capsule, capsule.ToJsonString());
        input.IncludePart();
        builder.Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var copies = 0;
        var moves = 0;
        var installer = new NativeComponentPackageInstaller(root, store,
            copyFile: (source, target) =>
            {
                if (boundary == "staging" && ++copies == 2) throw new IOException("controlled staging failure");
                File.Copy(source, target);
            }, moveDirectory: (source, target) =>
            {
                if (boundary == "directory" && ++moves == 2) throw new IOException("controlled member directory failure");
                Directory.Move(source, target);
            });
        Assert.Throws<IOException>(() => installer.Install(input.Output));
        Assert.Equal("0.9.0", Assert.Single(store.LoadAll()).Version);
        Assert.False(File.Exists(Path.Combine(root, "native-install.pending")));
        Assert.Empty(Directory.GetDirectories(root, ".native-stage-*"));
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order());
        foreach (var item in before) Assert.Equal(item.Value, File.ReadAllBytes(item.Key));
    }

    [Fact]
    public void NativeInstall_ReusedDirectoriesAndReceiptsSurviveFailedInventoryCommit()
    {
        using var input = new ComponentBundleTestInput();
        input.IncludePart();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var root = input.Output + "-installed";
        new NativeComponentPackageInstaller(root, new InstalledPluginStateStore(root)).Install(input.Output);
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var saves = 0;
        var store = new InstalledPluginStateStore(root, (source, target, _) =>
        {
            if (++saves == 2) throw new IOException("controlled inventory replacement failure");
            File.Move(source, target, true);
        }, File.Move);
        Assert.Throws<IOException>(() => new NativeComponentPackageInstaller(root, store).Install(input.Output));
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order());
        foreach (var item in before) Assert.Equal(item.Value, File.ReadAllBytes(item.Key));
        Assert.Equal(2, store.LoadAll().Count);
    }

    [Fact]
    public void NativeInstall_InvalidRecoveryFactsFailClosedBeforeAnyRestoration()
    {
        using var input = new ComponentBundleTestInput();
        var root = input.Output + "-installed";
        Directory.CreateDirectory(root);
        var marker = Path.Combine(root, "native-install.pending");
        var unchanged = Path.Combine(root, "untouched.bin");
        File.WriteAllText(unchanged, "unrelated inventory and retained assets");
        var before = File.ReadAllBytes(unchanged);
        foreach (var recovery in new[]
        {
            new NativeInstallRecovery(".native-stage-" + Guid.NewGuid().ToString("N"), [new("../untouched", "1.0.0", null, false, false)]),
            new NativeInstallRecovery(null!, [new("InforCenter", "1.0.0", null, false, false)])
        })
        {
            File.WriteAllBytes(marker, ComponentBundleIntegrity.Serialize(new NativeInstallJournal(recovery, ComponentBundleIntegrity.Hash(recovery))));
            var journal = File.ReadAllBytes(marker);
            var failure = Assert.Throws<InvalidDataException>(() => new NativeComponentPackageInstaller(root, new InstalledPluginStateStore(root)).Install(input.Output));
            Assert.Contains("COMPONENT_BUNDLE_INSTALL_INCOMPLETE", failure.Message);
            Assert.Equal(journal, File.ReadAllBytes(marker));
            Assert.Equal(before, File.ReadAllBytes(unchanged));
        }
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AcceptedNativeCandidate_PreservesTrustedStorageAndUrlAssetsAcrossExternalSwitch(bool externalEnabled)
    {
        using var input = new ComponentBundleTestInput();
        input.IncludePart();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var installRoot = input.Output + "-installed";
        var store = new InstalledPluginStateStore(installRoot);
        new NativeComponentPackageInstaller(installRoot, store).Install(input.Output);
        var platformRoot = Path.Combine(input.Root, "platform");
        Directory.CreateDirectory(Path.Combine(platformRoot, "modules"));
        // 此低层检查只隔离来源与资产交付，真实平台依赖的端到端装配由固定发行物首片证明。
        var partDeclaration = JsonNode.Parse(File.ReadAllText(Path.Combine(input.Output, "modules", "Part", "plugin.json")))!;
        foreach (var dependency in partDeclaration["dependencies"]!.AsArray())
        {
            var id = dependency!.GetValue<string>();
            var root = Path.Combine(platformRoot, "modules", id);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "plugin.json"), JsonSerializer.Serialize(new
            {
                moduleId = id, version = "1.0.0", displayName = id, frontend = new { enabled = false }
            }));
        }
        var source = TrustedBuiltInModuleSource.CreateForHost(platformRoot, Path.Combine(platformRoot, "modules"));
        var inspector = new StandardDataPackageInspector(new PlatformApiHostCatalog("1.0.0", [], []));
        var inventory = InstalledPluginInventoryAdapter.Create(store);
        var state = new ResolvedPluginLifecycleState([new("InforCenter", "1.0.0", true), new("Part", "1.0.0", true)], null, null, false);
        new StandardDataPackagePreflight(inspector, "1.1.0", source, installRoot, externalEnabled).Validate(inventory, state);
        var plan = new StandardDataCandidatePlanCompiler(source, inspector, installRoot: installRoot, externalPluginsEnabled: externalEnabled).Compile(inventory, state);
        var part = Assert.Single(plan.Modules, module => module.ModuleId == "Part");
        Assert.True(part.Provenance.AuthenticatesBuiltIn(part));
        Assert.Equal(ModuleFrontendDelivery.Url, part.FrontendDelivery);
        Assert.Empty(plan.RequiredModuleIds);
        var snapshot = part.Provenance.GetAuthenticatedBuiltInSnapshot(part)!;
        var expectedBackend = snapshot.ReadDistributionFile(part.BackendEntry!);
        File.WriteAllText(Path.Combine(part.PackageRoot, "backend", "PartModule.dll"), "changed after candidate preparation");
        var materializer = new RuntimeViewMaterializer(store, new FrontendRuntimeManifestBuilder("/plugins/runtime/assets"), source);
        using var stage = materializer.Stage(installRoot, plan);
        using var commit = stage.BeginCommit();
        commit.Complete();
        Assert.Equal(expectedBackend, File.ReadAllBytes(Path.Combine(installRoot, "runtime", "backend-load-view", "Part", "PartModule.dll")));
        Assert.True(File.Exists(Path.Combine(installRoot, "public", "Part", "1.0.0", "frontend", "entry.js")));
        Assert.True(File.Exists(Path.Combine(installRoot, "public", "Part", "1.0.0", "frontend", "resource.mjs")));
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.Combine(installRoot, "public"), "*", SearchOption.AllDirectories), path => path.Contains("backend") || path.Contains("metadata") || path.Contains("migrations"));
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(installRoot, "runtime", "active-module-runtime.json")));
        var frontend = Assert.Single(catalog.RootElement.GetProperty("modules").EnumerateArray(), module => module.GetProperty("moduleId").GetString() == "Part").GetProperty("frontend");
        Assert.Equal(JsonValueKind.Null, frontend.GetProperty("moduleId").ValueKind);
        Assert.Equal("/plugins/runtime/assets/Part/1.0.0/frontend/entry.js", frontend.GetProperty("url").GetString());
        Assert.Throws<InvalidDataException>(() => new StandardDataCandidatePlanCompiler(source, inspector, installRoot: installRoot).Compile(inventory, state));
    }

    [Fact]
    public void NativeInstall_AddsImmutableInventoryWithoutSelectingOrActivating()
    {
        using var input = new ComponentBundleTestInput();
        var manifest = new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, new Dictionary<string, ComponentBuiltArtifacts>());
        var installRoot = input.Output + "-installed";
        var store = new ICP.Platform.Kernel.PluginPackages.InstalledPluginStateStore(installRoot);
        var installer = new NativeComponentPackageInstaller(installRoot, store);
        installer.Install(input.Output);
        installer.Install(input.Output);
        Assert.Equal(["1.0.0"], Assert.Single(store.LoadAll()).InstalledVersions);
        Assert.False(File.Exists(Path.Combine(installRoot, "lifecycle", "v2", "state.json")));
        Assert.Equal(manifest.Components[0].ContentDigest,
            NativeComponentInventory.ReadReceipt(installRoot, "InforCenter", "1.0.0")!.Component.ContentDigest);
    }

    [Fact]
    public void NativeInstall_RejectsInvalidBundleBeforeInventoryCommit()
    {
        using var input = new ComponentBundleTestInput();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, new Dictionary<string, ComponentBuiltArtifacts>());
        File.AppendAllText(Path.Combine(input.Output, "modules", "InforCenter", "plugin.json"), "changed");
        var installRoot = input.Output + "-installed";
        var store = new ICP.Platform.Kernel.PluginPackages.InstalledPluginStateStore(installRoot);
        Assert.Throws<InvalidDataException>(() => new NativeComponentPackageInstaller(installRoot, store).Install(input.Output));
        Assert.Empty(store.LoadAll());
        Assert.False(Directory.Exists(Path.Combine(installRoot, "packages")));
    }

    [Fact]
    public void PureComposition_PacksAndReadsWithoutInventedRuntimeEntries()
    {
        using var input = new ComponentBundleTestInput();
        var manifest = new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, new Dictionary<string, ComponentBuiltArtifacts>());
        var inspected = new ComponentBundleInspector().Inspect(input.Output);
        var component = Assert.Single(inspected.Components);
        Assert.Equal("InforCenter", component.ModuleId);
        Assert.Equal("1.0.0", component.Version);
        Assert.Equal(manifest.ContentDigest, inspected.Manifest.ContentDigest);
        Assert.Null(component.BackendEntry);
        Assert.Null(component.FrontendEntry);
        Assert.Null(component.Persistence);
        Assert.Equal(["authoring/newp.plugin.json", "plugin.json"], inspected.Manifest.Components[0].Files.Keys);
    }

    [Fact]
    public void SameIdentityAndVersion_ReuseRequiresExactlyTheSameContent()
    {
        using var input = new ComponentBundleTestInput();
        var builder = new ComponentBundleBuilder();
        var original = builder.Stage(input.Repository, "InforCenter", input.Output, new Dictionary<string, ComponentBuiltArtifacts>());
        ComponentBundleInspector.RequireReusable(original.Components[0], original.Components[0]);
        var capsule = JsonNode.Parse(File.ReadAllText(input.Capsule))!;
        capsule["description"] = "changed own content";
        File.WriteAllText(input.Capsule, capsule.ToJsonString());
        var changed = builder.Stage(input.Repository, "InforCenter", input.Output + "-changed", new Dictionary<string, ComponentBuiltArtifacts>());
        var error = Assert.Throws<InvalidDataException>(() => ComponentBundleInspector.RequireReusable(original.Components[0], changed.Components[0]));
        Assert.Contains("COMPONENT_BUNDLE_VERSION_CONTENT_CONFLICT", error.Message);
        Assert.Equal(original.ContentDigest, new ComponentBundleInspector().Inspect(input.Output).Manifest.ContentDigest);
    }

    [Fact]
    public void Inspector_ReindexedInvalidBackendFailsBeforeAnyAssemblyExecution()
    {
        using var input = new ComponentBundleTestInput();
        input.IncludePart();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        File.WriteAllText(Path.Combine(input.Output, "modules", "Part", "backend", "PartModule.dll"), "invalid managed assembly");
        Reindex(input.Output);
        var error = Assert.Throws<InvalidDataException>(() => new ComponentBundleInspector().Inspect(input.Output));
        Assert.Contains("COMPONENT_BUNDLE_ASSEMBLY_INVALID", error.Message);
    }

    [Fact]
    public void Inspector_ReindexedMissingFrontendChunkIsRejected()
    {
        using var input = new ComponentBundleTestInput();
        input.IncludePart();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        File.Delete(Path.Combine(input.Output, "modules", "Part", "frontend", "resource.mjs"));
        Reindex(input.Output);
        var error = Assert.Throws<InvalidDataException>(() => new ComponentBundleInspector().Inspect(input.Output));
        Assert.Contains("COMPONENT_BUNDLE_ASSET_MISSING", error.Message);
    }

    [Fact]
    public void Inspector_ReindexedModelChangeCannotRewritePublishedMigrationTarget()
    {
        using var input = new ComponentBundleTestInput();
        input.IncludePart();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var modelPath = Path.Combine(input.Output, "modules", "Part", "metadata", "models", "Part.model.json");
        var model = JsonNode.Parse(File.ReadAllText(modelPath))!;
        model["Part"]!["fields"]![2]!["storage"]!["nullable"] = false;
        File.WriteAllText(modelPath, model.ToJsonString());
        Reindex(input.Output);
        var error = Assert.Throws<MigrationManifestValidationException>(() => new ComponentBundleInspector().Inspect(input.Output));
        Assert.Equal("PERSISTENCE_MODEL_MISMATCH", error.Code);
    }

    [Fact]
    public void Inspector_ReindexedAssemblyForDifferentFrameworkIsRejected()
    {
        using var input = new ComponentBundleTestInput();
        input.IncludePart();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var assembly = Path.Combine(input.Output, "modules", "Part", "backend", "PartModule.dll");
        var bytes = File.ReadAllBytes(assembly);
        var expectedFramework = System.Text.Encoding.UTF8.GetBytes(".NETCoreApp,Version=v10.0");
        var offset = bytes.AsSpan().IndexOf(expectedFramework);
        Assert.True(offset >= 0);
        bytes[offset + expectedFramework.Length - 3] = (byte)'1';
        File.WriteAllBytes(assembly, bytes);
        Reindex(input.Output);
        var error = Assert.Throws<InvalidDataException>(() => new ComponentBundleInspector().Inspect(input.Output));
        Assert.Contains("COMPONENT_BUNDLE_RUNTIME_MISMATCH", error.Message);
    }

    [Fact]
    public void Inspector_ReindexedUnknownAssemblyReferenceIsRejected()
    {
        using var input = new ComponentBundleTestInput();
        input.IncludePart();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var assembly = Path.Combine(input.Output, "modules", "Part", "backend", "PartModule.dll");
        var bytes = File.ReadAllBytes(assembly);
        var offset = bytes.AsSpan().IndexOf(new byte[] { (byte)'S', (byte)'d', (byte)'k', 0 });
        Assert.True(offset >= 0);
        bytes[offset] = (byte)'F';
        bytes[offset + 1] = (byte)'o';
        bytes[offset + 2] = (byte)'o';
        File.WriteAllBytes(assembly, bytes);
        Reindex(input.Output);
        var error = Assert.Throws<InvalidDataException>(() => new ComponentBundleInspector().Inspect(input.Output));
        Assert.Contains("COMPONENT_BUNDLE_DEPENDENCY_MISSING", error.Message);
    }

    [Theory]
    [InlineData("missing", "COMPONENT_BUNDLE_ASSET_MISMATCH")]
    [InlineData("corrupt", "COMPONENT_BUNDLE_ASSET_MISMATCH")]
    [InlineData("extra", "COMPONENT_BUNDLE_ASSET_MISMATCH")]
    [InlineData("transport", "COMPONENT_BUNDLE_DECLARATION_MISMATCH")]
    [InlineData("migration-source", "COMPONENT_BUNDLE_MIGRATION_MISMATCH")]
    [InlineData("runtime", "COMPONENT_BUNDLE_RUNTIME_MISMATCH")]
    [InlineData("frontend-runtime", "COMPONENT_BUNDLE_RUNTIME_MISMATCH")]
    [InlineData("frontend-shared-import", "COMPONENT_BUNDLE_RUNTIME_MISMATCH")]
    public void Inspector_RejectsBadAssetsAndInconsistentDeclarations(string mutation, string code)
    {
        using var input = new ComponentBundleTestInput();
        input.IncludePart();
        new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var memberRoot = Path.Combine(input.Output, "modules", "Part");
        switch (mutation)
        {
            case "missing": File.Delete(Path.Combine(memberRoot, "metadata", "models", "Part.model.json")); break;
            case "corrupt": File.AppendAllText(Path.Combine(memberRoot, "frontend", "entry.js"), "corrupted"); break;
            case "extra": File.WriteAllText(Path.Combine(memberRoot, "unindexed.txt"), "extra"); break;
            case "transport":
                var transportPath = Path.Combine(memberRoot, "plugin.json");
                var transport = JsonNode.Parse(File.ReadAllBytes(transportPath))!;
                transport["version"] = "9.0.0";
                File.WriteAllText(transportPath, transport.ToJsonString());
                Reindex(input.Output);
                break;
            case "migration-source":
                var source = Directory.EnumerateFiles(Path.Combine(memberRoot, "migrations", "sources"), "*.cs", SearchOption.AllDirectories).Single();
                File.AppendAllText(source, "// changed published source");
                Reindex(input.Output);
                break;
            case "runtime": File.AppendAllText(Path.Combine(input.Output, "platform-runtime-release.json"), " "); break;
            case "frontend-runtime":
            case "frontend-shared-import":
                var frontendPath = Path.Combine(memberRoot, "frontend", "frontend-artifacts.json");
                var frontend = JsonNode.Parse(File.ReadAllBytes(frontendPath))!;
                if (mutation == "frontend-runtime") frontend["platformRuntime"]!["version"] = "9.0.0";
                else frontend["files"]!["entry.js"]!["sharedImports"] = new JsonArray("unknown-shared-runtime");
                File.WriteAllText(frontendPath, frontend.ToJsonString());
                Reindex(input.Output);
                break;
        }
        var error = Assert.Throws<InvalidDataException>(() => new ComponentBundleInspector().Inspect(input.Output));
        Assert.Contains(code, error.Message);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("C:/outside.txt")]
    [InlineData("metadata/models/../outside.txt")]
    [InlineData("asset:stream")]
    public void Inspector_ReindexedIllegalAssetPathIsRejected(string relative)
    {
        using var input = new ComponentBundleTestInput();
        var manifest = new ComponentBundleBuilder().Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var files = new SortedDictionary<string, string>(manifest.Components[0].Files.ToDictionary(item => item.Key, item => item.Value), StringComparer.Ordinal)
            { [relative] = new string('0', 64) };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var member = manifest.Components[0] with { Files = files, ContentDigest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(files, options))) };
        manifest = manifest with { Components = [member], ContentDigest = "" };
        manifest = manifest with { ContentDigest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(manifest, options))) };
        File.WriteAllBytes(Path.Combine(input.Output, "component-bundle.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, options));
        var error = Assert.Throws<InvalidDataException>(() => new ComponentBundleInspector().Inspect(input.Output));
        Assert.Contains("COMPONENT_BUNDLE_PATH_INVALID", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Archive_DuplicatePathsAndLinksAreRejectedBeforeExtraction(bool link)
    {
        using var input = new ComponentBundleTestInput();
        var zipPath = Path.Combine(input.Root, "unsafe.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("modules/InforCenter/plugin.json");
            if (link) entry.ExternalAttributes = 0xA000 << 16;
            else archive.CreateEntry("modules/inforcenter/plugin.json");
        }
        var destination = Path.Combine(input.Root, "extracted");
        var error = Assert.Throws<InvalidDataException>(() => new ComponentBundleInspector().ExtractVerified(zipPath, destination));
        Assert.Contains("COMPONENT_BUNDLE_PATH_INVALID", error.Message);
        Assert.False(Directory.Exists(destination));
    }

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("C:/escaped.txt")]
    [InlineData("modules/InforCenter/asset:stream")]
    [InlineData("modules/InforCenter/../escaped.txt")]
    [InlineData("modules\\InforCenter\\escaped.txt")]
    public void Archive_IllegalPathIsRejectedBeforeExtraction(string path)
    {
        using var input = new ComponentBundleTestInput();
        var zipPath = Path.Combine(input.Root, "unsafe.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create)) archive.CreateEntry(path);
        var destination = Path.Combine(input.Root, "extracted");
        var error = Assert.Throws<InvalidDataException>(() => new ComponentBundleInspector().ExtractVerified(zipPath, destination));
        Assert.Contains("COMPONENT_BUNDLE_PATH_INVALID", error.Message);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void PackTwice_IsByteIdenticalAndArchiveReadsAsTheSameComponents()
    {
        using var input = new ComponentBundleTestInput();
        var builder = new ComponentBundleBuilder();
        var manifest = builder.Stage(input.Repository, "InforCenter", input.Output, input.Artifacts);
        var first = Path.Combine(input.Root, "first.zip");
        var second = Path.Combine(input.Root, "second.zip");
        builder.Pack(input.Output, first);
        builder.Pack(input.Output, second);
        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        var inspected = new ComponentBundleInspector().ExtractVerified(first, Path.Combine(input.Root, "extracted"));
        Assert.Equal(manifest.ContentDigest, inspected.Manifest.ContentDigest);
        Assert.Equal("InforCenter", Assert.Single(inspected.Components).ModuleId);
    }

    // 代表攻击者重算 checksum；结构与真实声明校验仍必须独立拒绝不一致资产。
    private static void Reindex(string root)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var manifestPath = Path.Combine(root, "component-bundle.json");
        var manifest = JsonSerializer.Deserialize<ComponentBundleManifest>(File.ReadAllBytes(manifestPath), options)!;
        var members = manifest.Components.Select(member =>
        {
            var memberRoot = Path.Combine(root, member.Root);
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(memberRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                files.Add(Path.GetRelativePath(memberRoot, file).Replace('\\', '/'), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
            return member with { Files = files, ContentDigest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(files, options))) };
        }).ToArray();
        manifest = manifest with { Components = members, ContentDigest = "" };
        manifest = manifest with { ContentDigest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(manifest, options))) };
        File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest, options));
    }

}
