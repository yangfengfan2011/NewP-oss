using System.Linq.Expressions;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modeling;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>直接项的相对主体只关联目标可信字段；规则主体目录保持原契约。</summary>
internal sealed class DataScopeDirectSubjects(DataScopeCatalog catalog, EfPlatformDbContext db, DataScopeIdentities identities)
{
    private static readonly (DataScopeSubjectType Type, string Field, string IdentityType)[] Definitions =
    [
        (new("creator", "创建人", false), "CreatedBy", "user"),
        (new("owner", "所属人", false), "OwnerId", "user"),
        (new("ownerDepartment", "所属部门", false, true), "OwnerDepartmentId", "department")
    ];

    // 采用正式投影标记，不把名称恰好相同的普通字符串字段当作可信身份。
    private bool Available(string resourceKey, string field)
    {
        var resource = catalog.Resolve(resourceKey);
        var model = db.PersistencePlan.Models.Single(item => item.LogicalKey == resource.ModelKey);
        return model.Kind == ModelKind.Entity && model.Fields.Any(item => item.Name == field
            && (field == "CreatedBy" ? item.IsAuditField : item.IsOwnershipField));
    }

    // 仅对目标实际采用的可信字段公布相对主体。
    internal IReadOnlyList<DataScopeSubjectType> Types(string resourceKey) => identities.SubjectTypes
        .Concat(Definitions.Where(item => Available(resourceKey, item.Field)).Select(item => item.Type)).ToArray();

    // 相对主体没有客户端身份；其他类型仍由原目录校验。
    internal void Validate(string resourceKey, string type, string? id, DataScopeMatchOptions? options)
    {
        var definition = Definitions.SingleOrDefault(item => item.Type.Key == type);
        if (definition.Type is null) { identities.Validate(type, id, options); return; }
        if (!Available(resourceKey, definition.Field))
            throw new RolePermissionValidationException("目标不支持此相对主体。", "DATA_SCOPE_SUBJECT_TYPE_UNKNOWN", field: "type");
        if (id is not null)
            throw new RolePermissionValidationException("相对主体不能指定身份。", "DATA_SCOPE_SUBJECT_ID_INVALID", field: "subjectId");
        if (options?.IncludeDescendants == true && !definition.Type.SupportsDescendants)
            throw new RolePermissionValidationException("该主体类型不支持包含下级。", "DATA_SCOPE_DESCENDANTS_UNSUPPORTED", field: "matchOptions");
    }

    // 只识别直接项内建的三个相对选择器。
    internal static bool IsDynamic(string type) => Definitions.Any(item => item.Type.Key == type);

    // 预先把相对选择器映射为现有事实源，不把新类型注册进规则。
    internal IEnumerable<string> IdentityTypes(string resourceKey, IEnumerable<string> types)
    {
        foreach (var type in types)
        {
            ValidateDynamic(type);
            yield return Definitions.SingleOrDefault(item => item.Type.Key == type).IdentityType ?? type;
        }
        // 模型去掉字段后不能把失效配置默认为不匹配。
        void ValidateDynamic(string type)
        {
            if (Definitions.Any(item => item.Type.Key == type)) Validate(resourceKey, type, null, null);
        }
    }

    // SQL 与单对象意见共用相关 EXISTS；不物化对象全集，不为每行访问身份目录。
    internal Expression<Func<Dictionary<string, object?>, bool>> Membership(string resourceKey,
        DataScopeResolvedIdentities facts, IReadOnlyList<string> types)
    {
        var subject = Expression.Parameter(typeof(Dictionary<string, object?>), "subject");
        Expression expression = Expression.Constant(false);
        foreach (var type in types)
        {
            var definition = Definitions.SingleOrDefault(item => item.Type.Key == type);
            if (definition.Type is null)
            {
                foreach (var membership in facts.Memberships(type))
                {
                    var match = Expression.AndAlso(Equal(subject, "SubjectType", membership.Type), Equal(subject, "SubjectId", membership.Id));
                    if (membership.RequiresDescendants) match = Expression.AndAlso(match, Property<bool>(subject, "IncludeDescendants"));
                    expression = Expression.OrElse(expression, match);
                }
                continue;
            }
            Validate(resourceKey, type, null, null);
            var resource = catalog.Resolve(resourceKey);
            var target = Expression.Parameter(typeof(Dictionary<string, object?>), "target");
            Expression targetMatch = Expression.Constant(false);
            foreach (var membership in facts.Memberships(definition.IdentityType).Where(item => item.Id is not null))
            {
                // 目标引用是规范身份，显式二进制比较保持与事实 owner 的序数匹配一致。
                var value = Expression.Call(typeof(RelationalDbFunctionsExtensions), nameof(RelationalDbFunctionsExtensions.Collate), [typeof(string)],
                    Expression.Property(null, typeof(EF), nameof(EF.Functions)), Property<string>(target, definition.Field), Expression.Constant("Latin1_General_100_BIN2"));
                Expression match = Expression.Equal(value, Expression.Constant(membership.Id));
                if (membership.RequiresDescendants) match = Expression.AndAlso(match, Property<bool>(subject, "IncludeDescendants"));
                targetMatch = Expression.OrElse(targetMatch, match);
            }
            targetMatch = Expression.AndAlso(EqualProperties(target, resource.IdField, subject, "ObjectId"), targetMatch);
            var targets = db.Set<Dictionary<string, object?>>(db.ResolveModelKey(resource.ModelKey)).AsNoTracking();
            var exists = Expression.Call(typeof(Queryable), nameof(Queryable.Any), [typeof(Dictionary<string, object?>)], targets.Expression,
                Expression.Quote(Expression.Lambda<Func<Dictionary<string, object?>, bool>>(targetMatch, target)));
            expression = Expression.OrElse(expression, Expression.AndAlso(Equal(subject, "SubjectType", type), exists));
        }
        return Expression.Lambda<Func<Dictionary<string, object?>, bool>>(expression, subject);
    }

    // 保留模型属性引用供 EF 翻译。
    private static Expression Property<T>(Expression row, string name) => Expression.Call(typeof(EF), nameof(EF.Property), [typeof(T)], row, Expression.Constant(name));
    // 普通主体关系沿原字段比较语义。
    private static Expression Equal(Expression row, string name, string? value) => Expression.Equal(Property<string>(row, name), Expression.Constant(value, typeof(string)));
    // 关联真实目标主键与直接项的规范对象身份。
    private static Expression EqualProperties(Expression left, string leftName, Expression right, string rightName) => Expression.Equal(Property<string>(left, leftName), Property<string>(right, rightName));
}
