using ICP.Platform.Presentation;

namespace ICP.Platform.HostApi.Presentation;

public sealed class HttpRequestPresentationContextAccessor(IHttpContextAccessor httpContextAccessor)
    : IRequestPresentationContextAccessor
{
    public static object ItemKey { get; } = new();

    public RequestPresentationContext Current =>
        httpContextAccessor.HttpContext?.Items[ItemKey] as RequestPresentationContext
        ?? throw new InvalidOperationException("REQUEST_PRESENTATION_CONTEXT_UNAVAILABLE");
}
