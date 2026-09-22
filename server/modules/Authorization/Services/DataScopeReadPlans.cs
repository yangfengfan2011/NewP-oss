using System.Linq.Expressions;
using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>首片裁决所有用户的读取意见，并把最终拒绝范围变为数据库谓词。</summary>
public sealed class DataScopeReadPlans(DataScopeRules rules, DataScopeCatalog catalog,
    IHttpContextAccessor context, ILocalAdministratorRoleAssignment administrators) : IDataScopeReadPlanProvider
{
    /// <inheritdoc/>
    public async Task<DataScopeReadPlan> GetReadPlanAsync(string resourceKey, CancellationToken cancellationToken = default)
    {
        catalog.Resolve(resourceKey);
        var action = catalog.Actions.Single(action => action.Key == "read");
        if (!action.Enabled) return new(null);
        var id = context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("DATA_SCOPE_TRUSTED_SUBJECT_REQUIRED");
        if (await administrators.InspectAsync(id, cancellationToken) == LocalAdministratorAssignmentState.Assigned) return new(null);
        var applicable = await rules.ForResourceAsync(resourceKey, cancellationToken);
        var row = Expression.Parameter(typeof(Dictionary<string, object?>), "row");
        Expression? permitted = null;
        foreach (var rule in applicable.Where(rule => rule.Enabled))
        {
            catalog.Validate(rule);
            var opinions = rule.Subjects.Where(subject => subject.Actions.TryGetValue("read", out var opinion) && opinion != "unset").ToArray();
            if (opinions.Length == 0) continue;
            var priority = opinions.Max(subject => subject.Priority);
            if (!opinions.Any(subject => subject.Priority == priority && subject.Actions["read"] == "deny")) continue;
            var (property, value) = catalog.ParseValue(resourceKey, rule.Condition);
            var access = Expression.Call(typeof(EF), nameof(EF.Property), [property.ClrType], row, Expression.Constant(property.Name));
            var constant = Expression.Convert(Expression.Constant(value), property.ClrType);
            var match = Expression.Equal(access, constant);
            Expression allow = Expression.Not(match);
            // null 不命中等值拒绝；显式补齐可空字段使 SQL 三值逻辑与完整裁决一致。
            if (property.IsNullable) allow = Expression.OrElse(Expression.Equal(access, Expression.Constant(null, property.ClrType)), allow);
            permitted = permitted is null ? allow : Expression.AndAlso(permitted, allow);
        }
        return new(permitted is null ? null : Expression.Lambda<Func<Dictionary<string, object?>, bool>>(permitted, row));
    }
}
