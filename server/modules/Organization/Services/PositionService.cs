using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using System.Globalization;

namespace ICP.Platform.Modules.Organization.Services;

public sealed class PositionService : IPositionService
{
    private readonly IPositionRepository _positionRepository;

    public PositionService(IPositionRepository positionRepository)
    {
        _positionRepository = positionRepository;
    }

    public async Task<List<PositionDto>> GetByDepartmentIdAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        var positions = await _positionRepository.GetByDepartmentIdAsync(departmentId, cancellationToken);
        return positions.Select(MapToDto).ToList();
    }

    public async Task<PositionDto?> GetByIdAsync(Guid positionId, CancellationToken cancellationToken = default)
    {
        var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        return position is null ? null : MapToDto(position);
    }

    public async Task<PositionDto> CreateAsync(PositionDto dto, CancellationToken cancellationToken = default)
    {
        var position = new Position
        {
            CompanyId = ParseGuid(dto.CompanyId, nameof(dto.CompanyId)),
            DepartmentId = ParseGuid(dto.DepartmentId, nameof(dto.DepartmentId)),
            Code = dto.Code,
            Name = dto.Name,
            EmploymentStatus = dto.EmploymentStatus,
            SortOrder = ParseSortOrder(dto.SortOrder)
        };

        await _positionRepository.CreateAsync(position, cancellationToken);
        return MapToDto(position);
    }

    public async Task<PositionDto> UpdateAsync(Guid positionId, PositionDto dto, CancellationToken cancellationToken = default)
    {
        var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        if (position is null)
        {
            throw new InvalidOperationException($"岗位 '{positionId}' 不存在。");
        }

        // 岗位更新继续沿用现有边界：部门、名称、状态可改，代码与公司归属保持稳定。
        position.DepartmentId = ParseGuid(dto.DepartmentId, nameof(dto.DepartmentId));
        position.Name = dto.Name;
        position.EmploymentStatus = dto.EmploymentStatus;
        position.IsEnabled = dto.IsEnabled;
        position.SortOrder = ParseSortOrder(dto.SortOrder);
        position.UpdatedAt = DateTime.UtcNow;

        await _positionRepository.UpdateAsync(position, cancellationToken);
        return MapToDto(position);
    }

    private static PositionDto MapToDto(Position position) => new()
    {
        Id = position.Id.ToString("D"),
        CompanyId = position.CompanyId.ToString("D"),
        DepartmentId = position.DepartmentId.ToString("D"),
        Code = position.Code,
        Name = position.Name,
        EmploymentStatus = position.EmploymentStatus,
        IsEnabled = position.IsEnabled,
        SortOrder = position.SortOrder.ToString(CultureInfo.InvariantCulture),
        CreatedAt = position.CreatedAt,
        UpdatedAt = position.UpdatedAt
    };

    // 生成 DTO 保持 string 形态，领域模型仍坚持强类型标识与排序字段。
    private static Guid ParseGuid(string value, string propertyName) =>
        Guid.TryParse(value, out var guid)
            ? guid
            : throw new InvalidOperationException($"{propertyName} 必须是有效 GUID。");

    private static int ParseSortOrder(string? sortOrder) =>
        string.IsNullOrWhiteSpace(sortOrder)
            ? 0
            : int.TryParse(sortOrder, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException("SortOrder 必须是有效整数。");
}
