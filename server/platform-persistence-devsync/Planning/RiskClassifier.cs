namespace ICP.Platform.Persistence.DevSync.Planning;

/// <summary>
/// 将结构操作映射到任务 7 的风险级别。
/// 当前只有 Safe 操作可在显式确认后执行，Confirm 操作由协调器统一阻断。
/// </summary>
public sealed class RiskClassifier
{
    /// <summary>
    /// 根据结构动作类型返回默认风险级别，供计划输出与后续确认流程复用。
    /// </summary>
    public string Classify(string operationKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);

        return operationKind switch
        {
            SchemaOperationKind.DropColumn => RiskLevel.Confirm,
            SchemaOperationKind.RenameTable => RiskLevel.Confirm,
            SchemaOperationKind.RenameColumn => RiskLevel.Confirm,
            SchemaOperationKind.AlterColumnType => RiskLevel.Confirm,
            SchemaOperationKind.AlterNullability => RiskLevel.Confirm,
            _ => RiskLevel.Safe
        };
    }
}
