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

    /// <summary>验证完整根树的身份、层级和已注册主体、条件。</summary>
    public void Validate(DataScopeRuleDto rule)
    {
        Resolve(rule.ResourceKey);
        Conditions.ValidateRegistration();
        var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var subjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ValidateNode(rule, null);

        // 输入树的嵌套位置决定真实父身份，不能相信节点自报的 ParentId。
        void ValidateNode(DataScopeRuleDto node, string? parentId)
        {
            if (string.IsNullOrWhiteSpace(node.Id) || node.Id.Length > 128 || !nodeIds.Add(node.Id)
                || node.RootId != rule.Id || node.ParentId != parentId || node.ResourceKey != rule.ResourceKey
                || string.IsNullOrWhiteSpace(node.Name) || node.Name.Length > 256
                || node.Children is null || node.Subjects is null)
                throw new RolePermissionValidationException("规则树身份、父子关系或资源无效。");
            try { Conditions.Validate(node.ResourceKey, node.Condition); }
            catch (RolePermissionValidationException exception) when (parentId is not null && exception.Message.StartsWith("条件 ", StringComparison.Ordinal))
            { throw new RolePermissionValidationException($"节点 {node.Id}：{exception.Message}"); }
            foreach (var subject in node.Subjects)
            {
                if (string.IsNullOrWhiteSpace(subject.Id) || subject.Id.Length > 128 || !subjectIds.Add(subject.Id)
                    || subject.Actions is null || subject.Actions.Any(action =>
                        !Actions.Any(defined => defined.Key == action.Key) || action.Value is not ("allow" or "deny" or "unset")))
                    throw new RolePermissionValidationException("主体授权项身份或读取三态无效。");
                identities.Validate(subject.Type, subject.SubjectId, subject.MatchOptions);
            }
            foreach (var child in node.Children) ValidateNode(child, node.Id);
        }
    }

    /// <summary>保存时规范化默认匹配选项和全部日期叶子；固定 Instant 不随后续查看者重新解释。</summary>
    public DataScopeRuleDto NormalizeForSave(DataScopeRuleDto rule)
    {
        return NormalizeNode(rule);

        // 日期和主体选项逐节点规范化，保存审计只记录最终配置。
        DataScopeRuleDto NormalizeNode(DataScopeRuleDto node) => node with
        {
            Subjects = node.Subjects.Select(subject => subject.MatchOptions?.IncludeDescendants == false
                ? subject with { MatchOptions = null } : subject).ToArray(),
            Condition = Conditions.Normalize(node.ResourceKey, node.Condition),
            Children = node.Children.Select(NormalizeNode).ToArray()
        };
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
