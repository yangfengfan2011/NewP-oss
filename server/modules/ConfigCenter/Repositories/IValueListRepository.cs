using ICP.Platform.Modules.ConfigCenter.Models;

namespace ICP.Platform.Modules.ConfigCenter.Repositories;

public interface IValueListRepository
{
    Task<List<ValueListItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ValueListItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<ValueListItem?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task CreateAsync(ValueListItem item, CancellationToken cancellationToken = default);
    Task UpdateAsync(ValueListItem item, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
