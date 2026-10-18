namespace ICP.Platform.Authorization.Contracts;

/// <summary>从正式按钮声明取得要求；请求上下文须由既有功能授权 endpoint 提供。</summary>
public interface IActionDataAccess
{
    /// <summary>核验登记声明、执行关系及当前功能位置，再交集完整对象读取和声明的各数据动作。</summary>
    Task<bool> IsAllowedAsync(string execution, string resourceKey, string objectId, CancellationToken cancellationToken = default);
}

/// <summary>声明上下文无效与对象拒绝分开；依赖故障不转换为找不到。</summary>
public sealed class ActionDataAccessException(int status, string code) : InvalidOperationException(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
