namespace ICP.Platform.Authorization.Contracts;

/// <summary>单对象授权的正式路由目标与完整替换输入；配置中的身份不能覆盖路由。</summary>
public sealed record DataScopeObjectSaveInput(string ResourceKey, string ObjectId, DataScopeObjectConfiguration Configuration);

/// <summary>原 ACL 锁内交出的同一规范对象；写入续体只由授权能力的实际实现拥有。</summary>
public abstract record DataScopeObjectSaveState(ButtonExecutionTarget Target);

/// <summary>资源 owner 委托同一授权保存边界；按钮执行器仍拥有自动目标检查。</summary>
public interface IDataScopeObjectSaveHandler
    : IButtonBusinessHandler<DataScopeObjectSaveInput, DataScopeObjectSaveState, DataScopeAuthorizationResult>
{
}
