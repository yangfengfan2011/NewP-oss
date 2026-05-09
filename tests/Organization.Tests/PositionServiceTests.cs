using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class PositionServiceTests
{
    private const string CompanyId = "Company_11111111111111111111111111111111";
    private const string DepartmentId = "Department_22222222222222222222222222222222";
    private const string DepartmentId2 = "Department_33333333333333333333333333333333";

    [Fact]
    public async Task CreatePosition_WhenEmploymentStatusOmitted_DefaultsToFullTime()
    {
        var service = BuildService();

        var created = await service.CreateAsync(new PositionDto
        {
            CompanyId = CompanyId,
            DepartmentId = DepartmentId,
            Code = "DEV",
            Name = "开发工程师",
            SortOrder = "1"
        });

        Assert.Equal(EmploymentStatus.FullTime, created.EmploymentStatus);
    }

    [Fact]
    public async Task CreatePosition_WithExplicitEmploymentStatus_UsesRequestedStatus()
    {
        var service = BuildService();

        var created = await service.CreateAsync(new PositionDto
        {
            CompanyId = CompanyId,
            DepartmentId = DepartmentId,
            Code = "CONSULTANT",
            Name = "顾问",
            EmploymentStatus = EmploymentStatus.Contractor,
            SortOrder = "1"
        });

        Assert.Equal(EmploymentStatus.Contractor, created.EmploymentStatus);
    }

    [Fact]
    public async Task UpdatePosition_ValidRequest_ReturnsUpdatedAt()
    {
        var service = BuildService();
        var created = await service.CreateAsync(new PositionDto
        {
            CompanyId = CompanyId,
            DepartmentId = DepartmentId,
            Code = "DEV",
            Name = "开发工程师",
            EmploymentStatus = EmploymentStatus.FullTime,
            SortOrder = "1"
        });

        var updated = await service.UpdateAsync(created.Id, new PositionDto
        {
            DepartmentId = DepartmentId2,
            Name = "高级开发工程师",
            EmploymentStatus = EmploymentStatus.Contractor,
            IsEnabled = false,
            SortOrder = "2"
        });

        Assert.Equal("高级开发工程师", updated.Name);
        Assert.Equal(DepartmentId2, updated.DepartmentId);
        Assert.False(updated.IsEnabled);
        Assert.NotNull(updated.UpdatedAt);
    }

    private static PositionService BuildService() =>
        new(new InMemoryPositionRepository(), new PlatformObjectIdGenerator());

    private sealed class InMemoryPositionRepository : IPositionRepository
    {
        private readonly List<Position> _positions = [];

        public Task<List<Position>> GetByDepartmentIdAsync(string departmentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_positions.Where(position => position.DepartmentId == departmentId).Select(Clone).ToList());

        public Task<Position?> GetByIdAsync(string positionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_positions.FirstOrDefault(position => position.Id == positionId) is { } position ? Clone(position) : null);

        public Task CreateAsync(Position position, CancellationToken cancellationToken = default)
        {
            _positions.Add(Clone(position));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Position position, CancellationToken cancellationToken = default)
        {
            var index = _positions.FindIndex(existing => existing.Id == position.Id);
            if (index >= 0)
            {
                _positions[index] = Clone(position);
            }

            return Task.CompletedTask;
        }

        private static Position Clone(Position position) => new()
        {
            Id = position.Id,
            CompanyId = position.CompanyId,
            DepartmentId = position.DepartmentId,
            Code = position.Code,
            Name = position.Name,
            EmploymentStatus = position.EmploymentStatus,
            IsEnabled = position.IsEnabled,
            SortOrder = position.SortOrder,
            CreatedAt = position.CreatedAt,
            UpdatedAt = position.UpdatedAt
        };
    }
}
