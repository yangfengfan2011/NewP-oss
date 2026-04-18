using ICP.Platform.Modules.ConfigCenter.DTOs;
using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Repositories;
using ICP.Platform.Sdk.UserMessages;

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

        var code = dto.Code.Trim();
        var name = dto.Name.Trim();

        var existing = await _repository.GetByCodeAsync(code, cancellationToken);
        if (existing is not null)
        {
            throw CreateCodeAlreadyExistsException(code);
        }

        var item = new ValueListItem
        {
            Code = code,
            Name = name,
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

        var code = dto.Code.Trim();
        var name = dto.Name.Trim();

        var item = await _repository.GetByIdAsync(id, cancellationToken);
        if (item is null)
        {
            throw CreateItemNotFoundException(id);
        }

        var duplicated = await _repository.GetByCodeAsync(code, cancellationToken);
        if (duplicated is not null && duplicated.Id != id)
        {
            throw CreateCodeAlreadyExistsException(code);
        }

        item.Code = code;
        item.Name = name;
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
            throw CreateItemNotFoundException(id);
        }

        await _repository.DeleteAsync(id, cancellationToken);
    }

    private static void Validate(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw CreateCodeRequiredException();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw CreateNameRequiredException();
        }
    }

    private static UserMessageException CreateCodeRequiredException()
    {
        return new UserMessageException(
            400,
            "ValueLists.CodeRequired",
            fallbackMessage: "值列表编码不能为空。");
    }

    private static UserMessageException CreateNameRequiredException()
    {
        return new UserMessageException(
            400,
            "ValueLists.NameRequired",
            fallbackMessage: "值列表名称不能为空。");
    }

    private static UserMessageException CreateCodeAlreadyExistsException(string code)
    {
        return new UserMessageException(
            400,
            "ValueLists.CodeAlreadyExists",
            new Dictionary<string, object?>
            {
                ["code"] = code
            },
            fallbackMessage: $"值列表编码 {code} 已存在。");
    }

    private static UserMessageException CreateItemNotFoundException(string id)
    {
        return new UserMessageException(
            404,
            "ValueLists.ItemNotFound",
            new Dictionary<string, object?>
            {
                ["id"] = id
            },
            fallbackMessage: $"值列表项 {id} 不存在。");
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
