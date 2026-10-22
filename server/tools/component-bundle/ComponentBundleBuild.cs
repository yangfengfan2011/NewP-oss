using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.PluginPlatform.ComponentBundles;

// 正式发行构建只固定这一入口的参数，不改变开发调试或已发布迁移的项目配置。
internal static class ComponentBundleBuild
{
    // 构建真实 owner 工程、前端正式产物，再交给公共 builder 生成内容索引。
    internal static async Task<ComponentBundleManifest> BuildAsync(string repositoryRoot, string rootModuleId, string outputRoot)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var catalog = ComponentSourceCatalog.Discover(repositoryRoot);
        var group = catalog.GetGroup(rootModuleId);
        using var runtime = JsonDocument.Parse(PlatformRuntimeReleaseIdentity.Descriptor);
        var storagePath = Path.Combine(repositoryRoot, "server", "platform-modeling", "metadata", "storage-types.json");
        using (var storageStream = File.OpenRead(storagePath))
            if (Convert.ToHexStringLower(SHA256.HashData(storageStream)) != runtime.RootElement.GetProperty("backend").GetProperty("storageTypeMapDigest").GetString())
                throw new InvalidDataException("COMPONENT_BUNDLE_RUNTIME_MISMATCH: source storage contract differs from controlled platform release.");
        var builtProjects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var artifacts = new Dictionary<string, ComponentBuiltArtifacts>(StringComparer.Ordinal);
        var releaseRoot = Path.Combine(repositoryRoot, ".codex-temp", "component-bundle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(releaseRoot);
        var releasePath = Path.Combine(releaseRoot, "platform-runtime-release.json");
        await File.WriteAllBytesAsync(releasePath, PlatformRuntimeReleaseIdentity.Descriptor);
        foreach (var component in group)
        {
            string? backend = null;
            string? frontendRoot = null;
            var migrationAssemblies = new Dictionary<string, string>(StringComparer.Ordinal);
            var migrationSources = new Dictionary<string, string>(StringComparer.Ordinal);
            if (component.Capsule.TryGetProperty("entryAssembly", out var entry))
                backend = await BuildProject(catalog.ResolveProject(repositoryRoot, component.ModuleId, entry.GetString()!), repositoryRoot, builtProjects);
            if (component.Capsule.TryGetProperty("persistence", out var persistence) && persistence.TryGetProperty("migrations", out var migrations))
            {
                foreach (var migration in migrations.EnumerateObject())
                {
                    var assembly = migration.Value.GetProperty("assembly").GetString()!;
                    var project = catalog.ResolveProject(repositoryRoot, component.ModuleId, assembly);
                    migrationAssemblies.Add(assembly, await BuildProject(project, repositoryRoot, builtProjects));
                    var manifestPath = ResolveDeclaredAsset(component.Root, migration.Value.GetProperty("manifest").GetString()!);
                    var manifest = MigrationManifestIntegrity.Parse(File.ReadAllBytes(manifestPath));
                    foreach (var step in manifest.Migrations)
                    {
                        var matches = Directory.EnumerateFiles(Path.GetDirectoryName(project)!, step.MigrationId + ".cs", SearchOption.AllDirectories)
                            .Where(path => !Path.GetRelativePath(Path.GetDirectoryName(project)!, path).Split(Path.DirectorySeparatorChar).Any(segment => segment is "obj" or "bin")).ToArray();
                        if (matches.Length != 1) throw new InvalidDataException($"COMPONENT_BUNDLE_MIGRATION_MISMATCH: '{component.ModuleId}/{step.MigrationId}' expects one canonical source.");
                        migrationSources.Add($"migrations/sources/{migration.Name}/{step.MigrationId}.cs", matches[0]);
                    }
                }
            }
            if (component.Capsule.TryGetProperty("frontend", out var frontend) && !(frontend.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False))
            {
                var key = frontend.GetProperty("entry").GetString()!;
                await Run("node", ["scripts/build-frontend-plugin.mjs", key, "--runtime-release", releasePath], Path.Combine(repositoryRoot, "frontend"));
                frontendRoot = Path.Combine(repositoryRoot, "artifacts", "frontend-plugins", key);
            }
            artifacts.Add(component.ModuleId, new(backend, frontendRoot, migrationAssemblies, migrationSources));
        }
        return new ComponentBundleBuilder().Stage(repositoryRoot, rootModuleId, outputRoot, artifacts);
    }

    // 通过正式 MSBuild 查询实际 TargetPath；不猜测 bin、框架或 solution 的配置映射。
    private static async Task<string> BuildProject(string project, string repositoryRoot, IDictionary<string, string> built)
    {
        if (built.TryGetValue(project, out var existing)) return existing;
        await Run("dotnet", ["build", project, "-c", "Release", "-p:DebugType=none", "-p:DebugSymbols=false",
            "-p:IncludeSourceRevisionInInformationalVersion=false", "-v:minimal"], repositoryRoot);
        var output = await Run("dotnet", ["msbuild", project, "-nologo", "-p:Configuration=Release", "-getProperty:TargetPath,TargetFramework"], repositoryRoot, capture: true);
        using var properties = JsonDocument.Parse(output);
        var path = properties.RootElement.GetProperty("Properties").GetProperty("TargetPath").GetString()!;
        using var runtime = JsonDocument.Parse(PlatformRuntimeReleaseIdentity.Descriptor);
        if (properties.RootElement.GetProperty("Properties").GetProperty("TargetFramework").GetString() != runtime.RootElement.GetProperty("backend").GetProperty("targetFramework").GetString())
            throw new InvalidDataException($"COMPONENT_BUNDLE_RUNTIME_MISMATCH: '{project}' target framework differs from controlled release.");
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new FileNotFoundException("COMPONENT_BUNDLE_ASSET_MISSING: MSBuild TargetPath not produced.", path);
        built.Add(project, path);
        return path;
    }

    // 子进程顺序运行并保留原始输出；任一真实构建失败就停止，不返回部分成功索引。
    private static async Task<string> Run(string fileName, IReadOnlyList<string> arguments, string cwd, bool capture = false)
    {
        var start = new ProcessStartInfo(fileName) { WorkingDirectory = cwd, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start '{fileName}'.");
        var outputTask = Pump(process.StandardOutput, capture);
        var errorTask = Pump(process.StandardError, false);
        await process.WaitForExitAsync();
        var output = await outputTask;
        await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException($"COMPONENT_BUNDLE_BUILD_FAILED: '{fileName}' exited {process.ExitCode}.");
        return output;
    }

    // 构建日志流式转发；仅短小的 MSBuild 属性查询需要返回捕获结果。
    private static async Task<string> Pump(StreamReader reader, bool capture)
    {
        var result = new System.Text.StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            Console.Error.WriteLine(line);
            if (capture) result.AppendLine(line);
        }
        return result.ToString();
    }

    // authoring 迁移引用只能在该组件源码根内；历史 entryAssembly 仅用于身份关联，不在此解析。
    private static string ResolveDeclaredAsset(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Contains(':')
            || relative.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException($"COMPONENT_BUNDLE_PATH_INVALID: '{relative}'.");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!File.Exists(path)) throw new FileNotFoundException("COMPONENT_BUNDLE_ASSET_MISSING: declared source asset not found.", path);
        return path;
    }
}
