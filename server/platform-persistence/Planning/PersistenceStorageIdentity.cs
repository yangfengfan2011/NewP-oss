using System.Text.RegularExpressions;

namespace ICP.Platform.Persistence.Planning;

/// <summary>外部 owner 的物理 schema 身份，与全局模型名称及组合归属独立。</summary>
public static partial class PersistenceStorageIdentity
{
    /// <summary>为 authoring 与 runtime 使用同一确定的外部组件 schema。</summary>
    public static string ExternalSchema(string moduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        return "np_" + NonSchemaCharacterPattern().Replace(moduleId.ToLowerInvariant(), "_").Trim('_');
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSchemaCharacterPattern();
}
