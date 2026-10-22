using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>角色只读字段复用 Entity 显示计划；原 Controller 继续持有全部管理准入与写入协议。</summary>
public sealed class RoleReadResource : IStandardCrudQueryCustomizer
{
    public string ResourceKey => "authorization.role";

    /// <summary>只装配服务读取，不开放通用 CRUD HTTP 端点。</summary>
    public static StandardCrudDescriptor Create()
    {
        string[] fields = ["EID", "ECODE", "ENAME", "Description", "IsEnabled", "IsBuiltIn",
            "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"];
        return new StandardCrudDescriptor
        {
            DataExecutions = new Dictionary<string, string> { ["list"] = "authorization.role.list", ["read"] = "authorization.role.read" },
            ResourceKey = "authorization.role", ModuleId = "Authorization", ModelName = "Role",
            Route = "api/authorization/roles", MapStandardEndpoints = false, IdField = "EID", DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(fields, fields, [], []),
            SortableFields = ["ECODE"], DefaultSortField = "ECODE", DefaultSortDirection = "asc"
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<StandardCrudQueryCondition> BuildConditions(StandardCrudDescriptor descriptor, StandardCrudListQuery query)
    {
        var keyword = query.Filters?.GetValueOrDefault("query");
        return string.IsNullOrWhiteSpace(keyword) ? [] :
            [new("ECODE", "contains", keyword, "keyword", "or"), new("ENAME", "contains", keyword, "keyword", "or")];
    }
}
