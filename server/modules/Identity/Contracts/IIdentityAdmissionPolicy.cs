namespace ICP.Platform.Identity.Contracts;

/// <summary>账号准入的跨模块参与者；规则仍由登记模块拥有。</summary>
public interface IIdentityAdmissionPolicy
{
    /// <summary>会话创建及账号安全写入必须在最外层事务声明的协调模型；独立维护身份可返回空集合。</summary>
    IReadOnlyCollection<string> GetCoordinationModels(IdentityAccountSnapshot account);

    /// <summary>读取当前资格；确定不满足返回 false，暂不可验证必须抛出异常，不能冒充失效。</summary>
    ValueTask<bool> IsAllowedAsync(IdentityAccountSnapshot account, CancellationToken cancellationToken = default);
}
