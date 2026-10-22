using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Modules.Document;

/// <summary>列表按编码或名称搜索，SQL 层仍交集真实资源读取权限。</summary>
public sealed class DocumentCrudQueryCustomizer : IStandardCrudQueryCustomizer
{
    public string ResourceKey => "document.document";

    /// <inheritdoc/>
    public IReadOnlyList<StandardCrudQueryCondition> BuildConditions(StandardCrudDescriptor descriptor, StandardCrudListQuery query)
        => query.Filters?.GetValueOrDefault("keyword") is { Length: > 0 } keyword
            ? [new("ECODE", "contains", keyword, "keyword", "or"), new("ENAME", "contains", keyword, "keyword", "or")] : [];
}
