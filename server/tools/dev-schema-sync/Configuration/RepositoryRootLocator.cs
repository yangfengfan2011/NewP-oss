namespace ICP.Platform.Tools.DevSchemaSync.Configuration;

public static class RepositoryRootLocator
{
    /// <summary>
    /// 从 AppContext.BaseDirectory 向上查找包含 apps、server、modules 的仓库根。
    /// </summary>
    public static string Find()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "apps"))
                && Directory.Exists(Path.Combine(current.FullName, "server"))
                && Directory.Exists(Path.Combine(current.FullName, "modules")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录，未找到 apps、server、modules。");
    }
}
