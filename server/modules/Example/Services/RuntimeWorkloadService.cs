using System.Globalization;
using ICP.Platform.Modules.Example.DTOs;

namespace ICP.Platform.Modules.Example.Services;

public sealed class RuntimeWorkloadService : IRuntimeWorkloadService
{
    private const string ContextTokenPrefix = "runtime-workload:";

    private static readonly (string Id, string Name)[] Members =
    [
        ("member-1", "陈示例"),
        ("member-2", "林示例"),
        ("member-3", "周示例")
    ];
    private readonly TimeProvider _timeProvider;

    // 注入平台时间源，使动态日期列在生产与测试中使用同一计算入口。
    public RuntimeWorkloadService(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public RuntimeWorkloadColumnSetDto GetColumns()
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var columns = new List<RuntimeWorkloadColumnDto>
        {
            new("member", "memberName", "成员", "text", true, false)
        };

        for (var dayIndex = 0; dayIndex < 14; dayIndex++)
        {
            var inCurrentWeek = dayIndex < 7;
            var semanticDay = dayIndex % 7 + 1;
            var prefix = inCurrentWeek ? "current-week" : "next-week";
            var fieldPrefix = inCurrentWeek ? "currentWeekDay" : "nextWeekDay";
            var date = monday.AddDays(dayIndex);
            columns.Add(new RuntimeWorkloadColumnDto(
                $"{prefix}.day-{semanticDay}",
                $"{fieldPrefix}{semanticDay}",
                date.ToString("M月d日", CultureInfo.InvariantCulture),
                "number",
                true,
                true));
        }

        return new RuntimeWorkloadColumnSetDto(columns, $"{ContextTokenPrefix}{monday:yyyy-MM-dd}");
    }

    /// <inheritdoc/>
    public RuntimeWorkloadListResultDto GetRows(
        string contextToken,
        string? keyword,
        int pageIndex,
        int pageSize,
        string? sortBy = null,
        string? sortDirection = null)
    {
        var monday = ParseContextToken(contextToken);
        var normalizedPage = Math.Max(1, pageIndex);
        var normalizedPageSize = pageSize is 20 or 50 or 100 ? pageSize : 20;
        var rows = Members
            .Select((member, memberIndex) => BuildRow(member.Id, member.Name, memberIndex, monday))
            .Where(row => string.IsNullOrWhiteSpace(keyword)
                || Convert.ToString(row["memberName"], CultureInfo.InvariantCulture)!
                    .Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        rows = SortRows(rows, sortBy, sortDirection);
        var items = rows
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToArray();
        return new RuntimeWorkloadListResultDto(items, rows.Length, normalizedPage, normalizedPageSize);
    }

    // token 只绑定同一业务周期，不携带权限或可由 Shell 解释的业务字段。
    private static DateOnly ParseContextToken(string contextToken)
    {
        if (string.IsNullOrWhiteSpace(contextToken)
            || !contextToken.StartsWith(ContextTokenPrefix, StringComparison.Ordinal)
            || !DateOnly.TryParseExact(
                contextToken[ContextTokenPrefix.Length..],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var monday))
        {
            throw new ArgumentException("Invalid runtime workload column context token.", nameof(contextToken));
        }

        return monday;
    }

    // 固定成员和十四天上界使示例计算保持确定且无需数据库。
    private static IReadOnlyDictionary<string, object?> BuildRow(
        string id,
        string memberName,
        int memberIndex,
        DateOnly monday)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["memberName"] = memberName,
            ["periodStart"] = monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
        for (var dayIndex = 0; dayIndex < 14; dayIndex++)
        {
            var fieldPrefix = dayIndex < 7 ? "currentWeekDay" : "nextWeekDay";
            row[$"{fieldPrefix}{dayIndex % 7 + 1}"] = (memberIndex + 1 + dayIndex) % 9;
        }

        return row;
    }

    // 运行时字段只在当前已计算行内排序，不引入第二套读取或动态执行入口。
    private static IReadOnlyDictionary<string, object?>[] SortRows(
        IReadOnlyDictionary<string, object?>[] rows,
        string? sortBy,
        string? sortDirection)
    {
        if (string.IsNullOrWhiteSpace(sortBy) || rows.Length == 0 || !rows[0].ContainsKey(sortBy))
        {
            return rows;
        }

        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return (descending
                ? rows.OrderByDescending(row => row[sortBy]?.ToString(), StringComparer.Ordinal)
                : rows.OrderBy(row => row[sortBy]?.ToString(), StringComparer.Ordinal))
            .ToArray();
    }
}
