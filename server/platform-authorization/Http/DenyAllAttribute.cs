using Microsoft.AspNetCore.Authorization;

namespace ICP.Platform.Authorization.Http;

/// <summary>
/// 显式冻结尚未开放的 endpoint；Host policy 保证任何主体都不能执行。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class DenyAllAttribute : AuthorizeAttribute
{
    public const string PolicyName = "Platform.DenyAll";

    public DenyAllAttribute(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason.Trim();
        Policy = PolicyName;
    }

    public string Reason { get; }
}
