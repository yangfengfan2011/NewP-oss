namespace ICP.Platform.Sdk.UserMessages;

/// <summary>
/// 携带共享用户消息契约的业务异常。
/// </summary>
public sealed class UserMessageException : Exception
{
    /// <summary>
    /// HTTP 状态码。
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// 多语言消息键。
    /// </summary>
    public string MessageKey { get; }

    /// <summary>
    /// 消息参数。
    /// </summary>
    public IReadOnlyDictionary<string, object?>? MessageArgs { get; }

    /// <summary>
    /// 兼容降级文本。
    /// </summary>
    public string? FallbackMessage { get; }

    /// <summary>
    /// 稳定业务语义码。
    /// </summary>
    public string? Code { get; }

    /// <summary>
    /// 字段语义键。
    /// </summary>
    public string? FieldKey { get; }

    /// <summary>
    /// 动作语义键。
    /// </summary>
    public string? ActionKey { get; }

    /// <summary>
    /// 状态语义键。
    /// </summary>
    public string? StatusKey { get; }

    /// <summary>
    /// 消息级别。
    /// </summary>
    public UserMessageLevel Level { get; }

    /// <summary>
    /// 创建用户消息异常。
    /// </summary>
    public UserMessageException(
        int statusCode,
        string messageKey,
        IReadOnlyDictionary<string, object?>? messageArgs = null,
        string? fallbackMessage = null,
        string? code = null,
        string? fieldKey = null,
        string? actionKey = null,
        string? statusKey = null,
        UserMessageLevel level = UserMessageLevel.error,
        Exception? innerException = null)
        : base(fallbackMessage ?? messageKey, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageKey);

        StatusCode = statusCode;
        MessageKey = messageKey;
        MessageArgs = messageArgs is null ? null : ValidateAndCloneMessageArgs(messageArgs);
        FallbackMessage = fallbackMessage;
        Code = code;
        FieldKey = fieldKey;
        ActionKey = actionKey;
        StatusKey = statusKey;
        Level = level;
    }

    /// <summary>
    /// 转换为共享 DTO，供 Controller 直接返回。
    /// </summary>
    public UserMessageDto ToDto()
    {
        return UserMessageDto.Failure(
            MessageKey,
            MessageArgs,
            FallbackMessage,
            Code,
            FieldKey,
            ActionKey,
            StatusKey,
            Level);
    }

    private static Dictionary<string, object?> ValidateAndCloneMessageArgs(IReadOnlyDictionary<string, object?> messageArgs)
    {
        var clonedMessageArgs = new Dictionary<string, object?>(messageArgs.Count);
        foreach (var (key, value) in messageArgs)
        {
            if (!IsSupportedMessageArgValue(value))
            {
                throw new ArgumentException("messageArgs only supports string, number, boolean, or null values.", nameof(messageArgs));
            }

            clonedMessageArgs[key] = value;
        }

        return clonedMessageArgs;
    }

    private static bool IsSupportedMessageArgValue(object? value)
    {
        if (value is null
            or string
            or bool
            or byte
            or sbyte
            or short
            or ushort
            or int
            or uint
            or long
            or ulong
            or decimal)
        {
            return true;
        }

        if (value is float floatValue)
        {
            return !float.IsNaN(floatValue) && !float.IsInfinity(floatValue);
        }

        if (value is double doubleValue)
        {
            return !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue);
        }

        return false;
    }
}
