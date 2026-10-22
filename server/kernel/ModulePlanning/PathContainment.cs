using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

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

    internal static VerifiedOpenFile OpenContainedFile(string rootPath, string candidatePath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var candidate = Path.GetFullPath(candidatePath);
        if (!Contains(root, candidate) || !File.Exists(candidate))
        {
            throw new FileNotFoundException("Artifact is missing or outside its trusted boundary.", candidate);
        }

        RejectReparsePoints(candidate);
        var stream = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var finalPath = ResolveFinalPath(stream.SafeFileHandle, candidate);
            if (!Contains(root, finalPath))
            {
                throw new InvalidOperationException(
                    $"Opened artifact resolves outside its trusted boundary: {candidate}");
            }

            // 路径检查只负责拒绝显式链接；最终归属由已打开句柄确认，后续读取也只走该句柄。
            RejectReparsePoints(candidate);
            return new VerifiedOpenFile(candidate, finalPath, stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    internal static byte[] ReadContainedFile(string rootPath, string candidatePath)
    {
        using var opened = OpenContainedFile(rootPath, candidatePath);
        return opened.ReadAllBytes();
    }

    private static string ResolveFinalPath(SafeFileHandle handle, string fallbackPath)
    {
        if (OperatingSystem.IsWindows())
        {
            var capacity = 512u;
            while (true)
            {
                var buffer = new StringBuilder(checked((int)capacity));
                var length = GetFinalPathNameByHandle(handle, buffer, capacity, 0);
                if (length == 0)
                {
                    throw new IOException(
                        $"Unable to resolve opened artifact path '{fallbackPath}'.",
                        new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
                }

                if (length < capacity)
                {
                    return NormalizeWindowsFinalPath(buffer.ToString());
                }

                capacity = checked(length + 1);
            }
        }

        if (OperatingSystem.IsLinux())
        {
            var descriptorPath = $"/proc/self/fd/{handle.DangerousGetHandle().ToInt64()}";
            var target = File.ResolveLinkTarget(descriptorPath, returnFinalTarget: true);
            if (target is not null)
            {
                return Path.GetFullPath(target.FullName);
            }
        }

        throw new PlatformNotSupportedException(
            $"Opened artifact path verification is unavailable on this platform: {fallbackPath}");
    }

    private static string NormalizeWindowsFinalPath(string path)
    {
        const string uncPrefix = @"\\?\UNC\";
        const string devicePrefix = @"\\?\";
        if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[uncPrefix.Length..];
        }

        return path.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase)
            ? path[devicePrefix.Length..]
            : path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);
}

internal sealed class VerifiedOpenFile : IDisposable
{
    private readonly FileStream _stream;

    internal VerifiedOpenFile(string requestedPath, string finalPath, FileStream stream)
    {
        RequestedPath = requestedPath;
        FinalPath = finalPath;
        _stream = stream;
    }

    internal string RequestedPath { get; }

    internal string FinalPath { get; }

    internal byte[] ReadAllBytes()
    {
        _stream.Position = 0;
        using var buffer = new MemoryStream();
        _stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public void Dispose() => _stream.Dispose();
}
