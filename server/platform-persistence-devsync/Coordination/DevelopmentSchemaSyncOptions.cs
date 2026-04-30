using ICP.Platform.Persistence.DevSync.Planning;

namespace ICP.Platform.Persistence.DevSync.Coordination;

/// <summary>
/// 开发态结构同步的共享内核选项。
/// 该对象只承载可注入的运行输入，不包含 Console、CLI 参数或 Host 启动细节。
/// </summary>
/// <param name="ConfirmApply">
/// 可选的执行确认策略；CLI 可在此桥接交互确认，Host/非交互路径未提供时即使 <paramref name="ApplyChanges"/> 为 true 也会拒绝执行。
/// </param>
/// <param name="AllowConfirmOperations">
/// 是否允许 Confirm 风险计划进入执行阶段；默认关闭，Host 自动启动路径必须保持关闭。
/// </param>
/// <param name="ConfirmRisk">
/// Confirm 风险操作的二次确认策略；仅 CLI 交互路径应提供，未提供时执行器默认拒绝高风险操作。
/// </param>
public sealed record DevelopmentSchemaSyncOptions(
    string RepositoryRoot,
    string ConnectionString,
    string Provider,
    bool ApplyChanges = false,
    bool GenerateSqlPreview = false,
    Func<SchemaPlan, bool>? ConfirmApply = null,
    bool AllowConfirmOperations = false,
    Func<IReadOnlyList<SchemaOperation>, bool>? ConfirmRisk = null)
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
