using ICP.Platform.Modules.ConfigCenter.DTOs;

namespace ICP.Platform.Modules.ConfigCenter.Services;

public interface IValueListService
{
    Task<List<ValueListItemDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ValueListItemDto> CreateAsync(ValueListItemDto dto, string? updatedBy = null, CancellationToken cancellationToken = default);
    Task<ValueListItemDto> UpdateAsync(string id, ValueListItemDto dto, string? updatedBy = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
