using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Compression;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.PluginPlatform.ComponentBundles;

/// <summary>在代码或迁移消费前完整核验资产；内容校验不授予可信原生来源。</summary>
public sealed class ComponentBundleInspector
{
    /// <summary>供库存 owner 在提交前比较同身份同版本资产；本方法不读取或修改库存。</summary>
    public static void RequireReusable(ComponentBundleMember installed, ComponentBundleMember incoming)
    {
        if (installed.ModuleId != incoming.ModuleId || installed.Version != incoming.Version)
            throw new ArgumentException("Reuse comparison requires the same component identity and version.");
        if (installed.ContentDigest != incoming.ContentDigest)
            throw new InvalidDataException($"COMPONENT_BUNDLE_VERSION_CONTENT_CONFLICT: '{incoming.ModuleId}@{incoming.Version}'.");
    }

    /// <summary>预检整个 ZIP 路径集合后解到新暂存根，完整核验才返回可读取输入。</summary>
    public ComponentBundleInspection ExtractVerified(string zipPath, string outputRoot, PlatformRuntimeRelease? supportedRuntime = null)
    {
        if (Directory.Exists(outputRoot) || File.Exists(outputRoot))
            throw new IOException($"COMPONENT_BUNDLE_OUTPUT_EXISTS: '{outputRoot}'.");
        using var archive = ZipFile.OpenRead(zipPath);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            ComponentBundleIntegrity.Resolve(outputRoot, entry.FullName);
            if (!paths.Add(entry.FullName) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException($"COMPONENT_BUNDLE_PATH_INVALID: duplicate or symbolic link '{entry.FullName}'.");
        }
        Directory.CreateDirectory(outputRoot);
        foreach (var entry in archive.Entries)
        {
            var target = ComponentBundleIntegrity.Resolve(outputRoot, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = entry.Open();
            using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(destination);
        }
        return Inspect(outputRoot, supportedRuntime);
    }

    /// <summary>核验受控发行兼容、所有资产摘要、声明映射和正式候选读取。</summary>
    public ComponentBundleInspection Inspect(string packageRoot, PlatformRuntimeRelease? supportedRuntime = null)
    {
        var manifest = JsonSerializer.Deserialize<ComponentBundleManifest>(File.ReadAllBytes(
            ComponentBundleIntegrity.Resolve(packageRoot, "component-bundle.json")), ComponentBundleIntegrity.Json)
            ?? throw new InvalidDataException("COMPONENT_BUNDLE_INVALID: empty manifest.");
        if (manifest.SchemaVersion != 1 || manifest.Components.Count == 0)
            throw new InvalidDataException("COMPONENT_BUNDLE_INVALID: unsupported or empty bundle.");
        if (manifest.PlatformRuntime != (supportedRuntime ?? PlatformRuntimeReleaseIdentity.Current)
            || ComponentBundleIntegrity.HashFile(ComponentBundleIntegrity.Resolve(packageRoot, "platform-runtime-release.json")) != manifest.PlatformRuntime.ContentDigest)
            throw new InvalidDataException("COMPONENT_BUNDLE_RUNTIME_MISMATCH: unsupported platform runtime release.");
        if (manifest.ContentDigest != ComponentBundleIntegrity.Hash(manifest with { ContentDigest = "" }))
            throw new InvalidDataException("COMPONENT_BUNDLE_DIGEST_MISMATCH: bundle index changed.");
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "component-bundle.json", "platform-runtime-release.json" };
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in manifest.Components)
        {
            if (!identities.Add(member.ModuleId) || member.Root != "modules/" + member.ModuleId || !roots.Add(member.Root))
                throw new InvalidDataException($"COMPONENT_BUNDLE_IDENTITY_MISMATCH: '{member.ModuleId}'.");
            var root = ComponentBundleIntegrity.Resolve(packageRoot, member.Root);
            var orderedFiles = new SortedDictionary<string, string>(member.Files.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal);
            if (member.ContentDigest != ComponentBundleIntegrity.Hash(orderedFiles))
                throw new InvalidDataException($"COMPONENT_BUNDLE_DIGEST_MISMATCH: '{member.ModuleId}'.");
            foreach (var file in member.Files)
            {
                var path = ComponentBundleIntegrity.Resolve(root, file.Key);
                if (!expected.Add(member.Root + "/" + file.Key))
                    throw new InvalidDataException($"COMPONENT_BUNDLE_PATH_INVALID: duplicate '{file.Key}'.");
                if (!File.Exists(path) || ComponentBundleIntegrity.HashFile(path) != file.Value)
                    throw new InvalidDataException($"COMPONENT_BUNDLE_ASSET_MISMATCH: '{member.ModuleId}/{file.Key}'.");
            }
        }
        var actual = ComponentBundleIntegrity.Files(packageRoot).ToArray();
        if (actual.Length != expected.Count || actual.Any(path => !expected.Contains(path)))
            throw new InvalidDataException("COMPONENT_BUNDLE_ASSET_MISMATCH: package contains unindexed assets.");
        var assemblyPaths = manifest.Components.SelectMany(member => member.Files.Keys
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                && (path.StartsWith("backend/", StringComparison.Ordinal) || path.StartsWith("migrations/", StringComparison.Ordinal)))
            .Select(path => ComponentBundleIntegrity.Resolve(packageRoot, member.Root + "/" + path))).ToArray();
        var provided = ComponentBundleAssemblyValidator.ReadIdentities(assemblyPaths);
        var framework = ComponentBundleAssemblyValidator.FrameworkAssemblies();
        foreach (var member in manifest.Components)
            ValidateDeclaration(member, ComponentBundleIntegrity.Resolve(packageRoot, member.Root), provided, framework);
        foreach (var assembly in assemblyPaths)
            ComponentBundleAssemblyValidator.Validate(assembly, provided, framework, requireRuntimeTarget: false);
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(packageRoot, "modules"));
        if (candidates.Count != manifest.Components.Count)
            throw new InvalidDataException("COMPONENT_BUNDLE_IDENTITY_MISMATCH: incomplete component candidates.");
        var canonical = new CanonicalModulePlanCompiler().Compile("bundle-inspect", candidates.Select(candidate => candidate with { Enabled = false }));
        if (!canonical.IsSuccess)
            throw new InvalidDataException(string.Join(Environment.NewLine, canonical.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));
        var rootCandidate = candidates.SingleOrDefault(candidate => candidate.ModuleId == manifest.RootModuleId)
            ?? throw new InvalidDataException("COMPONENT_BUNDLE_IDENTITY_MISMATCH: root component absent.");
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(rootCandidate.ModuleId);
        var byId = candidates.ToDictionary(candidate => candidate.ModuleId, StringComparer.Ordinal);
        while (pending.TryPop(out var id))
        {
            if (!reachable.Add(id)) continue;
            foreach (var child in byId[id].CompositionMembers) pending.Push(child);
        }
        if (reachable.Count != candidates.Count)
            throw new InvalidDataException("COMPONENT_BUNDLE_IDENTITY_MISMATCH: unrelated components in bundle.");
        return new(manifest, candidates);
    }

    // 两份清单必须严格对应唯一 authoring 与受控 transport；绝不追随 authoring 的历史越界路径。
    private static void ValidateDeclaration(ComponentBundleMember member, string root, IReadOnlyDictionary<string, Version> provided, IReadOnlyDictionary<string, Version> framework)
    {
        var authoring = JsonNode.Parse(File.ReadAllBytes(ComponentBundleIntegrity.Resolve(root, "authoring/newp.plugin.json")))!;
        var transport = JsonNode.Parse(File.ReadAllBytes(ComponentBundleIntegrity.Resolve(root, "plugin.json")))!;
        if (authoring["moduleId"]?.GetValue<string>() != member.ModuleId || authoring["version"]?.GetValue<string>() != member.Version
            || !JsonNode.DeepEquals(transport, ComponentBundleIntegrity.Transport(authoring)))
            throw new InvalidDataException($"COMPONENT_BUNDLE_DECLARATION_MISMATCH: '{member.ModuleId}'.");
        if (transport["entryAssembly"] is JsonValue backend)
            ComponentBundleAssemblyValidator.Validate(RequireFile(root, backend.GetValue<string>()), provided, framework, authoring);
        if (transport["frontend"] is JsonObject frontend && frontend["enabled"]?.GetValue<bool>() != false)
        {
            RequireFile(root, frontend["entry"]!.GetValue<string>());
            ComponentBundleFrontendValidator.Validate(root);
        }
        if (transport["persistence"] is not JsonObject persistence) return;
        foreach (var model in persistence["models"]?.AsArray() ?? []) RequireFile(root, model!.GetValue<string>());
        if (persistence["migrations"] is not JsonObject migrations) return;
        foreach (var provider in migrations)
        {
            var assemblyPath = RequireFile(root, provider.Value!["assembly"]!.GetValue<string>());
            ComponentBundleAssemblyValidator.Validate(assemblyPath, provided, framework);
            var manifestPath = RequireFile(root, provider.Value["manifest"]!.GetValue<string>());
            var migrationManifest = MigrationManifestIntegrity.Parse(File.ReadAllBytes(manifestPath));
            var kind = Enum.Parse<PersistenceProvider>(provider.Key, true);
            using var release = JsonDocument.Parse(PlatformRuntimeReleaseIdentity.Descriptor);
            var storageBytes = PlatformRuntimeReleaseIdentity.StorageTypeMap;
            if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(storageBytes)) != release.RootElement.GetProperty("backend").GetProperty("storageTypeMapDigest").GetString())
                throw new InvalidDataException("COMPONENT_BUNDLE_RUNTIME_MISMATCH: controlled platform storage contract changed without a release.");
            var typeMap = JsonSerializer.Deserialize<StorageTypeMapDocument>(storageBytes, ComponentBundleIntegrity.Json)!;
            var definitions = (persistence["models"]?.AsArray() ?? []).Select(model =>
            {
                var path = RequireFile(root, model!.GetValue<string>());
                var document = new JsonModelMetadataDocumentLoader().LoadFromFile(path);
                if (document.ModuleId != member.ModuleId)
                    throw new InvalidDataException($"COMPONENT_BUNDLE_DECLARATION_MISMATCH: model owner '{document.ModuleId}' differs from '{member.ModuleId}'.");
                // 此处验证原生发行的既定物理契约；不向候选授予可信 provenance 或执行权限。
                var definition = new ModelMetadataProjector().Project(document, typeMap, kind.ToString()) with { OwnerModuleId = member.ModuleId, SchemaName = "dbo" };
                new ModelDefinitionValidator().Validate(definition);
                return definition;
            }).ToArray();
            MigrationManifestIntegrity.Validate(member.ModuleId, kind, migrationManifest, File.ReadAllBytes(assemblyPath), manifestPath, PersistenceModelDigest.Compute(definitions));
            foreach (var step in migrationManifest.Migrations)
            {
                var source = RequireFile(root, $"migrations/sources/{provider.Key}/{step.MigrationId}.cs");
                if (MigrationManifestIntegrity.ComputeCanonicalContentDigest(File.ReadAllText(source)) != step.ContentDigest)
                    throw new InvalidDataException($"COMPONENT_BUNDLE_MIGRATION_MISMATCH: '{member.ModuleId}/{step.MigrationId}' source drift.");
            }
        }
    }

    // 资产引用必须存在于组件固定根内，不能借父集合读取其他组件或主机文件。
    private static string RequireFile(string root, string relative)
    {
        var path = ComponentBundleIntegrity.Resolve(root, relative);
        if (!File.Exists(path)) throw new InvalidDataException($"COMPONENT_BUNDLE_ASSET_MISSING: '{relative}'.");
        return path;
    }
}
