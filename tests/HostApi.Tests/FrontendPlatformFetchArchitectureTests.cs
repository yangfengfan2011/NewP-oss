using System.Text.RegularExpressions;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class FrontendPlatformFetchArchitectureTests
{
    [Fact]
    public void AuthenticatedWrites_DoNotBypassPlatformFetch()
    {
        var repositoryRoot = FindRepositoryRoot();
        var frontendRoot = Path.Combine(repositoryRoot, "frontend");
        var violations = Directory.EnumerateFiles(frontendRoot, "*.*", SearchOption.AllDirectories)
            .Where(static path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(static path => !path.Contains($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(
                Path.Combine("packages", "core", "src", "http", "platformFetch.ts"),
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => FindDirectWriteFetches(path)
                .Select(invocation => $"{Path.GetRelativePath(repositoryRoot, path)}: {invocation}"))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"认证写请求必须消费 @icp-platform/core 的 platformFetch，禁止直接 fetch：{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void StandardCrudClient_RequiresThePlatformFetchOwnerToBePassedExplicitly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sdkSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "packages",
            "sdk",
            "src",
            "standardCrud.ts"));

        Assert.DoesNotContain("fetcher: typeof fetch = fetch", sdkSource, StringComparison.Ordinal);

        var shellRoot = Path.Combine(repositoryRoot, "frontend", "packages", "shell", "src");
        var violations = Directory.EnumerateFiles(shellRoot, "*.*", SearchOption.AllDirectories)
            .Where(static path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            .Where(static path => !path.Contains(
                $"{Path.DirectorySeparatorChar}__tests__{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => CreateStandardCrudClientRegex().Matches(File.ReadAllText(path))
                .Select(match => new { path, invocation = match.Value }))
            .Where(static match => !match.invocation.Contains("platformFetch", StringComparison.Ordinal))
            .Select(match => Path.GetRelativePath(repositoryRoot, match.path))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Shell 标准 CRUD 必须显式注入唯一 platformFetch：{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static IEnumerable<string> FindDirectWriteFetches(string path)
    {
        var source = File.ReadAllText(path);
        foreach (Match match in DirectFetchStartRegex().Matches(source))
        {
            var invocation = ReadInvocation(source, match.Index);
            if (UnsafeMethodRegex().IsMatch(invocation))
            {
                yield return Regex.Replace(invocation, @"\s+", " ").Trim();
            }
        }
    }

    private static string ReadInvocation(string source, int start)
    {
        var open = source.IndexOf('(', start);
        var depth = 0;
        var quote = '\0';
        var escaped = false;
        for (var index = open; index < source.Length; index++)
        {
            var character = source[index];
            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '"' or '`')
            {
                quote = character;
            }
            else if (character == '(')
            {
                depth++;
            }
            else if (character == ')' && --depth == 0)
            {
                return source[start..(index + 1)];
            }
        }

        return source[start..];
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "frontend"))
                && File.Exists(Path.Combine(directory.FullName, "ICP-Platform.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the NewP repository root.");
    }

    [GeneratedRegex(@"(?<![\w])fetch\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex DirectFetchStartRegex();

    [GeneratedRegex("""\bmethod\s*:\s*['"](?:POST|PUT|PATCH|DELETE)['"]""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeMethodRegex();

    [GeneratedRegex(@"createStandardCrudClient\s*\([^;]+?\)", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex CreateStandardCrudClientRegex();
}
