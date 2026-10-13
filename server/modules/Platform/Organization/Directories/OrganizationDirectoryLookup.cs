namespace ICP.Platform.Modules.Organization.Directories;

/// <summary>
/// Organization 目录的分页读取条件；候选页与当前值分离，当前值不受启用筛选影响。
/// </summary>
/// <param name="Keyword">可选的名称或编码关键字。</param>
/// <param name="Page">从一开始的页码。</param>
/// <param name="PageSize">单页最大候选数。</param>
/// <param name="CurrentValueIds">需要独立解析的当前稳定标识。</param>
/// <param name="IncludeDisabled">是否在候选页中包含停用项；当前值不受此开关影响。</param>
public sealed record OrganizationDirectoryLookupRequest(
    string? Keyword,
    int Page,
    int PageSize,
    IReadOnlyCollection<string>? CurrentValueIds = null,
    bool IncludeDisabled = false);

/// <summary>
/// Organization 目录的一页候选与候选页外当前值。
/// </summary>
/// <param name="Items">符合候选条件的当前页。</param>
/// <param name="CurrentItems">独立解析的当前值。</param>
/// <param name="Page">规范化后的页码。</param>
/// <param name="PageSize">规范化后的单页大小。</param>
/// <param name="HasMore">是否还有后续候选页。</param>
public sealed record OrganizationDirectoryPage<T>(
    IReadOnlyList<T> Items,
    IReadOnlyList<OrganizationDirectoryCurrentItem<T>> CurrentItems,
    int Page,
    int PageSize,
    bool HasMore);

/// <summary>
/// 已存在的目录当前值；可读项携带真实档案，遮蔽项只保留关联标识，缺失项不返回。
/// </summary>
public abstract record OrganizationDirectoryCurrentItem<T>
{
    // 由嵌套状态构造当前值，遮蔽分支不持有组织档案。
    private OrganizationDirectoryCurrentItem() { }

    /// <summary>可读取的真实目录档案，其启用状态不被候选条件改写。</summary>
    public sealed record Readable(T Item) : OrganizationDirectoryCurrentItem<T>;

    /// <summary>存在但不可读取的当前关联，只携带稳定标识。</summary>
    public sealed record Masked(string Id) : OrganizationDirectoryCurrentItem<T>;
}
