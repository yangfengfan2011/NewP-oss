using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Crud.Persistence;

public sealed class EfStandardCrudRepository : IStandardCrudRepository
{
    private readonly EfPlatformDbContext _dbContext;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;

    public EfStandardCrudRepository(
        EfPlatformDbContext dbContext,
        IPlatformObjectIdGenerator objectIdGenerator)
    {
        _dbContext = dbContext;
        _objectIdGenerator = objectIdGenerator;
    }

    /// <summary>
    /// 读取标准 CRUD 列表；动态字段的类型差异在代码内核中显式处理，避免把过滤语义下沉到元数据解释层。
    /// </summary>
    public async Task<StandardCrudListResult> ListAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        IReadOnlyList<StandardCrudQueryCondition> conditions,
        CancellationToken cancellationToken = default)
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
        var rows = await _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var source = ApplyConditions(rows, conditions);
        source = ApplySort(source, descriptor, query);

        var total = source.Count();
        var pageRows = source.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new StandardCrudListResult(
            pageRows.Select(row => Project(row, descriptor.FieldSet.ListFields)).ToList(),
            total,
            page,
            pageSize,
            query.SortBy,
            query.SortDirection);
    }

    /// <summary>
    /// 读取详情字段集合；返回值只包含 descriptor 中允许暴露的 detail 字段。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName)
            .AsNoTracking()
            .FirstOrDefaultAsync(item => EF.Property<string>(item, descriptor.IdField) == id, cancellationToken);

        return row is null ? null : Project(row, descriptor.FieldSet.DetailFields);
    }

    /// <summary>
    /// 生成 options 轻量列表；label 只来自 descriptor 显式声明的字段，避免隐式拼接约定扩散。
    /// </summary>
    public async Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName)
            .AsNoTracking()
            .Take(query.PageSize <= 0 ? 50 : query.PageSize)
            .ToListAsync(cancellationToken);

        return rows.Select(row => new StandardCrudOptionItem
        {
            Value = ReadString(row, descriptor.IdField),
            Label = string.Join(
                " ",
                descriptor.OptionLabelFields
                    .Select(field => ReadString(row, field))
                    .Where(value => value.Length > 0))
        }).ToList();
    }

    /// <summary>
    /// 执行标准等值存在性检查；当前实现与列表查询一样先保持 shared-type entity 的显式内核过滤。
    /// </summary>
    public async Task<bool> ExistsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudUniqueConstraint constraint,
        IReadOnlyDictionary<string, object?> equalityValues,
        string? excludedId = null,
        CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.Any(row =>
            (string.IsNullOrWhiteSpace(excludedId) ||
                !string.Equals(ReadString(row, descriptor.IdField), excludedId, StringComparison.Ordinal)) &&
            equalityValues.All(pair => row.TryGetValue(pair.Key, out var value) && UniqueValuesEqual(value, pair.Value, constraint)));
    }

    /// <summary>
    /// 创建 shared-type entity；缺省主键由代码内核生成，后续可由服务层控制字段白名单和默认值。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object?>> CreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        var row = new Dictionary<string, object?>(values, StringComparer.Ordinal);
        if (!row.ContainsKey(descriptor.IdField))
        {
            row[descriptor.IdField] = _objectIdGenerator.NewId(descriptor.ModelName);
        }

        _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName).Add(row);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Project(row, descriptor.FieldSet.DetailFields);
    }

    /// <summary>
    /// 更新 shared-type entity；只复制调用方传入字段，字段是否允许更新由服务层统一裁剪。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName)
            .FirstOrDefaultAsync(item => EF.Property<string>(item, descriptor.IdField) == id, cancellationToken);

        if (row is null)
        {
            return null;
        }

        foreach (var pair in values)
        {
            row[pair.Key] = pair.Value;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Project(row, descriptor.FieldSet.DetailFields);
    }

    /// <summary>
    /// 执行物理删除；是否允许删除必须由上层 delete policy 和 guard 先判定。
    /// </summary>
    public async Task<bool> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName)
            .FirstOrDefaultAsync(item => EF.Property<string>(item, descriptor.IdField) == id, cancellationToken);

        if (row is null)
        {
            return false;
        }

        _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName).Remove(row);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> ApplyConditions(
        IEnumerable<IReadOnlyDictionary<string, object?>> source,
        IReadOnlyList<StandardCrudQueryCondition> conditions)
    {
        var executableConditions = conditions.Where(IsExecutableCondition).ToList();
        foreach (var condition in executableConditions.Where(condition => string.IsNullOrWhiteSpace(condition.GroupKey)))
        {
            source = source.Where(row => MatchesCondition(row, condition));
        }

        foreach (var group in executableConditions
            .Where(condition => !string.IsNullOrWhiteSpace(condition.GroupKey))
            .GroupBy(condition => condition.GroupKey, StringComparer.Ordinal))
        {
            var groupOperators = group
                .Select(condition => string.IsNullOrWhiteSpace(condition.GroupOperator) ? "and" : condition.GroupOperator)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (groupOperators.Count > 1)
            {
                throw new InvalidOperationException($"标准 CRUD 查询条件组 {group.Key} 不能混用组合操作符。");
            }

            var groupOperator = groupOperators[0];
            if (!string.Equals(groupOperator, "or", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(groupOperator, "and", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"标准 CRUD 查询条件组 {group.Key} 不支持组合操作符 {groupOperator}。");
            }

            source = string.Equals(groupOperator, "or", StringComparison.OrdinalIgnoreCase)
                ? source.Where(row => group.Any(condition => MatchesCondition(row, condition)))
                : source.Where(row => group.All(condition => MatchesCondition(row, condition)));
        }

        return source;
    }

    private static bool IsExecutableCondition(StandardCrudQueryCondition condition) =>
        condition.Value is not null &&
        (string.Equals(condition.Operator, "=", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(condition.Operator, "contains", StringComparison.OrdinalIgnoreCase));

    private static bool MatchesCondition(IReadOnlyDictionary<string, object?> row, StandardCrudQueryCondition condition)
    {
        if (!row.TryGetValue(condition.Field, out var value))
        {
            return false;
        }

        if (string.Equals(condition.Operator, "=", StringComparison.OrdinalIgnoreCase))
        {
            return ValuesEqual(value, condition.Value);
        }

        return ReadString(value).Contains(ReadString(condition.Value), StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> ApplySort(
        IEnumerable<IReadOnlyDictionary<string, object?>> source,
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query)
    {
        var sortBy = string.IsNullOrWhiteSpace(query.SortBy) ? descriptor.DisplayField : query.SortBy;
        return string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase)
            ? source.OrderByDescending(row => ReadComparable(row, sortBy))
            : source.OrderBy(row => ReadComparable(row, sortBy));
    }

    private static IReadOnlyDictionary<string, object?> Project(
        IReadOnlyDictionary<string, object?> row,
        IReadOnlyList<string> fields)
    {
        var projected = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            projected[field] = row.TryGetValue(field, out var value) ? value : null;
        }

        return projected;
    }

    private static bool ValuesEqual(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left is bool leftBool && bool.TryParse(ReadString(right), out var rightBool))
        {
            return leftBool == rightBool;
        }

        if (left is DateTime leftDateTime && DateTime.TryParse(ReadString(right), out var rightDateTime))
        {
            return leftDateTime == rightDateTime;
        }

        return string.Equals(ReadString(left), ReadString(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool UniqueValuesEqual(object? left, object? right, StandardCrudUniqueConstraint constraint)
    {
        var normalizedLeft = NormalizeUniqueValue(left, constraint);
        var normalizedRight = NormalizeUniqueValue(right, constraint);
        if (normalizedLeft is null || normalizedRight is null)
        {
            return normalizedLeft is null && normalizedRight is null;
        }

        return normalizedLeft is string leftText && normalizedRight is string rightText
            ? string.Equals(
                leftText,
                rightText,
                constraint.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            : Equals(normalizedLeft, normalizedRight);
    }

    private static object? NormalizeUniqueValue(object? value, StandardCrudUniqueConstraint constraint)
    {
        if (value is not string text)
        {
            return value;
        }

        return constraint.TrimWhitespace ? text.Trim() : text;
    }

    private static string ReadString(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) ? ReadString(value) : string.Empty;

    private static string ReadString(object? value) => value?.ToString() ?? string.Empty;

    private static IComparable ReadComparable(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is IComparable comparable
            ? comparable
            : string.Empty;
}
