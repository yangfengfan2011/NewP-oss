using ICP.Platform.Tools.ModelDtoSync;

try
{
    var arguments = args.ToList();
    var checkOnly = arguments.Remove("--check");
    if (arguments.Count > 1 || arguments.Any(static argument => argument.StartsWith("--", StringComparison.Ordinal)))
    {
        throw new ArgumentException("用法：ModelDtoSync [--check] [repository-root]");
    }

    var repositoryRoot = arguments.Count == 1
        ? Path.GetFullPath(arguments[0])
        : FindRepositoryRoot();

    if (checkOnly)
    {
        var driftedFiles = ModuleDtoSyncRunner.Check(repositoryRoot);
        if (driftedFiles.Count == 0)
        {
            Console.WriteLine("model-dto-sync check passed.");
            return 0;
        }

        Console.Error.WriteLine("model-dto-sync 检测到生成物漂移：");
        foreach (var path in driftedFiles)
        {
            Console.Error.WriteLine(" - " + path);
        }
        Console.Error.WriteLine(
            "请运行：dotnet run --project ./server/tools/model-dto-sync/ModelDtoSync.csproj --no-restore -- .");
        return 1;
    }

    var writtenFiles = ModuleDtoSyncRunner.Run(repositoryRoot);
    foreach (var path in writtenFiles)
    {
        Console.WriteLine(path);
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("model-dto-sync 执行失败：" + ex.Message);
    return 1;
}

// 从工具当前目录向上定位包含新 authoring 根的仓库。
static string FindRepositoryRoot()
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, "server"))
            && Directory.Exists(Path.Combine(current.FullName, "frontend", "modules")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("无法定位仓库根目录，请在仓库内运行或传入仓库根路径。");
}
