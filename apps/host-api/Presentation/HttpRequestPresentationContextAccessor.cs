using ICP.Platform.Presentation;

namespace ICP.Platform.HostApi.Presentation;

/// <summary>
/// 从当前 HTTP 请求读取 Host 已校验的展示上下文。
/// </summary>
public sealed class HttpRequestPresentationContextAccessor(IHttpContextAccessor httpContextAccessor)
    : IRequestPresentationContextAccessor
{
    /// <summary>Host 在当前请求 Items 中存取展示上下文的私有键。</summary>
    public static object ItemKey { get; } = new();

    /// <inheritdoc />
    public RequestPresentationContext Current =>
        httpContextAccessor.HttpContext?.Items[ItemKey] as RequestPresentationContext
        ?? throw new InvalidOperationException("REQUEST_PRESENTATION_CONTEXT_UNAVAILABLE");
}
