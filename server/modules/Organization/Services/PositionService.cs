using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using System.Globalization;

namespace ICP.Platform.Modules.Organization.Services;

public sealed class PositionService : IPositionService
{
    private readonly IPositionRepository _positionRepository;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;

    public PositionService(
        IPositionRepository positionRepository,
        IPlatformObjectIdGenerator objectIdGenerator)
    {
        _positionRepository = positionRepository;
        _objectIdGenerator = objectIdGenerator;
    }

    public async Task<List<PositionDto>> GetByDepartmentIdAsync(string departmentId, CancellationToken cancellationToken = default)
    {
        var positions = await _positionRepository.GetByDepartmentIdAsync(departmentId, cancellationToken);
        return positions.Select(MapToDto).ToList();
    }

    public async Task<PositionDto?> GetByIdAsync(string positionId, CancellationToken cancellationToken = default)
    {
        var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        return position is null ? null : MapToDto(position);
    }

    public async Task<PositionDto> CreateAsync(PositionDto dto, CancellationToken cancellationToken = default)
    {
        var position = new Position
        {
            Id = _objectIdGenerator.NewId(nameof(Position)),
            CompanyId = RequireId(dto.CompanyId, nameof(dto.CompanyId)),
            DepartmentId = RequireId(dto.DepartmentId, nameof(dto.DepartmentId)),
            Code = dto.Code,
            Name = dto.DisplayName,
            EmploymentStatus = dto.EmploymentStatus,
            SortOrder = ParseSortOrder(dto.SortOrder)
        };

        await _positionRepository.CreateAsync(position, cancellationToken);
        return MapToDto(position);
    }

    public async Task<PositionDto> UpdateAsync(string positionId, PositionDto dto, CancellationToken cancellationToken = default)
    {
        var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        if (position is null)
        {
            throw new InvalidOperationException($"岗位 '{positionId}' 不存在。");
        }

        // 岗位更新继续沿用现有边界：部门、名称、状态可改，代码与公司归属保持稳定。
        position.DepartmentId = RequireId(dto.DepartmentId, nameof(dto.DepartmentId));
        position.Name = dto.DisplayName;
        position.EmploymentStatus = dto.EmploymentStatus;
        position.IsEnabled = dto.IsEnabled;
        position.SortOrder = ParseSortOrder(dto.SortOrder);
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
        DisplayName = position.Name,
        EmploymentStatus = position.EmploymentStatus,
        IsEnabled = position.IsEnabled,
        SortOrder = position.SortOrder.ToString(CultureInfo.InvariantCulture),
        CreatedAt = position.CreatedAt,
        UpdatedAt = position.UpdatedAt
    };

    // 平台对象引用按字符串协议流转；服务层只负责空值失败信号。
    private static string RequireId(string value, string propertyName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{propertyName} 不能为空。")
            : value;

    private static int ParseSortOrder(string? sortOrder) =>
        string.IsNullOrWhiteSpace(sortOrder)
            ? 0
            : int.TryParse(sortOrder, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException("SortOrder 必须是有效整数。");
}
