using System.Runtime.ExceptionServices;

namespace ICP.Platform.Acceptance;

/// <summary>
/// 三个验收闸门共同拥有的「一次运行」：场景归属、凭据登记与失败脱敏，
/// 以及「无论场景成败都归还资源、但清理失败不顶掉场景失败」的收尾顺序。
/// 子类只提供两件属于自己的事：跑哪些场景，以及怎么归还自己申请的资源。
/// </summary>
internal abstract class AcceptanceRunner
{
    // 子类实现遍布整个文件地读这几个值。这里用带下划线的 protected 字段而不是属性，
    // 是为了让收口的 diff 只包含结构变化，不掺进几百处纯改名。
    protected readonly string _repositoryRoot;
    protected readonly string _runDirectory;
    protected readonly string _runId;
    protected readonly List<string> _secretTokens = [];

    // 只由 RunScenarioAsync 写入：场景归属必须是「进入场景」的副产物，
    // 而不是要求每个调用点记得先赋值——漏一次就会把失败记到上一个场景头上。
    private string _activeScenario = "(尚未进入场景)";

    protected AcceptanceRunner(string repositoryRoot, string runDirectory, string runId)
    {
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _runDirectory = Path.GetFullPath(runDirectory);
        _runId = runId;
    }

    /// <summary>
    /// 跑完本闸门的全部场景并归还一次性资源。失败就是异常与栈，不产出裁决文件。
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ExceptionDispatchInfo? failure = null;
        try
        {
            await RunScenariosAsync(cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            // 取消不是场景失败：原样保留，让上层按取消处理而不是按断言失败处理。
            // 仍然先归还资源，所以这里只记录，不当场抛。
            failure = ExceptionDispatchInfo.Capture(exception);
        }
        catch (Exception exception)
        {
            // 脱敏后重建异常：原始文本可能带连接串口令，不能直接进测试输出。
            // 不挂 inner exception 是同一个原因；完整类型、消息与栈已在脱敏文本里。
            failure = ExceptionDispatchInfo.Capture(new InvalidOperationException(
                $"验收场景 {_activeScenario} 失败。{Environment.NewLine}{Sanitize(exception.ToString())}"));
        }

        // 收尾一律用 None：取消恰恰是最需要归还一次性 catalog 与 dev supervisor 的时刻，
        // 传一个已取消的 token 会让清理自己先被取消，把一次取消变成一地残留资源。
        var cleanup = await TeardownAsync(CancellationToken.None);
        var leaked = await ScanEvidenceForSecretsAsync(CancellationToken.None);

        // 三种失败都要报，但顺序固定：场景失败最具体，清理未完成最泛。
        failure?.Throw();
        if (leaked)
        {
            throw new InvalidOperationException("验收产物中出现了登记过的凭据片段。");
        }
        if (!string.Equals(cleanup.Status, "completed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"验收清理未完成：{cleanup.Diagnostic ?? cleanup.Status}");
        }
    }

    /// <summary>按顺序执行本闸门的场景；每个场景都经 <see cref="RunScenarioAsync"/> 进入。</summary>
    protected abstract Task RunScenariosAsync(CancellationToken cancellationToken);

    /// <summary>归还本闸门申请的一次性资源；失败返回结果而不是抛，让场景失败保持优先。</summary>
    protected abstract Task<AcceptanceCleanupResult> TeardownAsync(CancellationToken cancellationToken);

    // 进入一个场景：先登记归属再执行，失败诊断才能指到正确的场景名。
    protected Task RunScenarioAsync(
        string scenario,
        Func<CancellationToken, Task> body,
        CancellationToken cancellationToken)
    {
        _activeScenario = scenario;
        return body(cancellationToken);
    }

    // 登记一条连接的凭据形态；此后所有脱敏与证据扫描都认这组 token。
    protected void AddConnectionSecrets(string connectionString, string? password) =>
        AcceptanceInfrastructure.AddConnectionSecrets(_secretTokens, connectionString, password);

    // 任何要进入测试输出或证据的文本都先过这里。
    protected string Sanitize(string? value) => AcceptanceInfrastructure.Sanitize(_secretTokens, value);

    // 反向确认：脱敏之外，本轮产物里也不能有凭据以字节形式落盘。
    protected Task<bool> ScanEvidenceForSecretsAsync(CancellationToken cancellationToken) =>
        AcceptanceInfrastructure.ScanForSecretsAsync(_runDirectory, _secretTokens, cancellationToken);
}
