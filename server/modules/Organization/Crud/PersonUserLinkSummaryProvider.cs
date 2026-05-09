using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Repositories;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonUserLinkSummaryProvider : IStandardCrudSummaryProvider
{
    private readonly IPersonUserLinkRepository _repository;

    public PersonUserLinkSummaryProvider(IPersonUserLinkRepository repository)
    {
        _repository = repository;
    }

    public string ResourceKey => "organization.person";
    public string Key => "userLink";

    /// <summary>
    /// 构建人员账号绑定摘要；无绑定时显式返回 notConfigured，供前端区分未配置与读取失败。
    /// </summary>
    public async Task<StandardCrudSummaryBlock> BuildAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> detail,
        CancellationToken cancellationToken = default)
    {
        var personId = detail[descriptor.IdField]?.ToString() ?? string.Empty;
        var link = await _repository.GetByPersonIdAsync(personId, cancellationToken);
        if (link is null)
        {
            return new StandardCrudSummaryBlock { Status = "notConfigured" };
        }

        return new StandardCrudSummaryBlock
        {
            Status = "ready",
            Data = new PersonUserLinkDto
            {
                Id = link.PersonId,
                PersonId = link.PersonId,
                UserId = link.UserId,
                CreatedAt = link.LinkedAt,
                LinkedAt = link.LinkedAt,
                LinkedBy = link.LinkedBy
            }
        };
    }
}
