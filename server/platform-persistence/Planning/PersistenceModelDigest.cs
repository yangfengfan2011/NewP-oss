using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Persistence.Planning;

/// <summary>
/// 为 provider 投影后的归属模型生成稳定内容摘要。
/// </summary>
public static class PersistenceModelDigest
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// 计算包含归属、物理映射与有序索引字段的规范模型摘要。
    /// </summary>
    public static string Compute(IEnumerable<ModelDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var models = definitions
            .OrderBy(static definition => definition.LogicalKey, StringComparer.Ordinal)
            .Select(static definition => new
            {
                definition.OwnerModuleId,
                definition.SchemaName,
                definition.Name,
                definition.TableName,
                kind = definition.Kind.ToString(),
                fields = definition.Fields
                    .OrderBy(static field => field.Name, StringComparer.Ordinal)
                    .Select(static field => new
                    {
                        field.Name,
                        field.StoreName,
                        kind = field.Kind.ToString(),
                        valueType = field.ValueType.ToString(),
                        field.StoreType,
                        field.Required,
                        field.IsPrimaryKey,
                        field.IsAuditField
                    }),
                indexes = definition.Indexes
                    .OrderBy(static index => index.Name, StringComparer.Ordinal)
                    .Select(static index => new
                    {
                        index.Name,
                        fields = index.Fields,
                        index.IsUnique
                    })
            })
            .ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(models, CanonicalJsonOptions);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
