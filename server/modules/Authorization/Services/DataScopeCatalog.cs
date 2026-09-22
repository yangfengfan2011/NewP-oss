using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ICP.Platform.Modeling;
using ICP.Platform.Presentation;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>规范资源映射来自全部显式描述符，动作来自正式候选；不按模块名硬编码资源。</summary>
public sealed class DataScopeCatalog(IStandardCrudResourceRegistry resources, CanonicalModulePlan plan,
    EfPlatformDbContext db, DataScopeIdentities identities, ITemporalInputNormalizer? temporalInputNormalizer = null)
{
    public IReadOnlyList<DataActionDeclaration> Actions => plan.DataActionCatalog;
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
        _ = ParseValue(rule.ResourceKey, rule.Condition);
        foreach (var subject in rule.Subjects)
        {
            if (string.IsNullOrWhiteSpace(subject.Id) || subject.Id.Length > 128
                || subject.Actions is null || subject.Actions.Any(action => action.Key != "read"
                    || !Actions.Any(defined => defined.Key == action.Key) || action.Value is not ("allow" or "deny" or "unset")))
                throw new RolePermissionValidationException("主体授权项身份或读取三态无效。");
            identities.Validate(subject.Type, subject.SubjectId, subject.MatchOptions);
        }
    }

    /// <summary>保存时规范化默认匹配选项和日期；固定 Instant 不随后续查看者重新解释。</summary>
    public DataScopeRuleDto NormalizeForSave(DataScopeRuleDto rule)
    {
        rule = rule with { Subjects = rule.Subjects.Select(subject => subject.MatchOptions?.IncludeDescendants == false
            ? subject with { MatchOptions = null } : subject).ToArray() };
        if (rule.Condition.ContextKey is not null) return rule;
        var resource = Resolve(rule.ResourceKey);
        var field = db.PersistencePlan.Models.Single(model => model.LogicalKey == resource.ModelKey)
            .Fields.Single(field => field.Name == rule.Condition.Field);
        if (field.ValueType is not (FieldValueType.Instant or FieldValueType.CalendarDate)) return rule;
        if (temporalInputNormalizer is null) throw new InvalidOperationException("DATA_SCOPE_TEMPORAL_NORMALIZER_REQUIRED");
        var normalized = temporalInputNormalizer.Normalize(field.ValueType, rule.Condition.Value);
        if (!normalized.IsSuccess) throw new RolePermissionValidationException(normalized.Message ?? "日期比较值无效。");
        return rule with { Condition = rule.Condition with { Value = JsonSerializer.SerializeToElement(normalized.Value) } };
    }

    /// <summary>按真实模型 CLR 类型解析规范比较值，不应用当前查看者的时区。</summary>
    public (IProperty Property, object Value) ParseValue(string resourceKey, DataScopeConditionDto condition)
    {
        if (condition is null || condition.Type != "attribute" || condition.Operator != "equal"
            || condition.ContextKey is null && condition.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new RolePermissionValidationException("本阶段只支持直接属性的非空固定值或当前身份值等值条件。");
        var property = Fields(resourceKey).SingleOrDefault(field => field.Name == condition.Field)
            ?? throw new RolePermissionValidationException("条件属性不存在。");
        if (condition.ContextKey is not null)
        {
            var context = identities.ContextValues.SingleOrDefault(value => value.Key == condition.ContextKey)
                ?? throw new RolePermissionValidationException("当前值未登记。");
            if (context.ValueType != (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType).Name)
                throw new RolePermissionValidationException("当前值与属性类型不相容。");
            if (!identities.ContextBindings.Any(binding => binding.ModelKey == Resolve(resourceKey).ModelKey
                && binding.Field == property.Name && binding.ContextKey == context.Key))
                throw new RolePermissionValidationException("当前值与属性的规范身份域不相容。");
            if (condition.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                throw new RolePermissionValidationException("当前值条件不能同时包含固定值。");
            return (property, condition.ContextKey);
        }
        try
        {
            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            var value = condition.Value.Deserialize(type) ?? throw new JsonException();
            return (property, value);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or FormatException)
        { throw new RolePermissionValidationException("比较值与属性类型不相容。"); }
    }
}
