using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;

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
            CompanyId = dto.CompanyId,
            DepartmentId = dto.DepartmentId,
            Code = dto.Code,
            Name = dto.Name,
            EmploymentStatus = dto.EmploymentStatus,
            SortOrder = dto.SortOrder
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
        position.DepartmentId = dto.DepartmentId;
        position.Name = dto.Name;
        position.EmploymentStatus = dto.EmploymentStatus;
        position.IsEnabled = dto.IsEnabled;
        position.SortOrder = dto.SortOrder;
        position.UpdatedAt = DateTime.UtcNow;

        await _positionRepository.UpdateAsync(position, cancellationToken);
        return MapToDto(position);
    }

    private static PositionDto MapToDto(Position position) => new()
    {
        Id = position.Id,
        CompanyId = position.CompanyId,
        DepartmentId = position.DepartmentId,
        Code = position.Code,
        Name = position.Name,
        EmploymentStatus = position.EmploymentStatus,
        IsEnabled = position.IsEnabled,
        SortOrder = position.SortOrder,
        CreatedAt = position.CreatedAt
    };
}
