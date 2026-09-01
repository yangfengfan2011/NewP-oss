namespace ICP.Platform.Presentation;

/// <summary>描述当前请求已校验的 locale 与可信会话 IANA 时区。</summary>
public sealed record RequestPresentationContext(string Locale, string TimeZoneId);

/// <summary>向共享展示能力提供当前请求的展示上下文。</summary>
public interface IRequestPresentationContextAccessor
{
    /// <summary>获取当前请求已校验的展示上下文。</summary>
    RequestPresentationContext Current { get; }
}
