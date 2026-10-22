using ICP.Platform.Kernel.ModulePlanning;

// MSBuild 只消费派生路径；Catalog 在短生命周期进程中运行，不锁住后续构建的程序集。
internal static class ComponentSourceBuildMapping
{
    // 将当前 Capsule 与原位产物关联为本轮 obj 路径映射，不写回 authoring。
    internal static void Write(string repositoryRoot, string projectPath, string configuration, string outputRoot, bool host, string? groupModuleId, string? excludedModuleIds = null)
    {
        var catalog = ComponentSourceCatalog.Discover(repositoryRoot);
        if (!string.IsNullOrWhiteSpace(groupModuleId)) catalog.GetGroup(groupModuleId);
        var capsules = new List<string>();
        var assets = new List<string>();
        var excluded = (excludedModuleIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        foreach (var component in catalog.Components)
        {
            if (host && excluded.Contains(component.ModuleId)) continue;
            if (component.Capsule.TryGetProperty("entryAssembly", out var entry))
            {
                var project = catalog.ResolveProject(repositoryRoot, component.ModuleId, entry.GetString()!);
                if (string.Equals(Path.GetFullPath(project), Path.GetFullPath(projectPath), StringComparison.OrdinalIgnoreCase))
                    capsules.Add(EscapeItem(component.CapsulePath));
            }
            if (!host) continue;
            AddAsset(assets, component.CapsulePath, "modules/" + component.ModuleId + "/plugin.json");
            foreach (var file in catalog.EnumerateOwnFiles(component.ModuleId))
            {
                var relative = Path.GetRelativePath(component.Root, file).Replace('\\', '/');
                if (relative.StartsWith("metadata/", StringComparison.Ordinal) || relative.StartsWith("migrations/", StringComparison.Ordinal) || relative.StartsWith("resources/", StringComparison.Ordinal))
                    AddAsset(assets, file, "modules/" + component.ModuleId + "/" + relative);
            }
            if (component.Capsule.TryGetProperty("persistence", out var persistence) && persistence.TryGetProperty("migrations", out var migrations))
            {
                foreach (var migration in migrations.EnumerateObject())
                {
                    var assemblyPath = migration.Value.GetProperty("assembly").GetString()!;
                    var project = catalog.ResolveProject(repositoryRoot, component.ModuleId, assemblyPath);
                    var framework = ComponentSourceCatalog.ReadProjectProperty(project, "TargetFramework")!;
                    var output = Path.Combine(Path.GetDirectoryName(project)!, "bin", configuration, framework, Path.GetFileName(assemblyPath));
                    if (!File.Exists(output)) throw new FileNotFoundException("Declared component migration output is missing.", output);
                    AddAsset(assets, output, "modules/" + component.ModuleId + "/" + assemblyPath);
                }
            }
        }
        // 历史 metadata 没有 Capsule，不授予发行身份；保留既有 Host 输出 owner。
        if (host)
        {
            var sourceRoot = Path.Combine(repositoryRoot, "frontend", "modules");
            foreach (var directory in ComponentSourceCatalog.EnumerateSourceDirectories(sourceRoot).Where(directory => !File.Exists(Path.Combine(directory, "newp.plugin.json"))))
            {
                var metadata = Path.Combine(directory, "metadata");
                if (!Directory.Exists(metadata)) continue;
                var id = string.Concat(Path.GetFileName(directory).Split('-').Select(segment => char.ToUpperInvariant(segment[0]) + segment[1..]));
                foreach (var file in Directory.EnumerateFiles(metadata, "*", SearchOption.AllDirectories))
                    AddAsset(assets, file, "modules/" + id + "/metadata/" + Path.GetRelativePath(metadata, file).Replace('\\', '/'));
            }
        }
        Directory.CreateDirectory(outputRoot);
        File.WriteAllLines(Path.Combine(outputRoot, "capsules.txt"), capsules);
        File.WriteAllLines(Path.Combine(outputRoot, "assets.txt"), assets);
    }

    // Windows 文件名不允许竖线，故分隔符不能与受支持源码路径冲突。
    private static void AddAsset(List<string> assets, string source, string target) => assets.Add(EscapeItem(source) + "|" + EscapeItem(target));

    // 派生文件采用 MSBuild item 转义，路径不能变成属性、分隔项或通配输入。
    private static string EscapeItem(string value) => value.Replace("%", "%25").Replace(";", "%3B").Replace("$", "%24")
        .Replace("@", "%40").Replace("'", "%27").Replace("(", "%28").Replace(")", "%29").Replace("*", "%2A").Replace("?", "%3F");
}
