using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class CompanyServiceTests
{
    [Fact]
    public async Task CreateCompany_WithUniqueCode_CreatesCompany()
    {
        var service = BuildService();

        var result = await service.CreateAsync(new CompanyDto
        {
            Code = "ICP",
            Name = "研发中心",
            SortOrder = 10
        });

        Assert.Equal("ICP", result.Code);
        Assert.Equal("研发中心", result.Name);
        Assert.Equal(10, result.SortOrder);
        Assert.True(result.IsEnabled);
    }

    [Fact]
    public async Task CreateCompany_WithDuplicateCode_ThrowsInvalidOperationException()
    {
        var service = BuildService();

        await service.CreateAsync(new CompanyDto
        {
            Code = "ICP",
            Name = "研发中心"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new CompanyDto
            {
                Code = "ICP",
                Name = "重复公司"
            }));
    }

    [Fact]
    public async Task UpdateCompany_ValidRequest_UpdatesCompany()
    {
        var service = BuildService();
        var company = await service.CreateAsync(new CompanyDto
        {
            Code = "ICP",
            Name = "研发中心"
        });

        var updated = await service.UpdateAsync(company.Id, new CompanyDto
        {
            Name = "研发中心（更新）",
            IsEnabled = false,
            SortOrder = 20
        });

        Assert.Equal(company.Id, updated.Id);
        Assert.Equal("ICP", updated.Code);
        Assert.Equal("研发中心（更新）", updated.Name);
        Assert.False(updated.IsEnabled);
        Assert.Equal(20, updated.SortOrder);
    }

    private static ICompanyService BuildService() => new CompanyService(new InMemoryCompanyRepository());

    private sealed class InMemoryCompanyRepository : ICompanyRepository
    {
        private readonly List<Company> _companies = [];

        public Task<List<Company>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_companies.Select(Clone).ToList());

        public Task<Company?> GetByIdAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_companies.FirstOrDefault(company => company.Id == companyId) is { } company ? Clone(company) : null);

        public Task<Company?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _companies.FirstOrDefault(company => string.Equals(company.Code, code, StringComparison.OrdinalIgnoreCase)) is { } company
                    ? Clone(company)
                    : null);

        public Task CreateAsync(Company company, CancellationToken cancellationToken = default)
        {
            _companies.Add(Clone(company));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Company company, CancellationToken cancellationToken = default)
        {
            var index = _companies.FindIndex(existing => existing.Id == company.Id);
            if (index >= 0)
            {
                _companies[index] = Clone(company);
            }

            return Task.CompletedTask;
        }

        private static Company Clone(Company company) => new()
        {
            Id = company.Id,
            Code = company.Code,
            Name = company.Name,
            IsEnabled = company.IsEnabled,
            SortOrder = company.SortOrder,
            CreatedAt = company.CreatedAt,
            UpdatedAt = company.UpdatedAt
        };
    }
}
