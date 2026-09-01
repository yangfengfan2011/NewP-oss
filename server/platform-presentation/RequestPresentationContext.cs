namespace ICP.Platform.Presentation;

public sealed record RequestPresentationContext(string Locale, string TimeZoneId);

public interface IRequestPresentationContextAccessor
{
    RequestPresentationContext Current { get; }
}
