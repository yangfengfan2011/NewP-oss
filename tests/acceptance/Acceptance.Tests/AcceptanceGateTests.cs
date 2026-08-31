using System.Security.Cryptography;
using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

/// <summary>
/// 把验收闸门作为普通集成测试运行：失败直接是断言失败与异常栈，
/// 不再经过 event/verdict/failureCode 这层无人消费的举证协议。
/// 与仓库既有的 SQL Server 集成测试一致，通过环境变量显式启用，未启用时跳过，
/// 因此 <c>dotnet test</c> 与 CI 可以安全包含它们。
/// </summary>
[Collection(AcceptanceGateCollection.Name)]
public sealed class AcceptanceGateTests
{
    private const string RunEnvironmentVariable = "NEWP_RUN_ACCEPTANCE";

    private static bool Enabled =>
        string.Equals(
            Environment.GetEnvironmentVariable(RunEnvironmentVariable),
            "1",
            StringComparison.Ordinal);

    [Fact]
    public async Task ExampleRequiredField_CompletesEveryScenario()
    {
        if (!Enabled)
        {
            return;
        }

        await RunGateAsync(AcceptanceGateSelection.ExampleRequiredField);
    }

    [Fact]
    public async Task IdentityFoundation_CompletesEveryScenario()
    {
        if (!Enabled)
        {
            return;
        }

        await RunGateAsync(AcceptanceGateSelection.IdentityFoundation);
    }

    [Fact]
    public async Task Personnel_CompletesEveryScenario()
    {
        if (!Enabled)
        {
            return;
        }

        await RunGateAsync(AcceptanceGateSelection.Personnel);
    }

    // 闸门共享 bin/Release 输出、.codex-temp/dev/state.json 与一次性 catalog，
    // 因此必须串行；并发运行会互相锁死构建产物并覆盖 supervisor 状态。
    private static async Task RunGateAsync(AcceptanceGateSelection gate)
    {
        var repositoryRoot = GetRepositoryRoot();
        var outputRoot = Path.Combine(repositoryRoot, ".codex-temp", "acceptance");
        var runId = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

        var outcome = await new AcceptanceSkeletonRunner().RunAsync(
            repositoryRoot,
            outputRoot,
            gate,
            runId);

        Assert.True(
            outcome.ExitCode == 0,
            $"{gate} 验收未通过，详见 {outcome.ResultPath}");
    }

    private static string GetRepositoryRoot()
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

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AcceptanceGateCollection
{
    public const string Name = "acceptance-gates";
}
