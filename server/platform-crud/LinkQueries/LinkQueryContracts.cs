using System.Linq.Expressions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Crud.LinkQueries;

/// <summary>类型身份与真实主键始终一起传输，不从 ID 文本推断模型。</summary>
public sealed record LinkObjectReference(string ModelKey, string Id);
/// <summary>关系结果保留原始存储方向；AssociatedObject 随查询方向变化。</summary>
public sealed record LinkQueryRow(string ResourceKey, string LinkId, LinkObjectReference Parent,
    LinkObjectReference Child, LinkObjectReference AssociatedObject, IReadOnlyDictionary<string, object?> Fields, bool HasChildren = false);
/// <summary>通用关系列表保持标准分页结果形状，不把关系投影伪装成 Entity。</summary>
public sealed record LinkQueryResult(IReadOnlyList<LinkQueryRow> Items, int Total, int Page, int PageSize, string? SortBy, string? SortDirection);
/// <summary>直接关系查询与标准列表使用相同的分页、字段和筛选语义。</summary>
public sealed record LinkQueryRequest(string Direction, LinkObjectReference Anchor, StandardCrudListQuery Query,
    IReadOnlyList<string>? Types = null);
/// <summary>已验证的唯一查询源，不建立独立于 linkedTypes 的业务类型清单。</summary>
public sealed record LinkQueryDefinition(ModelDefinition Model, LinkQueryMetadata Metadata,
    IReadOnlyDictionary<string, LinkQueryEndpoint> Endpoints, bool HasDataScopeResource = false);
public sealed record LinkQueryEndpoint(ModelDefinition Model, string ResourceKey, string IdField, string? ConstraintKey, string? TitleKey, string? Icon);

/// <summary>查询源拥有者提供强制上下文验证和源级读取条件，失败必须传播。</summary>
public interface ILinkQuerySourcePolicy
{
    string Key { get; }
    /// <summary>校验当前请求上下文及锚点；出现位置只作为定位输入，不能作为授权凭证。</summary>
    Task ValidateAsync(LinkQueryDefinition definition, LinkQueryRequest request, CancellationToken token);
    /// <summary>声明源级必须在计数和分页前下推的条件；null 只表示没有额外限制。</summary>
    Task<Expression<Func<Dictionary<string, object?>, bool>>?> ReadPredicateAsync(CancellationToken token);
}

/// <summary>端点拥有者显式提供 Link 读取的强制条件；不读取独立列表的临时筛选。</summary>
public interface ILinkQueryEndpointConstraint
{
    string Key { get; }
    /// <summary>返回可由 EF 翻译的强制条件；已声明约束不得返回缺失结果。</summary>
    Task<Expression<Func<Dictionary<string, object?>, bool>>> ReadPredicateAsync(LinkQueryEndpoint endpoint, CancellationToken token);
}

/// <summary>安全地将输入或业务拒绝翻译为稳定 Problem Details，不暴露隐藏节点信息。</summary>
public sealed class LinkQueryException(int status, string code, string? field = null) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string? Field { get; } = field;
}
