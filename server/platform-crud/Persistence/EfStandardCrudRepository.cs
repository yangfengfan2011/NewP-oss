using System.Globalization;
using System.Linq.Expressions;
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
    /// 读取详情字段集合；返回值只包含 descriptor 中允许暴露的 detail 字段。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey(descriptor.ModelName))
            .AsNoTracking()
            .FirstOrDefaultAsync(item => EF.Property<string>(item, descriptor.IdField) == id, cancellationToken);

        return row is null ? null : Project(row, descriptor.FieldSet.DetailFields);
    }

    /// <summary>
    /// 通过分页 lookup 内核生成兼容 options 轻量列表；关键词从标准 query filter 读取。
    /// </summary>
    public async Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default)
    {
        string? keyword = null;
        query.Filters?.TryGetValue("keyword", out keyword);
        var result = await LookupAsync(
            descriptor,
            new StandardCrudLookupRequest(keyword, query.Page, query.PageSize),
            cancellationToken);
        return result.Items;
    }

    /// <summary>
    /// 在数据源执行关键词过滤、稳定排序和分页，并以独立查询补齐候选页外的当前值。
    /// </summary>
    public async Task<StandardCrudLookupResult> LookupAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Clamp(request.Page, 1, int.MaxValue / pageSize);
        var rows = _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey(descriptor.ModelName))
            .AsNoTracking()
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(descriptor.EnabledField))
        {
            rows = rows.Where(row => EF.Property<bool>(row, descriptor.EnabledField) == true);
        }
        var keyword = request.Keyword?.Trim();
        if (!string.IsNullOrEmpty(keyword))
        {
            rows = rows.Where(BuildKeywordPredicate(descriptor.OptionLabelFields, keyword));
        }

        var orderedRows = ApplyOptionOrder(rows, descriptor);
        var pageRows = await orderedRows
            .Skip((page - 1) * pageSize)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = pageRows.Count > pageSize;
        var items = pageRows.Take(pageSize).Select(row => ToOption(descriptor, row)).ToList();

        var currentValueIds = (request.CurrentValueIds ?? [])
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Take(100)
            .ToList();
        var currentItems = currentValueIds.Count == 0
            ? []
            : (await ApplyOptionOrder(
                    _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey(descriptor.ModelName))
                        .AsNoTracking()
                        .Where(row => currentValueIds.Contains(EF.Property<string>(row, descriptor.IdField))),
                    descriptor)
                .ToListAsync(cancellationToken))
                .Select(row => ToOption(descriptor, row))
                .ToList();

        return new StandardCrudLookupResult(items, currentItems, page, pageSize, hasMore);
    }

    // lookup 的名称搜索在所有显式 label 字段间执行 OR，避免模块复制筛选规则。
    private static Expression<Func<Dictionary<string, object?>, bool>> BuildKeywordPredicate(
        IReadOnlyList<string> labelFields,
        string keyword)
    {
        var row = Expression.Parameter(typeof(Dictionary<string, object?>), "row");
        var loweredKeyword = Expression.Constant(keyword.ToLowerInvariant());
        Expression body = Expression.Constant(false);
        foreach (var field in labelFields)
        {
            var value = Expression.Call(
                typeof(EF),
                nameof(EF.Property),
                [typeof(string)],
                row,
                Expression.Constant(field));
            var safeValue = Expression.Coalesce(value, Expression.Constant(string.Empty));
            var loweredValue = Expression.Call(safeValue, nameof(string.ToLower), Type.EmptyTypes);
            var contains = Expression.Call(loweredValue, nameof(string.Contains), Type.EmptyTypes, loweredKeyword);
            body = Expression.OrElse(body, contains);
        }

        return Expression.Lambda<Func<Dictionary<string, object?>, bool>>(body, row);
    }

    // label 字段和主键共同形成确定性顺序，分页期间同名记录不会跨页漂移。
    private static IOrderedQueryable<Dictionary<string, object?>> ApplyOptionOrder(
        IQueryable<Dictionary<string, object?>> rows,
        StandardCrudDescriptor descriptor)
    {
        IOrderedQueryable<Dictionary<string, object?>>? ordered = null;
        foreach (var field in descriptor.OptionLabelFields.Append(descriptor.IdField).Distinct(StringComparer.Ordinal))
        {
            ordered = ordered is null
                ? rows.OrderBy(row => EF.Property<string>(row, field))
                : ordered.ThenBy(row => EF.Property<string>(row, field));
        }

        return ordered ?? rows.OrderBy(row => EF.Property<string>(row, descriptor.IdField));
    }

    // 选项投影只消费 descriptor 显式声明的主键和 label 字段。
    private static StandardCrudOptionItem ToOption(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> row)
    {
        var disabled = !string.IsNullOrWhiteSpace(descriptor.EnabledField)
            && row.TryGetValue(descriptor.EnabledField, out var enabledValue)
            && enabledValue is bool enabled
            && !enabled;
        var label = string.Join(
            " ",
            descriptor.OptionLabelFields
                .Select(field => ReadString(row, field))
                .Where(value => value.Length > 0));

        return new StandardCrudOptionItem
        {
            Value = ReadString(row, descriptor.IdField),
            Label = disabled ? $"{label}（已停用）" : label,
            Disabled = disabled
        };
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
        var rows = await _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey(descriptor.ModelName))
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

        _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey(descriptor.ModelName)).Add(row);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // shared-type 字典只包含调用方显式写入的键；由模型默认值初始化的受管字段
        // （例如 Revision）不会自动回填到原字典。创建后从正式持久化视图重读，
        // 使响应与后续详情读取保持同一份字段事实。
        var createdId = ReadString(row, descriptor.IdField);
        var created = await _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey(descriptor.ModelName))
            .AsNoTracking()
            .SingleAsync(item => EF.Property<string>(item, descriptor.IdField) == createdId, cancellationToken);
        return Project(created, descriptor.FieldSet.DetailFields);
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
        var modelKey = _dbContext.ResolveModelKey(descriptor.ModelName);
        var row = await _dbContext.Set<Dictionary<string, object?>>(modelKey)
            .FirstOrDefaultAsync(item => EF.Property<string>(item, descriptor.IdField) == id, cancellationToken);

        if (row is null)
        {
            return null;
        }

        foreach (var pair in values)
        {
            row[pair.Key] = NormalizeMappedValue(modelKey, pair.Key, pair.Value);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Project(row, descriptor.FieldSet.DetailFields);
    }

    /// <summary>
    /// 以 descriptor 声明的 long revision 执行原子 claim，并在同一事务内完成业务字段更新与版本递增。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var revisionField = descriptor.ConcurrencyRevisionField;
        if (string.IsNullOrWhiteSpace(revisionField))
        {
            return null;
        }

        var modelKey = _dbContext.ResolveModelKey(descriptor.ModelName);
        var rows = _dbContext.Set<Dictionary<string, object?>>(modelKey);
        if (!_dbContext.Database.IsRelational())
        {
            var inMemoryRow = await rows.FirstOrDefaultAsync(item =>
                EF.Property<string>(item, descriptor.IdField) == id
                && EF.Property<long>(item, revisionField) == expectedRevision,
                cancellationToken);
            if (inMemoryRow is null)
            {
                return null;
            }

            ApplyUpdateValues(modelKey, inMemoryRow, values, revisionField);
            inMemoryRow[revisionField] = expectedRevision + 1;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Project(inMemoryRow, descriptor.FieldSet.DetailFields);
        }

        await using var ownedTransaction = _dbContext.Database.CurrentTransaction is null
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var affected = await rows
            .Where(item =>
                EF.Property<string>(item, descriptor.IdField) == id
                && EF.Property<long>(item, revisionField) == expectedRevision)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    item => EF.Property<long>(item, revisionField),
                    item => EF.Property<long>(item, revisionField) + 1),
                cancellationToken);
        if (affected != 1)
        {
            return null;
        }

        var row = await rows.FirstAsync(
            item => EF.Property<string>(item, descriptor.IdField) == id,
            cancellationToken);
        ApplyUpdateValues(modelKey, row, values, revisionField);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (ownedTransaction is not null)
        {
            await ownedTransaction.CommitAsync(cancellationToken);
        }

        return Project(row, descriptor.FieldSet.DetailFields);
    }

    // 条件 claim 已拥有 revision；后续只复制业务字段，避免客户端把版本写回旧值。
    private void ApplyUpdateValues(
        string modelKey,
        IDictionary<string, object?> row,
        IReadOnlyDictionary<string, object?> values,
        string revisionField)
    {
        foreach (var pair in values)
        {
            if (!string.Equals(pair.Key, revisionField, StringComparison.Ordinal))
            {
                row[pair.Key] = NormalizeMappedValue(modelKey, pair.Key, pair.Value);
            }
        }
    }

    // JSON 没有日期标量；在 EF 映射边界恢复 DateTime，避免 shared-type 字典携带字符串进入 ChangeTracker。
    private object? NormalizeMappedValue(string modelKey, string fieldName, object? value)
    {
        if (value is not string text)
        {
            return value;
        }

        var property = _dbContext.Model.FindEntityType(modelKey)?.FindProperty(fieldName);
        var mappedType = property is null
            ? null
            : Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        if (mappedType != typeof(DateTime))
        {
            return value;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : value;
    }

    /// <summary>
    /// 执行物理删除；是否允许删除必须由上层 delete policy 和 guard 先判定。
    /// </summary>
    public async Task<bool> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey(descriptor.ModelName))
            .FirstOrDefaultAsync(item => EF.Property<string>(item, descriptor.IdField) == id, cancellationToken);

        if (row is null)
        {
            return false;
        }

        _dbContext.Set<Dictionary<string, object?>>(_dbContext.ResolveModelKey(descriptor.ModelName)).Remove(row);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
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
}
