using System.Globalization;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Query.Runtime;

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
        AddBirthDateRange(filters, conditions);
        return conditions;
    }

    // Person 只拥有显式边界键及反向范围校验；边界类型转换和数据库比较由查询内核统一执行。
    private static void AddBirthDateRange(
        IReadOnlyDictionary<string, string?> filters,
        ICollection<StandardCrudQueryCondition> conditions)
    {
        var from = ReadCalendarDateBoundary(filters, "birthDateFrom");
        var to = ReadCalendarDateBoundary(filters, "birthDateTo");
        if (from.Parsed is not null && to.Parsed is not null && from.Parsed > to.Parsed)
        {
            throw new PlatformQueryValidationException(
                "PlatformQuery.InvalidConditionRange",
                "人员出生日期范围的起始日期不得晚于结束日期。");
        }

        if (from.Value is not null)
        {
            conditions.Add(new StandardCrudQueryCondition("BirthDate", "gte", from.Value));
        }
        if (to.Value is not null)
        {
            conditions.Add(new StandardCrudQueryCondition("BirthDate", "lte", to.Value));
        }
    }

    // 显式 CalendarDate 边界必须使用严格 ISO 日期；空参数等同于未提交该边界。
    private static (string? Value, DateOnly? Parsed) ReadCalendarDateBoundary(
        IReadOnlyDictionary<string, string?> filters,
        string key)
    {
        if (!filters.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var normalized = value.Trim();
        if (!DateOnly.TryParseExact(
            normalized,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            throw new PlatformQueryValidationException(
                "PlatformQuery.InvalidConditionValue",
                $"人员出生日期边界 {key} 必须是 YYYY-MM-DD 日历日期。");
        }

        return (normalized, parsed);
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
