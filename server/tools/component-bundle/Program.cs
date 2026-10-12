using System.Text.Json;
using ICP.Platform.PluginPlatform.ComponentBundles;

// 本入口只构建、打包和静态检查，不授予信任或修改部署状态。
try
{
    if (args.Length == 4 && args[0] == "build")
    {
        var manifest = await ComponentBundleBuild.BuildAsync(args[1], args[2], args[3]);
        Console.WriteLine(JsonSerializer.Serialize(manifest));
        return 0;
    }
    if (args.Length == 3 && args[0] == "pack")
    {
        var manifest = new ComponentBundleBuilder().Pack(args[1], args[2]);
        Console.WriteLine(JsonSerializer.Serialize(manifest));
        return 0;
    }
    if (args.Length == 2 && args[0] == "inspect")
    {
        var inspector = new ComponentBundleInspector();
        var isDirectory = Directory.Exists(args[1]);
        var inspectionRoot = isDirectory ? args[1] : Path.Combine(Path.GetTempPath(), "NewP-bundle-inspect-" + Guid.NewGuid().ToString("N"));
        var result = isDirectory ? inspector.Inspect(args[1]) : inspector.ExtractVerified(args[1], inspectionRoot);
        Console.WriteLine(JsonSerializer.Serialize(new { result.Manifest, Components = result.Components.Select(component => new
        { component.ModuleId, component.Version, component.BackendEntry, component.FrontendEntry, component.CompositionMembers, component.Persistence }) }));
        if (!isDirectory)
        {
            // 仅清理本次生成且已核对位于临时目录内的目标，绝不删除调用方输入。
            var resolvedRoot = Path.GetFullPath(inspectionRoot);
            var resolvedTemp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resolvedRoot.StartsWith(resolvedTemp, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe inspection cleanup path.");
            Directory.Delete(resolvedRoot, true);
        }
        return 0;
    }
    throw new ArgumentException("Usage: component-bundle build <repository-root> <group-module-id> <new-output-root> | pack <built-root> <new-zip> | inspect <package-root-or-zip>");
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
