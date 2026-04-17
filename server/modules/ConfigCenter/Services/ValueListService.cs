using ICP.Platform.Modules.ConfigCenter.DTOs;
using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Repositories;

namespace ICP.Platform.Modules.ConfigCenter.Services;

public class ValueListService : IValueListService
{
    private readonly IValueListRepository _repository;

    public ValueListService(IValueListRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<ValueListItemDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await _repository.GetAllAsync(cancellationToken);
        return items
            .OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .Select(MapToDto)
            .ToList();
    }

    public async Task<ValueListItemDto> CreateAsync(
        ValueListItemDto dto,
        string? updatedBy = null,
        CancellationToken cancellationToken = default)
    {
        Validate(dto.Code, dto.Name);

        var existing = await _repository.GetByCodeAsync(dto.Code, cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationException($"值列表编码 '{dto.Code}' 已存在。");
        }

        var item = new ValueListItem
        {
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            UpdatedBy = updatedBy
        };

        await _repository.CreateAsync(item, cancellationToken);
        return MapToDto(item);
    }

    public async Task<ValueListItemDto> UpdateAsync(
        string id,
        ValueListItemDto dto,
        string? updatedBy = null,
        CancellationToken cancellationToken = default)
    {
        Validate(dto.Code, dto.Name);

        var item = await _repository.GetByIdAsync(id, cancellationToken);
        if (item is null)
        {
            throw new InvalidOperationException($"值列表项 '{id}' 不存在。");
        }

        var duplicated = await _repository.GetByCodeAsync(dto.Code, cancellationToken);
        if (duplicated is not null && duplicated.Id != id)
        {
            throw new InvalidOperationException($"值列表编码 '{dto.Code}' 已存在。");
        }

        item.Code = dto.Code.Trim();
        item.Name = dto.Name.Trim();
        item.Description = dto.Description?.Trim();
        item.UpdatedAt = DateTime.UtcNow;
        item.UpdatedBy = updatedBy;

        await _repository.UpdateAsync(item, cancellationToken);
        return MapToDto(item);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var item = await _repository.GetByIdAsync(id, cancellationToken);
        if (item is null)
        {
            throw new InvalidOperationException($"值列表项 '{id}' 不存在。");
        }

        await _repository.DeleteAsync(id, cancellationToken);
    }

    private static void Validate(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("值列表编码不能为空。");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("值列表名称不能为空。");
        }
    }

    private static ValueListItemDto MapToDto(ValueListItem item)
    {
        return new ValueListItemDto
        {
            Id = item.Id,
            Code = item.Code,
            Name = item.Name,
            Description = item.Description,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            UpdatedBy = item.UpdatedBy
        };
    }
}
