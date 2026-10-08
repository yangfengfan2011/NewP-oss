using System.Security.Cryptography;
using ICP.Platform.Acceptance;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

/// <summary>
/// 模块闸门就是普通集成测试：失败直接是异常与栈，不再经过 event/verdict/failureCode
/// 那层无人消费的举证协议。与仓库既有的 SQL Server 集成测试一致，通过环境变量显式
/// 启用，未启用时报为跳过，因此日常 <c>dotnet test</c> 可以安全包含它们。
/// </summary>
[Collection(AcceptanceGateCollection.Name)]
public sealed class AcceptanceGateTests
{
    private const string RunEnvironmentVariable = "NEWP_RUN_ACCEPTANCE";

    [EnvironmentGatedFact(RunEnvironmentVariable, "一次性 SQL Server catalog、dev supervisor 与真实浏览器")]
    public async Task ExampleRequiredField_CompletesEveryScenario()
    {
        var (root, runId) = PrepareRun("example-required-field");
        await new ExampleRequiredFieldAcceptanceRunner(RepositoryRoot, root, runId).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "一次性 SQL Server catalog、dev supervisor 与真实浏览器")]
    public async Task IdentityFoundation_CompletesEveryScenario()
    {
        var (root, runId) = PrepareRun("identity-foundation");
        await new IdentityFoundationAcceptanceRunner(RepositoryRoot, root, runId).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "共享专用 SQL Server catalog、dev supervisor 与真实浏览器")]
    public async Task Personnel_CompletesEveryScenario()
    {
        var (root, runId) = PrepareRun("personnel");
        await new PersonnelAcceptanceRunner(RepositoryRoot, root, runId).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "功能授权真实启动、角色入口跳转与整组保存")]
    public async Task Authorization_CompletesEveryScenario()
    {
        var (root, runId) = PrepareRun("authorization");
        await new AuthorizationAcceptanceRunner(RepositoryRoot, root, runId).RunAsync(CancellationToken.None);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "工作空间双根入口、普通账号与同库真实 Host 进程重启")]
    public async Task Workspace_CompletesEveryScenario()
    {
        var (root, runId) = PrepareRun("workspace");
        await new WorkspaceAcceptanceRunner(RepositoryRoot, root, runId).RunAsync(CancellationToken.None);
    }
    // run id 参与一次性 catalog 名与资源根命名，必须是固定长度的十六进制。
    private static (string RunDirectory, string RunId) PrepareRun(string gate)
    {
        var runId = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        var runDirectory = Path.Combine(RepositoryRoot, ".codex-temp", "acceptance", gate, runId);
        Directory.CreateDirectory(runDirectory);
        return (runDirectory, runId);
    }

    private static string RepositoryRoot { get; } = LocateRepositoryRoot();

    private static string LocateRepositoryRoot()
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

// 闸门共享 bin/Release 输出、.codex-temp/dev/state.json 与本机端口，因此必须串行。
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AcceptanceGateCollection
{
    public const string Name = "acceptance-gates";
}
