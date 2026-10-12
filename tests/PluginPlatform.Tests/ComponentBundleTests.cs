using System.Text.Json.Nodes;
using System.Text.Json;
using System.Security.Cryptography;
using System.IO.Compression;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.PluginPlatform.ComponentBundles;
using ICP.Platform.Persistence.Planning;
using Xunit;

namespace ICP.Platform.PluginPlatform.Tests;

public sealed class ComponentBundleTests
{
    [Fact]
    public void PureComposition_PacksAndReadsWithoutInventedRuntimeEntries()
    {
        using var input = new BundleInput();
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
        using var input = new BundleInput();
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
        using var input = new BundleInput();
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
        using var input = new BundleInput();
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
        using var input = new BundleInput();
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
        using var input = new BundleInput();
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
        using var input = new BundleInput();
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
        using var input = new BundleInput();
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
        using var input = new BundleInput();
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
        using var input = new BundleInput();
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
        using var input = new BundleInput();
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
        using var input = new BundleInput();
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

    private sealed class BundleInput : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "NewP-270-bundle-" + Guid.NewGuid().ToString("N"));
        internal string Repository => Path.Combine(Root, "repository");
        internal string Output => Path.Combine(Root, "bundle");
        internal string Capsule => Path.Combine(Repository, "frontend", "modules", "infor-center", "newp.plugin.json");
        internal string SourceRepository { get; }
        internal Dictionary<string, ComponentBuiltArtifacts> Artifacts { get; } = new(StringComparer.Ordinal);

        internal BundleInput()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ICP-Platform.sln"))) directory = directory.Parent;
            SourceRepository = directory?.FullName ?? throw new DirectoryNotFoundException();
            var source = ComponentSourceCatalog.Discover(SourceRepository).Get("InforCenter");
            Directory.CreateDirectory(Path.GetDirectoryName(Capsule)!);
            var capsule = JsonNode.Parse(File.ReadAllText(source.CapsulePath))!;
            capsule["composition"]!["members"] = new JsonArray();
            File.WriteAllText(Capsule, capsule.ToJsonString());
        }

        internal void IncludePart()
        {
            var catalog = ComponentSourceCatalog.Discover(SourceRepository);
            var part = catalog.Get("Part");
            var partRoot = Path.Combine(Path.GetDirectoryName(Capsule)!, "part");
            foreach (var file in catalog.EnumerateOwnFiles("Part"))
            {
                var relative = Path.GetRelativePath(part.Root, file);
                if (relative != "newp.plugin.json" && !relative.StartsWith("metadata") && !relative.StartsWith("migrations")) continue;
                var target = Path.Combine(partRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var parent = JsonNode.Parse(File.ReadAllText(Capsule))!;
            parent["composition"]!["members"] = new JsonArray("Part");
            File.WriteAllText(Capsule, parent.ToJsonString());
            var frontend = Path.Combine(Root, "frontend-built");
            Directory.CreateDirectory(frontend);
            File.WriteAllText(Path.Combine(frontend, "entry.js"), "export { default } from './resource.mjs';");
            File.WriteAllText(Path.Combine(frontend, "resource.mjs"), "export default { behaviorKey: 'part.frontend' };");
            File.WriteAllText(Path.Combine(frontend, "frontend-artifacts.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1, entry = "entry.js", platformRuntime = PlatformRuntimeReleaseIdentity.Current,
                files = new Dictionary<string, object>
                {
                    ["entry.js"] = new { imports = new[] { "resource.mjs" }, sharedImports = Array.Empty<string>(), assets = Array.Empty<string>() },
                    ["resource.mjs"] = new { imports = Array.Empty<string>(), sharedImports = Array.Empty<string>(), assets = Array.Empty<string>() }
                }
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var migrationSource = Directory.GetFiles(Path.Combine(SourceRepository, "server", "modules", "Part", "Migrations.SqlServer"), "20261008102916_Part_000001_Metadata.cs", SearchOption.AllDirectories).Single();
            Artifacts.Add("Part", new(Path.Combine(AppContext.BaseDirectory, "PartModule.dll"), frontend,
                new Dictionary<string, string> { ["migrations/Part.SqlServerMigrations.dll"] = Path.Combine(AppContext.BaseDirectory, "Part.SqlServerMigrations.dll") },
                new Dictionary<string, string> { ["migrations/sources/sqlServer/20261008102916_Part_000001_Metadata.cs"] = migrationSource }));
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
