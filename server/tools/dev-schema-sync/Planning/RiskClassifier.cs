namespace ICP.Platform.Tools.DevSchemaSync.Planning;

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
