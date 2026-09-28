namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>授权模块可预期拒绝的语言无关事实；诊断文本保留给日志及排错。</summary>
public class AuthorizationValidationException : InvalidOperationException
{
    public string Code { get; }
    public IReadOnlyDictionary<string, object?> Args { get; }
    public string? Field { get; }

    // 模块用例提供稳定原因和原值参数，不从诊断字符串反向推断。
    public AuthorizationValidationException(string message, string code, IReadOnlyDictionary<string, object?>? args = null, string? field = null)
        : base(message)
    {
        Code = code;
        Args = args ?? new Dictionary<string, object?>();
        Field = field;
    }
}
