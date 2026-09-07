using System.Diagnostics;
using System.Text;
using Xunit;

namespace ICP.Platform.RepositoryScripts.Tests;

public sealed class CurrentDocumentationValidationScriptTests
{
    private readonly string _repositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CheckCurrentDocs_ValidatesWorkingTreeAfterTrackedDocumentDeletion(
        bool stageDeletion,
        bool keepLink)
    {
        var root = await CreateTemporaryRepositoryAsync();
        try
        {
            var removedPath = Path.Combine(root, "removed.md");
            await File.WriteAllTextAsync(removedPath, "# Removed\n", Encoding.UTF8);
            var addResult = await RunProcessAsync("git", ["add", "--", "removed.md"], root);
            Assert.Equal(0, addResult.ExitCode);
            File.Delete(removedPath);
            if (stageDeletion)
            {
                var stageResult = await RunProcessAsync("git", ["add", "-u", "--", "removed.md"], root);
                Assert.Equal(0, stageResult.ExitCode);
            }

            // 保留一个未跟踪文档，证明检查仍覆盖工作区新增内容。
            await File.WriteAllTextAsync(
                Path.Combine(root, "README.md"),
                keepLink ? "[Removed](removed.md)\n" : "# Current\n",
                Encoding.UTF8);
            var indexPath = Path.Combine(root, ".git", "index");
            var indexBefore = await File.ReadAllBytesAsync(indexPath);

            var result = await RunCurrentDocsCheckAsync(root);

            Assert.Equal(indexBefore, await File.ReadAllBytesAsync(indexPath));
            Assert.DoesNotContain("MISSING_DOCUMENT", result.Output, StringComparison.Ordinal);
            if (keepLink)
            {
                Assert.Equal(1, result.ExitCode);
                Assert.Contains("BROKEN_LINK README.md:1", result.Output, StringComparison.Ordinal);
                Assert.Contains("removed.md", result.Output, StringComparison.Ordinal);
            }
            else
            {
                Assert.True(result.ExitCode == 0, result.Output);
                Assert.Contains("CURRENT_DOCS_CHECK_PASSED: 1 documents", result.Output, StringComparison.Ordinal);
            }
        }
        finally
        {
            // git add 生成的对象在 Windows 上只读，清理前仅解除本夹具文件的只读属性。
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            }
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckCurrentDocs_PassesForValidDocuments()
    {
        var root = await CreateTemporaryRepositoryAsync();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            await File.WriteAllTextAsync(
                Path.Combine(root, "README.md"),
                "# Current\n\n[Guide](docs/guide.md)\n",
                Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(root, "docs", "guide.md"), "# Guide\n", Encoding.UTF8);

            var result = await RunCurrentDocsCheckAsync(root);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("CURRENT_DOCS_CHECK_PASSED", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckCurrentDocs_FailsForBrokenLink()
    {
        var root = await CreateTemporaryRepositoryAsync();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "README.md"),
                "[Broken](docs/missing.md)\n",
                Encoding.UTF8);

            var result = await RunCurrentDocsCheckAsync(root);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("BROKEN_LINK", result.Output, StringComparison.Ordinal);
            Assert.Contains("README.md", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("docs/superpowers/specs/old-design.md")]
    [InlineData("docs/modules/old-boundary.md")]
    public async Task CheckCurrentDocs_FailsForRetiredWorkflowReference(string retiredReference)
    {
        var root = await CreateTemporaryRepositoryAsync();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "README.md"),
                $"# Current\n\nUse {retiredReference}.\n",
                Encoding.UTF8);

            var result = await RunCurrentDocsCheckAsync(root);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("RETIRED_WORKFLOW_REFERENCE", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckCurrentDocs_FailsForMachinePathAndMojibake()
    {
        var root = await CreateTemporaryRepositoryAsync();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "README.md"),
                "# Current\n\n```text\nD:\\Github\\Old\\Repo\n瀹¤\n```\n",
                Encoding.UTF8);

            var result = await RunCurrentDocsCheckAsync(root);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("MACHINE_PATH", result.Output, StringComparison.Ordinal);
            Assert.Contains("MOJIBAKE", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private async Task<ProcessResult> RunCurrentDocsCheckAsync(string root) =>
        await RunProcessAsync(
            "pwsh",
            [
                "-NoLogo",
                "-NoProfile",
                "-File",
                Path.Combine(_repositoryRoot, "scripts", "check-current-docs.ps1"),
                "-Root",
                root
            ],
            root);

    private static async Task<string> CreateTemporaryRepositoryAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "newp-current-docs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var result = await RunProcessAsync("git", ["init", "--quiet"], root);
        Assert.Equal(0, result.ExitCode);
        return root;
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start(), $"无法启动进程：{fileName}");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessResult(
            process.ExitCode,
            (await standardOutput) + Environment.NewLine + (await standardError));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "scripts", "check-current-docs.ps1"))
                && Directory.Exists(Path.Combine(current.FullName, "tests", "Kernel.Tests")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
