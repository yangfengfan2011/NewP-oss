using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modeling.Ownership;

/// <summary>
/// 统一解释平台固定字段的写入所有权；显式 authoring 会保留现有主键所有权判定。
/// </summary>
public static class PlatformModelFieldOwnership
{
    private static readonly HashSet<string> EntityAuditFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "CreatedBy",
        "CreatedAt",
        "UpdatedBy",
        "UpdatedAt"
    };

    private static readonly HashSet<string> LinkAuditFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "LCreatedBy",
        "LCreatedAt",
        "LUpdatedBy",
        "LUpdatedAt"
    };

    /// <summary>
    /// 判断固定字段是否由服务端提供值；显式声明的 EID/LID 继续由调用方拥有。
    /// </summary>
    /// <param name="modelKind">Model authoring 的 Entity 或 Link kind。</param>
    /// <param name="fieldName">待判断字段名。</param>
    /// <param name="hasExplicitAuthoring">字段是否出现在 authoring fields 中。</param>
    /// <returns>审计字段始终由服务端拥有；未显式声明的固定主键由服务端拥有。</returns>
    public static bool IsServerOwned(
        string modelKind,
        string fieldName,
        bool hasExplicitAuthoring)
    {
        var isEntity = string.Equals(modelKind, "Entity", StringComparison.OrdinalIgnoreCase);
        var isLink = string.Equals(modelKind, "Link", StringComparison.OrdinalIgnoreCase);
        var isAuditField = isEntity
            ? EntityAuditFields.Contains(fieldName)
            : isLink && LinkAuditFields.Contains(fieldName);
        var isGeneratedPrimaryKey = !hasExplicitAuthoring
            && (isEntity && string.Equals(fieldName, "EID", StringComparison.OrdinalIgnoreCase)
                || isLink && string.Equals(fieldName, "LID", StringComparison.OrdinalIgnoreCase));
        return isAuditField || isGeneratedPrimaryKey;
    }

    /// <summary>
    /// 按规范字段 artifact 保留既有审计与主键所有权语义。
    /// </summary>
    /// <param name="field">规范 Model 字段定义。</param>
    /// <param name="hasExplicitAuthoring">字段是否出现在 authoring fields 中。</param>
    /// <returns>审计字段始终由服务端拥有；未显式声明的主键由服务端拥有。</returns>
    public static bool IsServerOwned(FieldDefinition field, bool hasExplicitAuthoring)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.IsAuditField || field.IsPrimaryKey && !hasExplicitAuthoring;
    }
}
