namespace ICP.Platform.Query.Runtime;

/// <summary>
/// 表示平台查询契约校验失败。
/// Code 需要保持稳定，供 endpoint 和调用方映射为可预期错误响应。
/// </summary>
public sealed class PlatformQueryValidationException : InvalidOperationException
{
    /// <summary>
    /// 初始化平台查询校验异常。
    /// </summary>
    /// <param name="code">稳定错误码。</param>
    /// <param name="message">面向日志和诊断的错误消息。</param>
    public PlatformQueryValidationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>
    /// 获取稳定错误码。
    /// </summary>
    public string Code { get; }
}
