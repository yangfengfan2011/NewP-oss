using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.PluginPlatform.ComponentBundles;
using ICP.Platform.Persistence.Planning;

namespace ICP.Platform.Testing;

internal sealed class ComponentBundleTestInput : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "NewP-270-bundle-" + Guid.NewGuid().ToString("N"));
    internal string Repository => Path.Combine(Root, "repository");
    internal string Output => Path.Combine(Root, "bundle");
    internal string Capsule => Path.Combine(Repository, "frontend", "modules", "infor-center", "newp.plugin.json");
    internal string SourceRepository { get; }
    internal Dictionary<string, ComponentBuiltArtifacts> Artifacts { get; } = new(StringComparer.Ordinal);

    internal ComponentBundleTestInput()
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
        var manifest = MigrationManifestIntegrity.Parse(File.ReadAllBytes(Path.Combine(part.Root, "migrations", "Part.SqlServerMigrations.json")));
        var migrationProject = catalog.ResolveProject(SourceRepository, "Part", "migrations/Part.SqlServerMigrations.dll");
        var migrationSources = manifest.Migrations.ToDictionary(
            migration => "migrations/sources/sqlServer/" + migration.MigrationId + ".cs",
            migration => Path.Combine(Path.GetDirectoryName(migrationProject)!, "Migrations", migration.MigrationId + ".cs"), StringComparer.Ordinal);
        Artifacts.Add("Part", new(Path.Combine(AppContext.BaseDirectory, "PartModule.dll"), frontend,
            new Dictionary<string, string> { ["migrations/Part.SqlServerMigrations.dll"] = Path.Combine(AppContext.BaseDirectory, "Part.SqlServerMigrations.dll") },
            migrationSources));
    }

    public void Dispose() => Directory.Delete(Root, true);
}
