using ICP.Platform.Modules.ConfigCenter.Models;

namespace ICP.Platform.Modules.ConfigCenter.Repositories;

public class ValueListRepository : IValueListRepository
{
    private readonly List<ValueListItem> _items = new();
    private readonly object _lock = new();

    public Task<List<ValueListItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_items.OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase).ToList());
        }
    }

    public Task<ValueListItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_items.FirstOrDefault(item => item.Id == id));
        }
    }

    public Task<ValueListItem?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(
                _items.FirstOrDefault(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public Task CreateAsync(ValueListItem item, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _items.Add(item);
            return Task.CompletedTask;
        }
    }

    public Task UpdateAsync(ValueListItem item, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(existing => existing.Id == item.Id);
            if (index >= 0)
            {
                _items[index] = item;
            }

            return Task.CompletedTask;
        }
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(existing => existing.Id == id);
            if (index >= 0)
            {
                _items.RemoveAt(index);
            }

            return Task.CompletedTask;
        }
    }
}
