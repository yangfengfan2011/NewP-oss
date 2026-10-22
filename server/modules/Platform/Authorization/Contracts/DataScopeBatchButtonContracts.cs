namespace ICP.Platform.Authorization.Contracts;

/// <summary>真实owner已解析的完整规范集合与同一批量配置；不会扫描配置中的其他ID。</summary>
public sealed record DataScopeBatchSaveInput(IReadOnlyList<DataScopeAuthorizationTarget> Targets, DataScopeBatchConfiguration Configuration);

/// <summary>全部ACL锁与资格保护内交出的同一对象集合，写入续体仅由授权能力拥有。</summary>
public abstract record DataScopeBatchSaveState(ButtonTargetSet Target);

/// <summary>批量与单对象共用原授权保存流程，平台对完整集合自动检查按钮要求。</summary>
public interface IDataScopeBatchSaveHandler
    : IButtonBusinessHandler<DataScopeBatchSaveInput, DataScopeBatchSaveState, DataScopeAuthorizationResult<DataScopeBatchAuthorizationReceipt>>
{
}

/// <summary>核对只接收当前完整规范目标，不接受重放模板或提交结果声明。</summary>
public sealed record DataScopeBatchReadInput(IReadOnlyList<DataScopeAuthorizationTarget> Targets);

/// <summary>核对和平台判权共用同一已锁定对象集合。</summary>
public abstract record DataScopeBatchReadState(ButtonTargetSet Target);

/// <summary>完整组现时资格不成立时不返回部分配置；核对不成为写入回执。</summary>
public interface IDataScopeBatchReadHandler
    : IButtonBusinessHandler<DataScopeBatchReadInput, DataScopeBatchReadState, DataScopeAuthorizationResult<IReadOnlyList<DataScopeObjectConfiguration>>>
{
}
