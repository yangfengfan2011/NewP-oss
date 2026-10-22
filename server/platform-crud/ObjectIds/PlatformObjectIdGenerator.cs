using System.Text.RegularExpressions;

namespace ICP.Platform.Crud.ObjectIds;

public sealed partial class PlatformObjectIdGenerator : IPlatformObjectIdGenerator
{
    /// <summary>
    /// 生成平台对象标识。这里不接受 resourceKey 或自由文本，避免点号、斜杠等进入 URL path 和数据库主键。
    /// </summary>
    public string NewId(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName) || !ObjectNamePattern().IsMatch(objectName))
        {
            throw new ArgumentException("对象名称必须匹配 ^[A-Za-z][A-Za-z0-9]*$。", nameof(objectName));
        }

        return $"{objectName}_{Guid.NewGuid():N}";
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectNamePattern();
}
