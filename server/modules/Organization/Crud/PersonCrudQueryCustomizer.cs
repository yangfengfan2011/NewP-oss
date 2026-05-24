using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonCrudQueryCustomizer : IStandardCrudQueryCustomizer
{
    public string ResourceKey => "organization.person";

    /// <summary>
    /// 将人员列表的业务查询参数转换为内核可执行条件；组合和分页仍由 PlatformCrud 统一处理。
    /// </summary>
    public IReadOnlyList<StandardCrudQueryCondition> BuildConditions(StandardCrudDescriptor descriptor, StandardCrudListQuery query)
    {
        var conditions = new List<StandardCrudQueryCondition>();
        var filters = query.Filters ?? new Dictionary<string, string?>();

        if (filters.TryGetValue("keyword", out var keyword) && !string.IsNullOrWhiteSpace(keyword))
        {
            conditions.Add(new StandardCrudQueryCondition("PersonNo", "contains", keyword, GroupKey: "keyword", GroupOperator: "or"));
            conditions.Add(new StandardCrudQueryCondition("ENAME", "contains", keyword, GroupKey: "keyword", GroupOperator: "or"));
        }

        AddEquals(filters, conditions, "companyId", "CompanyId");
        AddEquals(filters, conditions, "departmentId", "DepartmentId");
        AddEquals(filters, conditions, "positionId", "PositionId");
        AddEquals(filters, conditions, "employmentStatus", "EmploymentStatus");
        AddEquals(filters, conditions, "isEnabled", "IsEnabled");
        return conditions;
    }

    // 只把显式传入且非空的筛选项交给内核，避免空值在 shared-type 查询里形成隐式覆盖。
    private static void AddEquals(
        IReadOnlyDictionary<string, string?> filters,
        ICollection<StandardCrudQueryCondition> conditions,
        string filterKey,
        string field)
    {
        if (filters.TryGetValue(filterKey, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            conditions.Add(new StandardCrudQueryCondition(field, "=", value));
        }
    }
}
