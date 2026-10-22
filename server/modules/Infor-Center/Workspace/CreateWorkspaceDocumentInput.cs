using System.Text.Json;
using System.Text.Json.Serialization;
using ICP.Platform.Modules.Document.Contracts;

namespace ICP.Platform.Modules.Workspace;

/// <summary>仅图文档接收显式文件选择；Folder/Part/编辑的既有 DTO 不改变。</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateWorkspaceDocumentInput
{
    [JsonConverter(typeof(WorkspaceDocumentPositionConverter))]
    public required WorkspacePosition Position { get; init; }
    public required Dictionary<string, JsonElement> Values { get; init; }
    public DocumentUploadSet? Uploads { get; init; }
}

/// <summary>新文件入口的 Position 严格接收三项现有身份，不影响其他位置协议的兼容形状。</summary>
public sealed class WorkspaceDocumentPositionConverter : JsonConverter<WorkspacePosition>
{
    /// <inheritdoc/>
    public override WorkspacePosition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Read(document.RootElement);
    }

    /// <summary>multipart 与 JSON 创建共用完整 Position 类型与未知成员检查。</summary>
    public static WorkspacePosition Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException("Position must be an object.");
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
            if (property.Name is null || !new[] { "spaceId", "rootFolderId", "linkIds" }.Contains(property.Name, StringComparer.OrdinalIgnoreCase)
                || !fields.TryAdd(property.Name, property.Value)) throw new JsonException("Position has an unknown or duplicate member.");
        if (!fields.TryGetValue("spaceId", out var space) || space.ValueKind != JsonValueKind.String
            || !fields.TryGetValue("rootFolderId", out var root) || root.ValueKind != JsonValueKind.String
            || !fields.TryGetValue("linkIds", out var links) || links.ValueKind != JsonValueKind.Array
            || links.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String)) throw new JsonException("Position is incomplete.");
        return new(space.GetString()!, root.GetString()!, links.EnumerateArray().Select(item => item.GetString()!).ToArray());
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, WorkspacePosition value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("spaceId", value.SpaceId); writer.WriteString("rootFolderId", value.RootFolderId);
        writer.WritePropertyName("linkIds"); JsonSerializer.Serialize(writer, value.LinkIds, options);
        writer.WriteEndObject();
    }
}
