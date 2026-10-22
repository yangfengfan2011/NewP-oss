using System.Text.Json.Serialization;

namespace ICP.Platform.Sdk.UserMessages;

/// <summary>
/// 用户消息级别。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserMessageLevel
{
    info,
    success,
    warning,
    error,
}
