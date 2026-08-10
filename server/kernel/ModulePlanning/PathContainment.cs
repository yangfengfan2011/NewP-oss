namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>
/// 统一判断规范化路径是否落在指定目录边界内。
/// </summary>
internal static class PathContainment
{
    internal static bool Contains(string rootPath, string candidatePath, bool allowRoot = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);

        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(candidatePath);
        var relative = Path.GetRelativePath(root, candidate);
        if (Path.IsPathRooted(relative))
        {
            return false;
        }

        if (string.Equals(relative, ".", StringComparison.Ordinal))
        {
            return allowRoot;
        }

        var firstSeparator = relative.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var firstSegment = firstSeparator < 0 ? relative : relative[..firstSeparator];
        return !string.Equals(firstSegment, "..", StringComparison.Ordinal);
    }

    internal static void RejectReparsePoints(string path)
    {
        FileSystemInfo? current = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Path cannot traverse a reparse point: {current.FullName}");
            }

            current = current switch
            {
                DirectoryInfo directory => directory.Parent,
                FileInfo file => file.Directory,
                _ => null
            };
        }
    }
}
