using System.Text.Json.Serialization;

namespace ICP.Platform.Sdk.UserMessages;

/// <summary>
/// 后端返回给前端展示的共享用户消息契约。
/// 该 DTO 既可以表达成功态，也可以表达失败态；
/// <see cref="UserMessageException"/> 只负责桥接失败态到该契约。
/// </summary>
public sealed class UserMessageDto
{
    /// <summary>
    /// 是否成功。
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// 消息级别。
    /// </summary>
    [JsonPropertyName("level")]
    public UserMessageLevel Level { get; init; }

    /// <summary>
    /// 多语言消息键。
    /// </summary>
    [JsonPropertyName("messageKey")]
    public string MessageKey { get; init; } = string.Empty;

    /// <summary>
    /// 消息参数。
    /// </summary>
    [JsonPropertyName("messageArgs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, object?>? MessageArgs { get; init; }

    /// <summary>
    /// 兼容降级文本。
    /// </summary>
    [JsonPropertyName("fallbackMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FallbackMessage { get; init; }

    /// <summary>
    /// 稳定业务语义码。
    /// </summary>
    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; init; }

    /// <summary>
    /// 字段语义键。
    /// </summary>
    [JsonPropertyName("fieldKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FieldKey { get; init; }

    /// <summary>
    /// 动作语义键。
    /// </summary>
    [JsonPropertyName("actionKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActionKey { get; init; }

    /// <summary>
    /// 状态语义键。
    /// </summary>
    [JsonPropertyName("statusKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StatusKey { get; init; }

    /// <summary>
    /// 构造成功态用户消息。
    /// </summary>
    public static UserMessageDto SuccessMessage(
        string messageKey,
        IReadOnlyDictionary<string, object?>? messageArgs = null,
        string? fallbackMessage = null,
        string? code = null,
        string? fieldKey = null,
        string? actionKey = null,
        string? statusKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageKey);

        return new UserMessageDto
        {
            Success = true,
            Level = UserMessageLevel.success,
            MessageKey = messageKey,
            MessageArgs = messageArgs is null ? null : ValidateAndCloneMessageArgs(messageArgs),
            FallbackMessage = fallbackMessage,
            Code = code,
            FieldKey = fieldKey,
            ActionKey = actionKey,
            StatusKey = statusKey,
        };
    }

    /// <summary>
    /// 构造失败态用户消息。
    /// </summary>
    public static UserMessageDto Failure(
        string messageKey,
        IReadOnlyDictionary<string, object?>? messageArgs = null,
        string? fallbackMessage = null,
        string? code = null,
        string? fieldKey = null,
        string? actionKey = null,
        string? statusKey = null,
        UserMessageLevel level = UserMessageLevel.error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageKey);

        return new UserMessageDto
        {
            Success = false,
            Level = level,
            MessageKey = messageKey,
            MessageArgs = messageArgs is null ? null : ValidateAndCloneMessageArgs(messageArgs),
            FallbackMessage = fallbackMessage,
            Code = code,
            FieldKey = fieldKey,
            ActionKey = actionKey,
            StatusKey = statusKey,
        };
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
