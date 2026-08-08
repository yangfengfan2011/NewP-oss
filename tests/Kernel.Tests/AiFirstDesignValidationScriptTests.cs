using System.Diagnostics;
using System.Text;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class AiFirstDesignValidationScriptTests : IDisposable
{
    private static readonly string[] RequiredChecklistItems =
    [
        "入口在哪里",
        "边界在哪里",
        "如何验证",
        "失败信号是什么",
        "如何回滚"
    ];

    private readonly List<string> _tempRoots = new();

    [Fact]
    public void ValidateAiFirstDesign_PassesForDocumentContainingRequiredChecklist()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempRoot(repoRoot);
        var designPath = Path.Combine(tempRoot, "valid-design.md");
        File.WriteAllText(
            designPath,
            """
            # 示例设计

            ## AI-First 最小检查清单

            - 入口在哪里：`scripts/validate-ai-first-design.ps1`
            - 边界在哪里：仅覆盖本轮新增设计文档
            - 如何验证：运行定向脚本
            - 失败信号是什么：脚本返回非零退出码
            - 如何回滚：删除本轮新增脚本并恢复模板
            """);

        var result = RunPowerShellScript(
            repoRoot,
            Path.Combine(repoRoot, "scripts", "validate-ai-first-design.ps1"),
            ["-Path", designPath]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("validation passed", result.StdOut);
    }

    [Fact]
    public void ValidateAiFirstDesign_FailsWhenChecklistItemMissing()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempRoot(repoRoot);
        var designPath = Path.Combine(tempRoot, "missing-item-design.md");
        File.WriteAllText(
            designPath,
            """
            # 示例设计

            ## AI-First 最小检查清单

            - 入口在哪里：`docs/designs/detailed-design-template.md`
            - 边界在哪里：只改模板和校验脚本
            - 如何验证：运行 Kernel.Tests
            - 如何回滚：恢复本轮改动
            """);

        var result = RunPowerShellScript(
            repoRoot,
            Path.Combine(repoRoot, "scripts", "validate-ai-first-design.ps1"),
            ["-Path", designPath]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("失败信号是什么", result.StdErr);
    }

    [Theory]
    [InlineData("TODO")]
    [InlineData("TBD")]
    [InlineData("待补")]
    [InlineData("占位")]
    public void ValidateAiFirstDesign_FailsWhenPlaceholderExists(string placeholder)
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempRoot(repoRoot);
        var designPath = Path.Combine(tempRoot, $"placeholder-{placeholder}.md");
        File.WriteAllText(
            designPath,
            $$"""
            # 示例设计

            ## AI-First 最小检查清单

            - 入口在哪里：`docs/designs/detailed-design-template.md`
            - 边界在哪里：只覆盖当前示例
            - 如何验证：运行校验脚本
            - 失败信号是什么：输出 {{placeholder}}
            - 如何回滚：删除当前示例

            {{placeholder}}
            """);

        var result = RunPowerShellScript(
            repoRoot,
            Path.Combine(repoRoot, "scripts", "validate-ai-first-design.ps1"),
            ["-Path", designPath]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(placeholder, result.StdErr);
    }

    [Fact]
    public void ValidateAiFirstDesign_PassesForDetailedDesignTemplate()
    {
        var repoRoot = FindRepositoryRoot();
        var templatePath = Path.Combine(repoRoot, "docs", "designs", "detailed-design-template.md");

        var result = RunPowerShellScript(
            repoRoot,
            Path.Combine(repoRoot, "scripts", "validate-ai-first-design.ps1"),
            ["-Path", templatePath]);

        Assert.Equal(0, result.ExitCode);
        foreach (var checklistItem in RequiredChecklistItems)
        {
            Assert.DoesNotContain(checklistItem, result.StdErr);
        }
    }

    [Fact]
    public void CheckAiFirstDesignTargets_PassesForControlledTargetSet()
    {
        var repoRoot = FindRepositoryRoot();

        var result = RunPowerShellScript(
            repoRoot,
            Path.Combine(repoRoot, "scripts", "check-ai-first-design-targets.ps1"),
            []);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("detailed-design-template.md", result.StdOut);
        Assert.Contains("ai-first-execution-standard.md", result.StdOut);
    }

    [Fact]
    public void CheckAiFirstDesignTargets_FailsWhenAdditionalDesignDoesNotMeetChecklist()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempRoot(repoRoot);
        var designPath = Path.Combine(tempRoot, "additional-invalid-design.md");
        File.WriteAllText(
            designPath,
            """
            # 附加设计

            - 入口在哪里：`docs/designs/detailed-design-template.md`
            - 边界在哪里：只覆盖附加设计
            - 如何验证：运行受控校验入口
            - 如何回滚：删除附加设计
            """);

        var result = RunPowerShellScript(
            repoRoot,
            Path.Combine(repoRoot, "scripts", "check-ai-first-design-targets.ps1"),
            ["-AdditionalPath", designPath]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("失败信号是什么", result.StdErr);
    }

    [Fact]
    public void CheckAiFirstDesignTargets_AutoDiscoversInvalidUntrackedDesignInDocsDesigns()
    {
        var repoRoot = FindRepositoryRoot();
        var designPath = Path.Combine(
            repoRoot,
            "docs",
            "designs",
            $"auto-discovery-{Guid.NewGuid():N}-design.md");

        try
        {
            File.WriteAllText(
                designPath,
                """
                # 自动发现设计

                - 入口在哪里：`docs/designs/detailed-design-template.md`
                - 边界在哪里：只覆盖自动发现示例
                - 如何验证：运行受控校验入口
                - 如何回滚：删除自动发现示例
                """);

            var result = RunPowerShellScript(
                repoRoot,
                Path.Combine(repoRoot, "scripts", "check-ai-first-design-targets.ps1"),
                []);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("失败信号是什么", result.StdErr);
        }
        finally
        {
            if (File.Exists(designPath))
            {
                File.Delete(designPath);
            }
        }
    }

    [Fact]
    public void CheckAiFirstDesignTargets_UsesExplicitTargetsFile()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempRoot(repoRoot);
        var invalidDesignPath = Path.Combine(tempRoot, "targets-file-invalid-design.md");
        var targetsFilePath = Path.Combine(tempRoot, "ai-first-targets.txt");

        File.WriteAllText(
            invalidDesignPath,
            """
            # 清单文件设计

            - 入口在哪里：`docs/designs/detailed-design-template.md`
            - 边界在哪里：只覆盖清单文件测试
            - 如何验证：运行受控校验入口
            - 如何回滚：删除测试文件
            """);

        File.WriteAllLines(
            targetsFilePath,
            [
                @".\docs\designs\detailed-design-template.md",
                invalidDesignPath
            ]);

        var result = RunPowerShellScript(
            repoRoot,
            Path.Combine(repoRoot, "scripts", "check-ai-first-design-targets.ps1"),
            [
                "-TargetsFile", targetsFilePath,
                "-IncludeChangedDesigns", "0"
            ]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("失败信号是什么", result.StdErr);
    }

    [Fact]
    public void RegisterAiFirstDesignTarget_AppendsRelativeDesignPathOnlyOnce()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempRoot(repoRoot);
        var architectureDir = Path.Combine(tempRoot, "docs", "architecture");
        var designsDir = Path.Combine(tempRoot, "docs", "designs");
        Directory.CreateDirectory(architectureDir);
        Directory.CreateDirectory(designsDir);

        var targetsFilePath = Path.Combine(architectureDir, "ai-first-design-check-targets.txt");
        var designPath = Path.Combine(designsDir, "sample-design.md");

        File.WriteAllLines(
            targetsFilePath,
            [
                "# test targets",
                "../designs/detailed-design-template.md"
            ]);

        File.WriteAllText(
            designPath,
            """
            # 示例设计

            ## AI-First 最小检查清单

            - 入口在哪里：`docs/designs/detailed-design-template.md`
            - 边界在哪里：只覆盖登记脚本测试
            - 如何验证：运行登记脚本
            - 失败信号是什么：登记失败
            - 如何回滚：删除清单中的新增项
            """);

        var scriptPath = Path.Combine(repoRoot, "scripts", "register-ai-first-design-target.ps1");

        var firstRun = RunPowerShellScript(
            repoRoot,
            scriptPath,
            [
                "-DesignPath", designPath,
                "-TargetsFile", targetsFilePath
            ]);

        var secondRun = RunPowerShellScript(
            repoRoot,
            scriptPath,
            [
                "-DesignPath", designPath,
                "-TargetsFile", targetsFilePath
            ]);

        Assert.Equal(0, firstRun.ExitCode);
        Assert.Equal(0, secondRun.ExitCode);

        var lines = File.ReadAllLines(targetsFilePath);
        var registeredLines = lines.Where(line => line.Contains("sample-design.md", StringComparison.OrdinalIgnoreCase)).ToArray();

        Assert.Single(registeredLines);
        Assert.Equal("../designs/sample-design.md", registeredLines[0]);
    }

    [Fact]
    public void NewDesignDoc_CreatesDesignFromTemplateAndRegistersTarget()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempRoot(repoRoot);
        var architectureDir = Path.Combine(tempRoot, "docs", "architecture");
        var designsDir = Path.Combine(tempRoot, "docs", "designs");
        Directory.CreateDirectory(architectureDir);
        Directory.CreateDirectory(designsDir);

        var templatePath = Path.Combine(designsDir, "detailed-design-template.md");
        var targetsFilePath = Path.Combine(architectureDir, "ai-first-design-check-targets.txt");
        File.WriteAllText(
            templatePath,
            """
            # 模板内容

            ## AI-First 最小检查清单

            - 入口在哪里：`scripts/new-design-doc.ps1`
            - 边界在哪里：仅覆盖设计创建脚本测试
            - 如何验证：运行设计创建脚本测试
            - 失败信号是什么：脚本返回非零退出码
            - 如何回滚：删除本轮新增设计文件与目标清单项
            """,
            Encoding.UTF8);
        File.WriteAllLines(targetsFilePath, ["# targets"]);

        var scriptPath = Path.Combine(repoRoot, "scripts", "new-design-doc.ps1");
        var result = RunPowerShellScript(
            repoRoot,
            scriptPath,
            [
                "-DesignName", "sample-capability",
                "-DesignsRoot", designsDir,
                "-TemplatePath", templatePath,
                "-TargetsFile", targetsFilePath,
                "-RegisterAiFirst", "1"
            ]);

        var todayPrefix = DateTime.Today.ToString("yyyy-MM-dd");
        var createdDesignPath = Path.Combine(designsDir, $"{todayPrefix}-sample-capability-design.md");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(createdDesignPath));
        Assert.Equal(File.ReadAllText(templatePath, Encoding.UTF8), File.ReadAllText(createdDesignPath, Encoding.UTF8));

        var targets = File.ReadAllLines(targetsFilePath, Encoding.UTF8);
        Assert.Contains($"../designs/{todayPrefix}-sample-capability-design.md", targets);
    }

    [Fact]
    public void NewDesignDoc_DoesNotDuplicateExistingDatePrefix()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempRoot(repoRoot);
        var architectureDir = Path.Combine(tempRoot, "docs", "architecture");
        var designsDir = Path.Combine(tempRoot, "docs", "designs");
        Directory.CreateDirectory(architectureDir);
        Directory.CreateDirectory(designsDir);

        var templatePath = Path.Combine(designsDir, "detailed-design-template.md");
        var targetsFilePath = Path.Combine(architectureDir, "ai-first-design-check-targets.txt");
        File.WriteAllText(
            templatePath,
            """
            # 模板内容

            ## AI-First 最小检查清单

            - 入口在哪里：`scripts/new-design-doc.ps1`
            - 边界在哪里：仅覆盖设计创建脚本测试
            - 如何验证：运行设计创建脚本测试
            - 失败信号是什么：脚本返回非零退出码
            - 如何回滚：删除本轮新增设计文件与目标清单项
            """,
            Encoding.UTF8);
        File.WriteAllLines(targetsFilePath, ["# targets"]);

        var scriptPath = Path.Combine(repoRoot, "scripts", "new-design-doc.ps1");
        var result = RunPowerShellScript(
            repoRoot,
            scriptPath,
            [
                "-DesignName", "2026-04-30-sample-capability",
                "-DesignsRoot", designsDir,
                "-TemplatePath", templatePath,
                "-TargetsFile", targetsFilePath,
                "-RegisterAiFirst", "1"
            ]);

        var createdDesignPath = Path.Combine(designsDir, "2026-04-30-sample-capability-design.md");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(createdDesignPath));
        Assert.False(File.Exists(Path.Combine(designsDir, $"{DateTime.Today:yyyy-MM-dd}-2026-04-30-sample-capability-design.md")));

        var targets = File.ReadAllLines(targetsFilePath, Encoding.UTF8);
        Assert.Contains("../designs/2026-04-30-sample-capability-design.md", targets);
    }

    [Fact]
    public void NewDesignDoc_CreatesDesignUnderCategoryWhenProvided()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempRoot(repoRoot);
        var architectureDir = Path.Combine(tempRoot, "docs", "architecture");
        var designsDir = Path.Combine(tempRoot, "docs", "designs");
        Directory.CreateDirectory(architectureDir);
        Directory.CreateDirectory(designsDir);

        var templatePath = Path.Combine(designsDir, "detailed-design-template.md");
        var targetsFilePath = Path.Combine(architectureDir, "ai-first-design-check-targets.txt");
        File.WriteAllText(
            templatePath,
            """
            # 模板内容

            ## AI-First 最小检查清单

            - 入口在哪里：`scripts/new-design-doc.ps1`
            - 边界在哪里：仅覆盖设计创建脚本分类测试
            - 如何验证：运行设计创建脚本测试
            - 失败信号是什么：脚本返回非零退出码
            - 如何回滚：删除本轮新增设计文件与目标清单项
            """,
            Encoding.UTF8);
        File.WriteAllLines(targetsFilePath, ["# targets"]);

        var scriptPath = Path.Combine(repoRoot, "scripts", "new-design-doc.ps1");
        var result = RunPowerShellScript(
            repoRoot,
            scriptPath,
            [
                "-DesignName", "categorized-capability",
                "-Category", "platform",
                "-DesignsRoot", designsDir,
                "-TemplatePath", templatePath,
                "-TargetsFile", targetsFilePath,
                "-RegisterAiFirst", "1"
            ]);

        var todayPrefix = DateTime.Today.ToString("yyyy-MM-dd");
        var createdDesignPath = Path.Combine(designsDir, "platform", $"{todayPrefix}-categorized-capability-design.md");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(createdDesignPath));

        var targets = File.ReadAllLines(targetsFilePath, Encoding.UTF8);
        Assert.Contains($"../designs/platform/{todayPrefix}-categorized-capability-design.md", targets);
    }

    [Fact]
    public void NewDesignDoc_FailsWhenTemplateDoesNotPassValidationAndDoesNotRegisterTarget()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempRoot(repoRoot);
        var architectureDir = Path.Combine(tempRoot, "docs", "architecture");
        var designsDir = Path.Combine(tempRoot, "docs", "designs");
        Directory.CreateDirectory(architectureDir);
        Directory.CreateDirectory(designsDir);

        var templatePath = Path.Combine(designsDir, "detailed-design-template.md");
        var targetsFilePath = Path.Combine(architectureDir, "ai-first-design-check-targets.txt");
        File.WriteAllText(
            templatePath,
            """
            # 无效模板

            ## AI-First 最小检查清单

            - 入口在哪里：`scripts/new-design-doc.ps1`
            - 边界在哪里：仅覆盖模板校验
            - 如何验证：运行脚本测试
            - 失败信号是什么：脚本返回非零退出码
            """,
            Encoding.UTF8);
        File.WriteAllLines(targetsFilePath, ["# targets"], Encoding.UTF8);

        var scriptPath = Path.Combine(repoRoot, "scripts", "new-design-doc.ps1");
        var result = RunPowerShellScript(
            repoRoot,
            scriptPath,
            [
                "-DesignName", "invalid-template-sample",
                "-DesignsRoot", designsDir,
                "-TemplatePath", templatePath,
                "-TargetsFile", targetsFilePath,
                "-RegisterAiFirst", "1"
            ]);

        var createdDesignPath = Path.Combine(designsDir, "invalid-template-sample-design.md");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("如何回滚", result.StdErr);
        Assert.False(File.Exists(createdDesignPath));

        var targets = File.ReadAllLines(targetsFilePath, Encoding.UTF8);
        Assert.DoesNotContain("../designs/invalid-template-sample-design.md", targets);
    }

    public void Dispose()
    {
        foreach (var tempRoot in _tempRoots)
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private string CreateTempRoot(string repoRoot)
    {
        var tempRoot = Path.Combine(repoRoot, "artifacts", "test-temp", $"ai-first-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        _tempRoots.Add(tempRoot);
        return tempRoot;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ICP-Platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static ScriptResult RunPowerShellScript(string workingDirectory, string scriptPath, IReadOnlyList<string> arguments)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add("& { [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); $target = $args[0]; $targetArgs = @($args | Select-Object -Skip 1); & $target @targetArgs }");
        process.StartInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdout.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderr.AppendLine(args.Data);
            }
        };

        Assert.True(process.Start(), $"Failed to start script {scriptPath}.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return new ScriptResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private sealed record ScriptResult(int ExitCode, string StdOut, string StdErr);
}
