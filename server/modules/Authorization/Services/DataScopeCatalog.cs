using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ICP.Platform.Presentation;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>规范资源映射来自全部显式描述符，动作来自正式候选；不按模块名硬编码资源。</summary>
public sealed class DataScopeCatalog(IStandardCrudResourceRegistry resources, CanonicalModulePlan plan,
    EfPlatformDbContext db, DataScopeIdentities identities, ITemporalInputNormalizer? temporalInputNormalizer = null,
    IEnumerable<IDataScopeBusinessConditionProvider>? conditionProviders = null,
    IEnumerable<IDataScopeEnumFieldProvider>? enumProviders = null)
{
    private DataScopeConditions Conditions => new(this, db, identities, temporalInputNormalizer, conditionProviders ?? [], enumProviders ?? []);
    public IReadOnlyList<DataActionDeclaration> Actions => plan.DataActionCatalog;
    public IReadOnlyList<DataScopeBusinessConditionDefinition> BusinessConditions
    {
        get { Conditions.ValidateRegistration(); return Conditions.BusinessTypes; }
    }
    public IReadOnlyList<string>? EnumValues(string resourceKey, string field) => Conditions.EnumValues(resourceKey, field);
    public IReadOnlyList<DataScopeResource> Resources => resources.GetAll().Select(resource =>
        new DataScopeResource(resource.ResourceKey, resource.ModuleId, resource.ModelName, resource.IdField)).ToArray();

    /// <summary>定位明确登记的资源；不从模型名或页面猜测身份。</summary>
    public DataScopeResource Resolve(string key) => Resources.SingleOrDefault(resource => resource.ResourceKey == key)
        ?? throw new RolePermissionValidationException("数据资源未登记。");

    /// <summary>返回正式模型的原始规范字段，不包含 Display companion。</summary>
    public IReadOnlyList<IProperty> Fields(string key)
    {
        var resource = Resolve(key);
        if (!resource.ModelKey.StartsWith(resource.ModuleId + "/", StringComparison.Ordinal))
            throw new InvalidOperationException("DATA_SCOPE_MODEL_OWNER_MISMATCH");
        var model = db.Model.FindEntityType(db.ResolveModelKey(resource.ModelKey))
            ?? throw new InvalidOperationException("DATA_SCOPE_MODEL_UNAVAILABLE");
        return model.GetProperties().Where(property => !property.Name.StartsWith("__", StringComparison.Ordinal)).ToArray();
    }

    /// <summary>验证当前单根读取结构与已注册主体、上下文；未知类型、子树和非读取动作拒绝。</summary>
    public void Validate(DataScopeRuleDto rule)
    {
        Resolve(rule.ResourceKey);
        if (string.IsNullOrWhiteSpace(rule.Id) || rule.Id.Length > 128 || rule.RootId != rule.Id || rule.ParentId is not null
            || string.IsNullOrWhiteSpace(rule.Name) || rule.Name.Length > 256 || rule.Children is null || rule.Children.Count != 0
            || rule.Subjects is null || rule.Subjects.Select(subject => subject.Id).Distinct(StringComparer.Ordinal).Count() != rule.Subjects.Count)
            throw new RolePermissionValidationException("本阶段只接受具有稳定身份的单根规则。");
        Conditions.ValidateRegistration();
        Conditions.Validate(rule.ResourceKey, rule.Condition);
        foreach (var subject in rule.Subjects)
        {
            if (string.IsNullOrWhiteSpace(subject.Id) || subject.Id.Length > 128
                || subject.Actions is null || subject.Actions.Any(action => action.Key != "read"
                    || !Actions.Any(defined => defined.Key == action.Key) || action.Value is not ("allow" or "deny" or "unset")))
                throw new RolePermissionValidationException("主体授权项身份或读取三态无效。");
            identities.Validate(subject.Type, subject.SubjectId, subject.MatchOptions);
        }
    }

    /// <summary>保存时规范化默认匹配选项和全部日期叶子；固定 Instant 不随后续查看者重新解释。</summary>
    public DataScopeRuleDto NormalizeForSave(DataScopeRuleDto rule)
    {
        rule = rule with { Subjects = rule.Subjects.Select(subject => subject.MatchOptions?.IncludeDescendants == false
            ? subject with { MatchOptions = null } : subject).ToArray() };
        return rule with { Condition = Conditions.Normalize(rule.ResourceKey, rule.Condition) };
    }

    /// <summary>读取条件只投影有读取贡献的叶子。</summary>
    public System.Linq.Expressions.Expression? BuildReadCondition(string resourceKey, DataScopeConditionDto condition,
        System.Linq.Expressions.ParameterExpression row, DataScopeResolvedIdentities facts) =>
        Conditions.BuildRead(resourceKey, condition, row, facts);

    /// <summary>无读取贡献的整节点不应解析主体或产生读取意见。</summary>
    public bool HasReadContribution(string resourceKey, DataScopeConditionDto condition) => Conditions.HasReadContribution(resourceKey, condition);

    /// <summary>完整判断为后续普通写入提供同一扩展语义；文本属性交数据库比较。</summary>
    public Task<bool> MatchesAsync(string resourceKey, string objectId, DataScopeConditionDto condition,
        DataScopeResolvedIdentities facts, CancellationToken token)
    {
        Conditions.Validate(resourceKey, condition);
        return Conditions.MatchesAsync(resourceKey, objectId, condition, facts, token);
    }

    /// <summary>枚举组合树实际引用的可信当前身份值。</summary>
    public IEnumerable<string> ContextKeys(DataScopeConditionDto condition) => Conditions.ContextKeys(condition);
}
