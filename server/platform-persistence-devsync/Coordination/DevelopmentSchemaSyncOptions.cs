using ICP.Platform.Persistence.DevSync.Planning;

namespace ICP.Platform.Persistence.DevSync.Coordination;

/// <summary>
/// 开发态结构同步的共享内核选项。
/// 该对象只承载可注入的运行输入，不包含 Console、CLI 参数或 Host 启动细节。
/// </summary>
/// <param name="ConfirmApply">
/// 可选的执行确认策略；CLI 可在此桥接交互确认，Host/非交互路径未提供时即使 <paramref name="ApplyChanges"/> 为 true 也会拒绝执行。
/// </param>
public sealed record DevelopmentSchemaSyncOptions(
    string RepositoryRoot,
    string ConnectionString,
    string Provider,
    bool ApplyChanges = false,
    bool GenerateSqlPreview = false,
    Func<SchemaPlan, bool>? ConfirmApply = null)
{
    /// <summary>
    /// 校验当前选项是否满足共享内核的最小运行前置条件。
    /// </summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RepositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(ConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(Provider);

        if (!string.Equals(Provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"开发态结构同步当前仅支持 SqlServer，实际 provider 为 '{Provider}'。");
        }
    }
}
