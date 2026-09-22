using System.Text.Json;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>规范资源映射来自全部显式描述符，动作来自正式候选；不按模块名硬编码资源。</summary>
public sealed class DataScopeCatalog(IStandardCrudResourceRegistry resources, CanonicalModulePlan plan,
    EfPlatformDbContext db)
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

    /// <summary>完整验证首片的可保存结构；未知类型、子树或非读取动作明确拒绝。</summary>
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
            if (string.IsNullOrWhiteSpace(subject.Id) || subject.Id.Length > 128 || subject.Type != "all" || subject.SubjectId is not null
                || subject.Actions is null || subject.Actions.Any(action => action.Key != "read"
                    || !Actions.Any(defined => defined.Key == action.Key) || action.Value is not ("allow" or "deny" or "unset")))
                throw new RolePermissionValidationException("本阶段仅接受所有用户及读取三态授权项。");
        }
    }

    /// <summary>比较值按真实模型 CLR 类型解析；空值不能冒充属性等值条件。</summary>
    public (IProperty Property, object Value) ParseValue(string resourceKey, DataScopeConditionDto condition)
    {
        if (condition is null || condition.Type != "attribute" || condition.Operator != "equal"
            || condition.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new RolePermissionValidationException("本阶段只支持直接属性的非空固定值等值条件。");
        var property = Fields(resourceKey).SingleOrDefault(field => field.Name == condition.Field)
            ?? throw new RolePermissionValidationException("条件属性不存在。");
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
