using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using System.Globalization;

namespace ICP.Platform.Modules.Organization.Services;

public sealed class CompanyService : ICompanyService
{
    private readonly ICompanyRepository _companyRepository;

    public CompanyService(ICompanyRepository companyRepository)
    {
        _companyRepository = companyRepository;
    }

    public async Task<List<CompanyDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var companies = await _companyRepository.GetAllAsync(cancellationToken);
        return companies.Select(MapToDto).ToList();
    }

    public async Task<CompanyDto?> GetByIdAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var company = await _companyRepository.GetByIdAsync(companyId, cancellationToken);
        return company is null ? null : MapToDto(company);
    }

    public async Task<CompanyDto> CreateAsync(CompanyDto dto, CancellationToken cancellationToken = default)
    {
        var existing = await _companyRepository.GetByCodeAsync(dto.Code, cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationException($"公司编码 '{dto.Code}' 已存在。");
        }

        var company = new Company
        {
            Code = dto.Code,
            Name = dto.DisplayName,
            SortOrder = ParseSortOrder(dto.SortOrder)
        };

        await _companyRepository.CreateAsync(company, cancellationToken);
        return MapToDto(company);
    }

    public async Task<CompanyDto> UpdateAsync(Guid companyId, CompanyDto dto, CancellationToken cancellationToken = default)
    {
        var company = await _companyRepository.GetByIdAsync(companyId, cancellationToken);
        if (company is null)
        {
            throw new InvalidOperationException($"公司 '{companyId}' 不存在。");
        }

        // 公司编码仍保持创建后不可改，避免本轮 DTO 收口顺带扩大行为变化。
        company.Name = dto.DisplayName;
        company.IsEnabled = dto.IsEnabled;
        company.SortOrder = ParseSortOrder(dto.SortOrder);
        company.UpdatedAt = DateTime.UtcNow;

        await _companyRepository.UpdateAsync(company, cancellationToken);
        return MapToDto(company);
    }

    private static CompanyDto MapToDto(Company company) => new()
    {
        Id = company.Id.ToString("D"),
        Code = company.Code,
        DisplayName = company.Name,
        IsEnabled = company.IsEnabled,
        SortOrder = company.SortOrder.ToString(CultureInfo.InvariantCulture),
        CreatedAt = company.CreatedAt,
        UpdatedAt = company.UpdatedAt
    };

    // 生成基础 DTO 以 catalog 字段类型为准；服务入口负责把可提交契约转换回领域模型类型。
    private static int ParseSortOrder(string? sortOrder) =>
        string.IsNullOrWhiteSpace(sortOrder)
            ? 0
            : int.TryParse(sortOrder, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidOperationException("SortOrder 必须是有效整数。");
}
