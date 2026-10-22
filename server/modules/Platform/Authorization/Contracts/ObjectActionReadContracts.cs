namespace ICP.Platform.Authorization.Contracts;

/// <summary>本次完整owner读取实际产生的规范身份和业务事实；不是读取结果凭证，不作为独立检查入口。</summary>
public sealed record ObjectActionRead<TRead>(string ObjectName, string ObjectId, TRead Value);

/// <summary>仅在现场完整读取的当前回调内有效；不保存允许结果，不能换目标、主体或请求复用。</summary>
public interface IObjectActionReadScope
{
    /// <summary>检查本次已规范化的同一目标的当前动作；全部通过才允许，业务自行保持其OR及调用次序。</summary>
    Task<DataExecutionDecision> CheckAsync(string objectName, string objectId, IReadOnlyList<string> actions,
        CancellationToken cancellationToken = default);
}
