using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class DepartmentCrudQueryCustomizer : IStandardCrudQueryCustomizer
{
    public string ResourceKey => "organization.department";

    /// <summary>
    /// 把部门关键词、公司、上级与启用状态转换为平台查询条件，筛选、排序和分页继续由数据库执行器下推。
    /// </summary>
    public IReadOnlyList<StandardCrudQueryCondition> BuildConditions(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query)
    {
        _ = descriptor;
        var conditions = new List<StandardCrudQueryCondition>();
        if (query.Filters?.GetValueOrDefault("keyword") is { Length: > 0 } keyword)
        {
            conditions.Add(new StandardCrudQueryCondition("ECODE", "contains", keyword, "keyword", "or"));
            conditions.Add(new StandardCrudQueryCondition("ENAME", "contains", keyword, "keyword", "or"));
        }

        if (query.Filters?.GetValueOrDefault("companyId") is { Length: > 0 } companyId)
        {
            conditions.Add(new StandardCrudQueryCondition("CompanyId", "=", companyId));
        }

        if (query.Filters?.GetValueOrDefault("parentDepartmentId") is { Length: > 0 } parentDepartmentId)
        {
            conditions.Add(new StandardCrudQueryCondition("ParentDepartmentId", "=", parentDepartmentId));
        }

        if (query.Filters?.GetValueOrDefault("isEnabled") is { Length: > 0 } isEnabled)
        {
            conditions.Add(new StandardCrudQueryCondition("IsEnabled", "=", isEnabled));
        }

        if (query.QueryScope is { } scope)
        {
            if (!scope.TryGetValue("CompanyId", out var scopedCompany) || string.IsNullOrWhiteSpace(scopedCompany)
                || !scope.ContainsKey("ParentDepartmentId")
                || scope.Keys.Any(key => key is not ("CompanyId" or "ParentDepartmentId")))
            {
                throw new Microsoft.AspNetCore.Http.BadHttpRequestException("直属部门范围需要 CompanyId 和显式 ParentDepartmentId。");
            }
            conditions.Add(new StandardCrudQueryCondition("CompanyId", "=", scopedCompany));
            conditions.Add(new StandardCrudQueryCondition("ParentDepartmentId", scope["ParentDepartmentId"] is null ? "isNull" : "=", scope["ParentDepartmentId"]));
        }
        return conditions;
    }
}
