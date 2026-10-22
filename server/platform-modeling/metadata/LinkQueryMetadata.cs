namespace ICP.Platform.Modeling.Metadata;

/// <summary>显式采用通用 Link 查询的声明；允许组合只来自所属模型的 linkedTypes。</summary>
public sealed record LinkQueryMetadata
{
    public required string ResourceKey { get; init; }
    public required string ReadBehavior { get; init; }
    public string? PolicyKey { get; init; }
    public required LinkQueryEndMetadata Parent { get; init; }
    public required LinkQueryEndMetadata Child { get; init; }
    public IReadOnlyDictionary<string, LinkQueryEndpointMetadata> Endpoints { get; init; } = new Dictionary<string, LinkQueryEndpointMetadata>();
    public IReadOnlyList<LinkQueryColumnMetadata> Columns { get; init; } = [];
    public string DefaultSortField { get; init; } = "UpdatedAt";
    public string DefaultSortDirection { get; init; } = "desc";
}

/// <summary>一端的引用字段和互斥的固定模型／类型判别字段。</summary>
public sealed record LinkQueryEndMetadata
{
    public required string ReferenceField { get; init; }
    public string? FixedModelKey { get; init; }
    public string? TypeField { get; init; }
}

/// <summary>关联端点的真实资源与可选强制读取条件，不继承独立页面的用户筛选。</summary>
public sealed record LinkQueryEndpointMetadata
{
    public required string ResourceKey { get; init; }
    public string? TitleKey { get; init; }
    public string? Icon { get; init; }
    public string? ConstraintKey { get; init; }
}

/// <summary>公共列的值语义、操作边界及按规范模型键的显式映射；null 映射只在可空列合法。</summary>
public sealed record LinkQueryColumnMetadata
{
    public required string Field { get; init; }
    public required string ValueType { get; init; }
    public bool Nullable { get; init; }
    public bool Sortable { get; init; }
    public IReadOnlyList<string> Operators { get; init; } = [];
    public IReadOnlyDictionary<string, string?> Mappings { get; init; } = new Dictionary<string, string?>();
}
