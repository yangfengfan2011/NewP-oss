using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace ICP.Platform.Persistence.Ownership;

/// <summary>
/// 表示由 Git common-dir 与 git-dir 派生的仓库/工作树数据库身份。
/// 分支名刻意不参与身份，避免切换分支时误切数据库。
/// </summary>
public sealed record WorktreeDatabaseIdentity(
    string RepositoryId,
    string WorktreeId,
    string CatalogName)
{
    public static WorktreeDatabaseIdentity FromGitPaths(string gitCommonDirectory, string gitDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitCommonDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitDirectory);

        var repositoryId = FingerprintPath(gitCommonDirectory);
        var worktreeId = FingerprintPath(gitDirectory);
        return new WorktreeDatabaseIdentity(
            repositoryId,
            worktreeId,
            $"NewP_{repositoryId[..12]}_{worktreeId[..12]}");
    }

    public static WorktreeDatabaseIdentity Resolve(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var commonDirectory = ResolveGitPath(repositoryRoot, "--git-common-dir");
        var gitDirectory = ResolveGitPath(repositoryRoot, "--git-dir");
        return FromGitPaths(commonDirectory, gitDirectory);
    }

    private static string ResolveGitPath(string repositoryRoot, string argument)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = Path.GetFullPath(repositoryRoot),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("--path-format=absolute");
        startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 git 以解析工作树数据库身份。");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException(
                $"无法解析工作树数据库身份。git exit code: {process.ExitCode}; {error.Trim()}");
        }

        return output.Trim();
    }

    private static string FingerprintPath(string path)
    {
        var normalized = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        if (OperatingSystem.IsWindows())
        {
            normalized = normalized.ToUpperInvariant();
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();
    }
}
