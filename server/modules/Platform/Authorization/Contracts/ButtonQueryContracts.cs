namespace ICP.Platform.Authorization.Contracts;

/// <summary>正式查询目标保留原读取来源，不构造单对象身份或携带允许结果。</summary>
public sealed record ButtonQueryTarget(string ResourceKey, string ObjectName, DataExecutionQueryContext Source)
    : ButtonObjectTarget(ResourceKey, ObjectName);

/// <summary>仅一次实际业务执行中的查询来源；后续读取仍按当前主体生成SQL读取计划。</summary>
public sealed record ButtonQueryReadSource(ButtonBusinessInvocation Invocation, ButtonQueryTarget Target);

/// <summary>请求级查询来源范围；退出业务回调后立即失效，不能作为复用凭据。</summary>
public interface IButtonQueryReadScope
{
    ButtonQueryReadSource? Current { get; }
    /// <summary>按嵌套调用保存并恢复来源；不记录权限裁决。</summary>
    IDisposable Enter(ButtonBusinessInvocation invocation, ButtonQueryTarget target);
}
