using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Modules.Identity.Application;

/// <summary>账号普通读取使用共享 CRUD；公开协议与 AdminOnly 授权仍由 Identity Controller 持有。</summary>
public sealed class IdentityAccountCrudResource : IStandardCrudQueryCustomizer
{
    public string ResourceKey => "identity.account";

    /// <summary>声明现有账号协议已经暴露的字段，安全命令不注册为通用写入。</summary>
    public static StandardCrudDescriptor Create()
    {
        string[] fields = ["EID", "ECODE", "ENAME", "Status", "Revision", "IsLocalAdministrator", "IsPersonnelBound",
            "PersonnelDisableHold", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"];
        return new StandardCrudDescriptor
        {
            ResourceKey = "identity.account", ModuleId = "Identity", ModelName = "IdentityAccount",
            Route = "api/identity/accounts", MapStandardEndpoints = false, IdField = "EID", DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(fields, fields, [], []),
            SortableFields = ["ECODE", "ENAME", "Status"], DefaultSortField = "ECODE", DefaultSortDirection = "asc"
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<StandardCrudQueryCondition> BuildConditions(StandardCrudDescriptor descriptor, StandardCrudListQuery query)
    {
        var filters = query.Filters;
        var conditions = new List<StandardCrudQueryCondition>();
        if (filters?.GetValueOrDefault("status") is { Length: > 0 } status)
        {
            conditions.Add(new("Status", "=", status));
        }
        else
        {
            conditions.Add(new("Status", "=", "Enabled", "status", "or"));
            conditions.Add(new("Status", "=", "Disabled", "status", "or"));
        }
        if (filters?.GetValueOrDefault("query") is { Length: > 0 } keyword)
        {
            conditions.Add(new("ECODE", "contains", keyword, "keyword", "or"));
            conditions.Add(new("ENAME", "contains", keyword, "keyword", "or"));
        }
        foreach (var field in new[] { "IsPersonnelBound", "IsLocalAdministrator" })
        {
            if (bool.TryParse(filters?.GetValueOrDefault(field), out var flag))
            {
                conditions.Add(new(field, "=", flag));
            }
        }
        return conditions;
    }
}
