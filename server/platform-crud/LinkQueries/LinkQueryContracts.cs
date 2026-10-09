using System.Linq.Expressions;
using System.Data.Common;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.DisplayPlanning;
using ICP.Platform.Query.Descriptors;

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
    IReadOnlyDictionary<string, LinkQueryEndpoint> Endpoints, IReadOnlyList<PlatformDisplayValueField> DisplayValueFields, bool HasDataScopeResource = false);
public sealed record LinkQueryEndpoint(ModelDefinition Model, string ResourceKey, string IdField, string? ConstraintKey, string? TitleKey, string? Icon, ModelDisplayProjectionPlan DisplayProjectionPlan);

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
    /// <summary>专属端点约束可声明唯一模型归属；通用旧表达式实现保持未限定。</summary>
    string? ModelKey => null;
    /// <summary>返回可由 EF 翻译的强制条件；已声明约束不得返回缺失结果。</summary>
    Task<Expression<Func<Dictionary<string, object?>, bool>>> ReadPredicateAsync(LinkQueryEndpoint endpoint, CancellationToken token);

    /// <summary>可信实现可补充同一数据库命令内的集合范围；名称由 reader 分配，SQL 和参数不得来自请求表达式。</summary>
    Task<LinkQuerySqlScope?> ReadSqlScopeAsync(LinkQueryEndpoint endpoint, string scopeName, CancellationToken token) => Task.FromResult<LinkQuerySqlScope?>(null);
}

/// <summary>端点 owner 的参数化 SQL 范围；Preparation 不返回结果集，IdsQuery 返回唯一一列对象主键。</summary>
public sealed record LinkQuerySqlScope(string Preparation, string IdsQuery, IReadOnlyList<DbParameter> Parameters)
{
    /// <summary>隔离可信 EF 查询中的参数 token；保留字面文本，不用字符串替换更改 SQL 值。</summary>
    public static string RenameParameters(string sql, IReadOnlyDictionary<string, string> names) => Runtime.RelationTreeReader.RenameParameters(sql, names);
}

/// <summary>安全地将输入或业务拒绝翻译为稳定 Problem Details，不暴露隐藏节点信息。</summary>
public sealed class LinkQueryException(int status, string code, string? field = null) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string? Field { get; } = field;
}
