using System.Linq.Expressions;
using System.Text.Json;

namespace ICP.Platform.Authorization.Contracts;

/// <summary>规范资源身份显式映射到所属模型和对象标识字段；专用命令模块可通过 DI 贡献，标准 CRUD 由描述符贡献。</summary>
public sealed record DataScopeResource(string ResourceKey, string ModuleId, string ModelName, string IdField)
{
    /// <summary>模型身份始终包含声明 owner，已规范化的描述符不再重复加前缀。</summary>
    public string ModelKey => ModelName.Contains('/', StringComparison.Ordinal) ? ModelName : $"{ModuleId}/{ModelName}";
}

/// <summary>统一裁决的可组合读取贡献；null 表示本层不限制，不表示执行失败。</summary>
public sealed record DataScopeReadPlan(Expression<Func<Dictionary<string, object?>, bool>>? Predicate);

/// <summary>真实消费者在数据库计数和分页前取得同一读取裁决。</summary>
public interface IDataScopeReadPlanProvider
{
    /// <summary>按可信当前主体与有效配置编译目标资源的读取计划；依赖故障必须传播。</summary>
    Task<DataScopeReadPlan> GetReadPlanAsync(string resourceKey, CancellationToken cancellationToken = default);
}

/// <summary>为引用显示批量裁决既有对象 ID；结果只包含最终可读的目标，不携带业务事实。</summary>
public interface IDataScopeReadableIds
{
    /// <summary>按显式资源身份在数据库应用最终读取计划；执行故障必须传播。</summary>
    Task<IReadOnlySet<string>> GetReadableIdsAsync(string resourceKey, IReadOnlyCollection<string> objectIds,
        CancellationToken cancellationToken = default);
}

/// <summary>数据范围批量读取执行失败，不能按普通名称目录或摘要不可用降级。</summary>
/// <param name="resourceKey">发生故障的规范资源身份。</param>
/// <param name="innerException">原始授权或数据库执行故障。</param>
public sealed class DataScopeReadExecutionException(string resourceKey, Exception innerException)
    : InvalidOperationException($"DATA_SCOPE_READ_EXECUTION_FAILED: {resourceKey}", innerException);

/// <summary>普通对象动作复用读取依赖、规则树和对象覆盖的最终裁决。</summary>
public interface IDataScopeObjectDecisionProvider
{
    /// <summary>按当前可信主体与当前对象事实判断动作；读取拒绝和动作拒绝由调用方区分响应。</summary>
    Task<bool> IsAllowedAsync(string resourceKey, string objectId, string actionKey, CancellationToken cancellationToken = default);

    /// <summary>按规范化且包含服务端字段的完整待写候选判断新建，不要求对象已持久化。</summary>
    Task<bool> IsCandidateAllowedAsync(string resourceKey, IReadOnlyDictionary<string, object?> candidate,
        string actionKey, CancellationToken cancellationToken = default);
}

/// <summary>业务条件的适用 Model 和读取翻译能力由贡献模块明确登记。</summary>
public sealed record DataScopeBusinessConditionDefinition(string Key, string Title, IReadOnlyList<string> ModelKeys, bool HasReadContribution);

/// <summary>业务模块提供完整对象判断；只有显式声明时才提供数据库读取表达式。</summary>
public interface IDataScopeBusinessConditionProvider
{
    IReadOnlyList<DataScopeBusinessConditionDefinition> Definitions { get; }

    /// <summary>验证业务参数；无效引用须拒绝保存。</summary>
    void Validate(string key, JsonElement value);

    /// <summary>按真实对象身份执行完整判断；事实源故障须传播。</summary>
    Task<bool> MatchesAsync(string key, DataScopeResource resource, string objectId, JsonElement value, CancellationToken cancellationToken);

    /// <summary>新建候选的完整业务判断直接使用待写事实，不读取尚不存在的目标对象。</summary>
    Task<bool> MatchesCandidateAsync(string key, DataScopeResource resource, IReadOnlyDictionary<string, object?> candidate,
        JsonElement value, CancellationToken cancellationToken);

    /// <summary>构造可由数据库执行的读取贡献；未声明时不得调用。</summary>
    Expression BuildReadExpression(string key, DataScopeResource resource, ParameterExpression row, JsonElement value);
}

/// <summary>枚举字段的合法持久化值由字段所属模块提供。</summary>
public sealed record DataScopeEnumField(string ModelKey, string Field, IReadOnlyList<string> Values);

/// <summary>模块登记直接字段的合法枚举值，保存校验不得接受任意字符串。</summary>
public interface IDataScopeEnumFieldProvider
{
    IReadOnlyList<DataScopeEnumField> Fields { get; }
}
