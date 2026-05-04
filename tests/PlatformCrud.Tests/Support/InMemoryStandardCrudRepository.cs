using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Tests.Support;

public class InMemoryStandardCrudRepository : IStandardCrudRepository
{
    private readonly List<Dictionary<string, object?>> _rows;

    public InMemoryStandardCrudRepository(IEnumerable<Dictionary<string, object?>> rows)
    {
        _rows = rows.Select(row => new Dictionary<string, object?>(row, StringComparer.Ordinal)).ToList();
    }

    public Task<StandardCrudListResult> ListAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        IReadOnlyList<StandardCrudQueryCondition> conditions,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Dictionary<string, object?>> filtered = _rows;
        foreach (var condition in conditions)
        {
            filtered = filtered.Where(row =>
                row.TryGetValue(condition.Field, out var value) &&
                value?.ToString()?.Contains(condition.Value?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true);
        }

        var items = filtered.Select(row => Project(row, descriptor.FieldSet.ListFields)).ToList();
        return Task.FromResult(new StandardCrudListResult(
            items,
            items.Count,
            query.Page <= 0 ? 1 : query.Page,
            query.PageSize <= 0 ? 20 : query.PageSize,
            query.SortBy,
            query.SortDirection));
    }

    public Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        var row = _rows.FirstOrDefault(item => string.Equals(item[descriptor.IdField]?.ToString(), id, StringComparison.Ordinal));
        return Task.FromResult<IReadOnlyDictionary<string, object?>?>(row is null ? null : Project(row, descriptor.FieldSet.DetailFields));
    }

    public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StandardCrudOptionItem> options = _rows.Select(row => new StandardCrudOptionItem
        {
            Value = row[descriptor.IdField]?.ToString() ?? string.Empty,
            Label = string.Join(
                " ",
                descriptor.OptionLabelFields
                    .Select(field => row.TryGetValue(field, out var value) ? value?.ToString() : null)
                    .Where(value => !string.IsNullOrWhiteSpace(value)))
        }).ToList();

        return Task.FromResult(options);
    }

    public virtual Task<bool> ExistsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudUniqueConstraint constraint,
        IReadOnlyDictionary<string, object?> equalityValues,
        string? excludedId = null,
        CancellationToken cancellationToken = default)
    {
        var exists = _rows.Any(row =>
            (string.IsNullOrWhiteSpace(excludedId) ||
                !string.Equals(row[descriptor.IdField]?.ToString(), excludedId, StringComparison.Ordinal)) &&
            equalityValues.All(pair =>
                row.TryGetValue(pair.Key, out var value) &&
                UniqueValuesEqual(value, pair.Value, constraint)));

        return Task.FromResult(exists);
    }

    public Task<IReadOnlyDictionary<string, object?>> CreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        var row = new Dictionary<string, object?>(values, StringComparer.Ordinal);
        if (!row.ContainsKey(descriptor.IdField))
        {
            row[descriptor.IdField] = Guid.NewGuid().ToString("D");
        }

        _rows.Add(row);
        return Task.FromResult(Project(row, descriptor.FieldSet.DetailFields));
    }

    public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        var row = _rows.FirstOrDefault(item => string.Equals(item[descriptor.IdField]?.ToString(), id, StringComparison.Ordinal));
        if (row is null)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object?>?>(null);
        }

        foreach (var pair in values)
        {
            row[pair.Key] = pair.Value;
        }

        return Task.FromResult<IReadOnlyDictionary<string, object?>?>(Project(row, descriptor.FieldSet.DetailFields));
    }

    public Task<bool> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_rows.RemoveAll(row => string.Equals(row[descriptor.IdField]?.ToString(), id, StringComparison.Ordinal)) > 0);
    }

    private static IReadOnlyDictionary<string, object?> Project(IReadOnlyDictionary<string, object?> row, IReadOnlyList<string> fields)
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
}
