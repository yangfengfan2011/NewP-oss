using ICP.Platform.Tools.ModelDtoSync;

try
{
    var repositoryRoot = args.Length > 0
        ? Path.GetFullPath(args[0])
        : FindRepositoryRoot();

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

static string FindRepositoryRoot()
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current is not null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, "server"))
            && Directory.Exists(Path.Combine(current.FullName, "modules")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("无法定位仓库根目录，请在仓库内运行或传入仓库根路径。");
}
