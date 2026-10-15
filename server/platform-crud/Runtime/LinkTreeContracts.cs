using System.Data.Common;
using ICP.Platform.Kernel.ModulePlanning;

namespace ICP.Platform.Crud.Runtime;

/// <summary>出现位置只固定根与有序 LID；实体身份不能代替路径。</summary>
public sealed record LinkTreePosition(string RootKey, string RootObjectId, IReadOnlyList<string> LinkIds);
public sealed record LinkTreeNode(string NodeKey, string NodeType, string Label, bool HasChildren, IReadOnlyDictionary<string, object?> Context, string? LabelKey = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? ContextMenuKeys = null);
public sealed record LinkTreePathItem(string NodeKey, string Label, string? LabelKey = null);
public sealed record LinkTreeSearchItem(LinkTreeNode Node, IReadOnlyList<LinkTreePathItem> Path);
public sealed record LinkTreeSearchPage(IReadOnlyList<LinkTreeSearchItem> Items, long Total, int Page, int PageSize);
/// <summary>可信策略的命令内根查询；固定输出 RootKey、RootObjectId、Label、LabelKey、RootOrder。</summary>
public sealed record LinkTreeRootSql(string Preparation, string RootsQuery, IReadOnlyList<DbParameter> Parameters);

/// <summary>模块提供业务根及安全上下文，平台拥有逐层读取、出现身份和所有树查询。</summary>
public interface ILinkTreeRootPolicy
{
    string Key { get; }
    string ScopeKind { get; }
    /// <summary>从可信请求主体产生参数化根查询；准备段不能返回结果集或执行业务写入。</summary>
    Task<LinkTreeRootSql> BuildAsync(LinkTreeDefinition definition, CancellationToken token);
    /// <summary>只映射已公开的根身份；不能覆盖平台 id、linkIds 或其他保留位置字段。</summary>
    IReadOnlyDictionary<string, object?> CreateContext(string rootKey, string rootObjectId);
}

/// <summary>与 encodeURIComponent 一致的出现位置编码，保留既有地址。</summary>
public static class LinkTreeNodeKey
{
    /// <summary>根键、根对象及每个 LID 分别编码，身份内的分隔符不会改变链。</summary>
    public static string Encode(LinkTreePosition position) => string.Join(':', new[] { position.RootKey, position.RootObjectId }.Concat(position.LinkIds).Select(EncodePart));

    /// <summary>恢复唯一的完整链；格式、空标识及重复关系不能退化为裸对象定位。</summary>
    public static LinkTreePosition Decode(string key)
    {
        try
        {
            var parts = key.Split(':').Select(Uri.UnescapeDataString).ToArray();
            if (parts.Length < 2 || parts.Any(string.IsNullOrWhiteSpace) || parts.Skip(2).Distinct(StringComparer.Ordinal).Count() != parts.Length - 2)
                throw new FormatException();
            return new(parts[0], parts[1], parts.Skip(2).ToArray());
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        { throw new LinkQueries.LinkQueryException(400, "Platform.TreeInvalidPosition", "nodeKey"); }
    }

    // RFC3986 与 JS 在这些未转义字符上不同，显式保留旧客户端生成的身份。
    private static string EncodePart(string value) => Uri.EscapeDataString(value).Replace("%21", "!", StringComparison.Ordinal)
        .Replace("%27", "'", StringComparison.Ordinal).Replace("%28", "(", StringComparison.Ordinal).Replace("%29", ")", StringComparison.Ordinal).Replace("%2A", "*", StringComparison.Ordinal);
}
