using System.Data.Common;
using System.Linq.Expressions;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Models;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Crud.LinkQueries;

/// <summary>已声明源策略显式提供无定位请求的等价树读取条件；不支持时不得省略策略。</summary>
public interface ILinkTreeSourceReadPolicy
{
    string Key { get; }
    /// <summary>以正式查询定义下推关系条件；null 只表示该来源没有额外关系过滤。</summary>
    Task<Expression<Func<Dictionary<string, object?>, bool>>?> ReadPredicateAsync(LinkQueryDefinition definition, CancellationToken token);
}

/// <summary>普通 Link 与出现树共用源行的正式 read、策略谓词及 Customizer 交集。</summary>
public sealed class LinkSourceReadScopes(EfPlatformDbContext db, IDataScopeReadPlanProvider reads, IStandardCrudResourceRegistry resources,
    IEnumerable<IStandardCrudQueryCustomizer> customizers, IEnumerable<ILinkTreeSourceReadPolicy> treePolicies)
{
    /// <summary>树不能静默跳过已声明来源策略；支持合同缺失或重复在启用前失败。</summary>
    public void ValidateTreeSupport(LinkQueryDefinition definition)
    {
        if (definition.Metadata.PolicyKey is { } key && treePolicies.Count(policy => policy.Key == key) != 1)
            throw new InvalidOperationException("LINK_TREE_SOURCE_READ_POLICY_MISSING_OR_DUPLICATE");
    }

    /// <summary>源 read 与无额外请求上下文的 Customizer 使用普通 Link 的同一规则。</summary>
    public async Task<IQueryable<Dictionary<string, object?>>> RowsAsync(LinkQueryDefinition definition,
        Expression<Func<Dictionary<string, object?>, bool>>? predicate, CancellationToken token)
    {
        var rows = db.Set<Dictionary<string, object?>>(definition.Model.LogicalKey).AsNoTracking();
        if (predicate is not null) rows = rows.Where(predicate);
        if (definition.HasDataScopeResource)
        {
            var plan = await reads.GetReadPlanAsync(definition.Metadata.ResourceKey, token);
            if (plan.Predicate is not null) rows = rows.Where(plan.Predicate);
        }
        var resource = resources.FindByResourceKey(definition.Metadata.ResourceKey);
        if (resource is not null)
        {
            var conditions = customizers.SingleOrDefault(customizer => customizer.ResourceKey == resource.ResourceKey)?.BuildConditions(resource, new()) ?? [];
            rows = EfPlatformQueryExecutor.ApplyConditions(rows,
                new PlatformQueryDescriptor { ResourceKey = definition.Metadata.ResourceKey, ModelName = definition.Model.LogicalKey, IdField = "LID", AllowedModelFields = definition.Model.Fields.Select(field => field.Name).ToArray() },
                db.Model.FindEntityType(definition.Model.LogicalKey)!, conditions.Select(condition => new PlatformQueryCondition(condition.Field, condition.Operator, condition.Value, condition.GroupKey, condition.GroupOperator)).ToArray());
        }
        return rows;
    }

    /// <summary>只执行显式树支持的来源条件，不伪造 anchor 或调用位置型 Validate。</summary>
    public async Task<IQueryable<Dictionary<string, object?>>> TreeRowsAsync(LinkQueryDefinition definition, CancellationToken token)
    {
        ValidateTreeSupport(definition);
        var predicate = definition.Metadata.PolicyKey is { } key ? await treePolicies.Single(policy => policy.Key == key).ReadPredicateAsync(definition, token) : null;
        return await RowsAsync(definition, predicate, token);
    }

    /// <summary>把同一源行交集下推为 LID 子查询，不在应用进程读取整图。</summary>
    public async Task<LinkQuerySqlScope> TreeSqlAsync(LinkQueryDefinition definition, string name, CancellationToken token)
    {
        if (name.Length == 0 || name.Any(character => !char.IsAsciiLetterOrDigit(character))) throw new ArgumentException("Invalid trusted source scope.", nameof(name));
        var rows = await TreeRowsAsync(definition, token);
        using var command = rows.Select(row => EF.Property<string>(row, "LID")).CreateDbCommand();
        var parameters = new List<DbParameter>(); var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DbParameter parameter in command.Parameters)
        {
            var parameterName = "@" + name + "Parameter" + parameters.Count;
            names.Add(parameter.ParameterName, parameterName); var copy = (DbParameter)((ICloneable)parameter).Clone(); copy.ParameterName = parameterName; parameters.Add(copy);
        }
        return new("", LinkQuerySqlScope.RenameParameters(command.CommandText.Trim().TrimEnd(';'), names), parameters);
    }
}
