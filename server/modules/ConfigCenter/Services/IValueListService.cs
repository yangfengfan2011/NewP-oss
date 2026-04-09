using ICP.Platform.Modules.ConfigCenter.DTOs;

namespace ICP.Platform.Modules.ConfigCenter.Services;

public interface IValueListService
{
    Task<List<ValueListItemDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ValueListItemDto> CreateAsync(ValueListItemCreateDto dto, string? updatedBy = null, CancellationToken cancellationToken = default);
    Task<ValueListItemDto> UpdateAsync(string id, ValueListItemUpdateDto dto, string? updatedBy = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
