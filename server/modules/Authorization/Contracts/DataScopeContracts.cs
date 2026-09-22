using System.Linq.Expressions;

namespace ICP.Platform.Authorization.Contracts;

/// <summary>规范资源身份显式映射到所属模型和对象标识字段。</summary>
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
