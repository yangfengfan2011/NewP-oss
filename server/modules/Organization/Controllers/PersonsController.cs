using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Organization.Controllers;

[ApiController]
[Route("api/organization/persons")]
public sealed class PersonsController : ControllerBase
{
    private readonly IPersonService _personService;
    private readonly IIdentityUserDirectory _identityUserDirectory;
    private readonly StandardCrudEndpoint _standardCrudEndpoint;
    private static readonly StandardCrudDescriptor PersonCrudDescriptor = PersonCrudDescriptorFactory.Create();

    public PersonsController(
        IPersonService personService,
        IIdentityUserDirectory identityUserDirectory,
        StandardCrudEndpoint standardCrudEndpoint)
    {
        _personService = personService;
        _identityUserDirectory = identityUserDirectory;
        _standardCrudEndpoint = standardCrudEndpoint;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PersonListItemDto>>> GetList(
        [FromQuery] string? keyword,
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? departmentId,
        [FromQuery] EmploymentStatus? employmentStatus,
        CancellationToken cancellationToken = default)
    {
        var result = await _standardCrudEndpoint.ListAsync(
            PersonCrudDescriptor,
            new StandardCrudListQuery(
                Page: 1,
                PageSize: int.MaxValue,
                Filters: new Dictionary<string, string?>
                {
                    ["keyword"] = keyword,
                    ["companyId"] = companyId?.ToString("D"),
                    ["departmentId"] = departmentId?.ToString("D"),
                    ["employmentStatus"] = employmentStatus?.ToString()
                }),
            cancellationToken);

        return Ok(result.Items.Select(MapListItem).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PersonDetailDto>> Get(Guid id, CancellationToken cancellationToken = default)
    {
        var detail = await _standardCrudEndpoint.GetDetailAsync(PersonCrudDescriptor, id.ToString("D"), cancellationToken);
        return detail is null ? NotFound() : Ok(MapDetail(detail));
    }

    [HttpPost]
    public async Task<ActionResult<PersonDto>> Create(
        [FromBody] PersonDto dto,
        CancellationToken cancellationToken = default)
    {
        var result = await _standardCrudEndpoint.CreateAsync(PersonCrudDescriptor, MapCreateValues(dto), cancellationToken);
        if (result.Succeeded && result.Detail is not null)
        {
            return CreatedAtAction(nameof(Get), new { id = ReadString(result.Detail.Fields, "EID") }, MapMutationDto(result.Detail));
        }

        var error = result.Error ?? MutationDetailMissing();
        return StatusCode(error.StatusCode, error);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PersonDto>> Update(
        Guid id,
        [FromBody] PersonDto dto,
        CancellationToken cancellationToken = default)
    {
        var result = await _standardCrudEndpoint.UpdateAsync(PersonCrudDescriptor, id.ToString("D"), MapUpdateValues(dto), cancellationToken);
        if (result.Succeeded && result.Detail is not null)
        {
            return Ok(MapMutationDto(result.Detail));
        }

        var error = result.Error ?? MutationDetailMissing();
        return StatusCode(error.StatusCode, error);
    }

    [HttpPost("{id:guid}/disable")]
    public async Task<ActionResult<PersonDetailDto>> Disable(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _standardCrudEndpoint.DisableAsync(PersonCrudDescriptor, id.ToString("D"), cancellationToken);
        return result.Succeeded && result.Detail is not null
            ? Ok(MapDetail(result.Detail))
            : StatusCode(result.Error!.StatusCode, result.Error);
    }

    [HttpGet("options")]
    public async Task<ActionResult<IReadOnlyList<StandardCrudOptionItem>>> Options(CancellationToken cancellationToken = default)
    {
        var options = await _standardCrudEndpoint.OptionsAsync(
            PersonCrudDescriptor,
            new StandardCrudListQuery(PageSize: 100),
            cancellationToken);

        return Ok(options);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _standardCrudEndpoint.DeleteAsync(PersonCrudDescriptor, id.ToString("D"), cancellationToken);
        return result.Succeeded ? NoContent() : StatusCode(result.Error!.StatusCode, result.Error);
    }

    [HttpGet("user-options")]
    public async Task<ActionResult<IReadOnlyList<IdentityUserOptionDto>>> SearchUserOptions(
        [FromQuery] string keyword,
        CancellationToken cancellationToken = default)
    {
        var options = await _identityUserDirectory.SearchAsync(keyword, cancellationToken);
        return Ok(options);
    }

    [HttpPut("{id:guid}/user-link")]
    public async Task<ActionResult<PersonUserLinkDto>> LinkUser(
        Guid id,
        [FromBody] LinkPersonUserRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _personService.LinkUserAsync(id, request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 将标准 CRUD 行数据收口回既有列表 DTO，避免前端在试点期感知字段命名差异。
    /// </summary>
    private static PersonListItemDto MapListItem(IReadOnlyDictionary<string, object?> row) => new()
    {
        Id = Guid.Parse(ReadString(row, "EID")),
        PersonNo = ReadString(row, "PersonNo"),
        Name = ReadString(row, "ENAME"),
        CompanyName = ReadString(row, "CompanyName"),
        DepartmentName = ReadString(row, "DepartmentName"),
        EmploymentStatus = Enum.TryParse<EmploymentStatus>(ReadString(row, "EmploymentStatus"), ignoreCase: true, out var status)
            ? status
            : EmploymentStatus.FullTime,
        IsEnabled = ReadBool(row, "IsEnabled", true)
    };

    /// <summary>
    /// 将标准 CRUD detail 映射到既有详情 DTO；摘要块保持状态化结构，具体读取失败由 provider 标记。
    /// </summary>
    private static PersonDetailDto MapDetail(StandardCrudDetail detail) => new()
    {
        Id = Guid.Parse(ReadString(detail.Fields, "EID")),
        PersonNo = ReadString(detail.Fields, "PersonNo"),
        Name = ReadString(detail.Fields, "ENAME"),
        CompanyId = ReadGuid(detail.Fields, "CompanyId"),
        DepartmentId = ReadGuid(detail.Fields, "DepartmentId"),
        PositionId = ReadGuid(detail.Fields, "PositionId"),
        EmploymentStatus = Enum.TryParse<EmploymentStatus>(ReadString(detail.Fields, "EmploymentStatus"), ignoreCase: true, out var status)
            ? status
            : EmploymentStatus.FullTime,
        IsEnabled = ReadBool(detail.Fields, "IsEnabled", true),
        CreatedAt = ReadDateTime(detail.Fields, "CreatedAt"),
        UserLinkSummary = MapSummary(detail.Summaries, "userLink"),
        AuthorizationSummary = MapSummary(detail.Summaries, "authorizationSummary")
    };

    private static string ReadString(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;

    private static bool ReadBool(IReadOnlyDictionary<string, object?> row, string key, bool fallback) =>
        row.TryGetValue(key, out var value) && value is bool typed ? typed : fallback;

    private static Guid? ReadGuid(IReadOnlyDictionary<string, object?> row, string key) =>
        Guid.TryParse(ReadString(row, key), out var guid) ? guid : null;

    private static DateTime ReadDateTime(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : default;

    private static PersonSummaryBlockDto MapSummary(IReadOnlyDictionary<string, StandardCrudSummaryBlock> summaries, string key) =>
        summaries.TryGetValue(key, out var block)
            ? new PersonSummaryBlockDto { Status = block.Status, Data = block.Data }
            : new PersonSummaryBlockDto { Status = "notConfigured" };

    /// <summary>
    /// 将既有 PersonDto 写入请求映射为标准 CRUD 字段，创建允许写入 PersonNo。
    /// </summary>
    private static IReadOnlyDictionary<string, object?> MapCreateValues(PersonDto dto) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["PersonNo"] = dto.PersonNo,
            ["ENAME"] = dto.DisplayName,
            ["CompanyId"] = dto.CompanyId,
            ["DepartmentId"] = dto.DepartmentId,
            ["PositionId"] = dto.PositionId,
            ["EmploymentStatus"] = dto.EmploymentStatus?.ToString(),
            ["IsEnabled"] = dto.IsEnabled
        };

    /// <summary>
    /// 更新请求不传 PersonNo，工号只读由字段白名单和映射层共同保护。
    /// </summary>
    private static IReadOnlyDictionary<string, object?> MapUpdateValues(PersonDto dto) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["ENAME"] = dto.DisplayName,
            ["CompanyId"] = dto.CompanyId,
            ["DepartmentId"] = dto.DepartmentId,
            ["PositionId"] = dto.PositionId,
            ["EmploymentStatus"] = dto.EmploymentStatus?.ToString(),
            ["IsEnabled"] = dto.IsEnabled
        };

    private static PersonDto MapMutationDto(StandardCrudDetail detail) => new()
    {
        Id = ReadString(detail.Fields, "EID"),
        PersonNo = ReadString(detail.Fields, "PersonNo"),
        DisplayName = ReadString(detail.Fields, "ENAME"),
        CompanyId = ReadStringOrNull(detail.Fields, "CompanyId"),
        DepartmentId = ReadStringOrNull(detail.Fields, "DepartmentId"),
        PositionId = ReadStringOrNull(detail.Fields, "PositionId"),
        EmploymentStatus = Enum.TryParse<EmploymentStatus>(ReadString(detail.Fields, "EmploymentStatus"), ignoreCase: true, out var status)
            ? status
            : EmploymentStatus.FullTime,
        IsEnabled = ReadBool(detail.Fields, "IsEnabled", true),
        CreatedAt = ReadDateTime(detail.Fields, "CreatedAt"),
        UpdatedAt = ReadNullableDateTime(detail.Fields, "UpdatedAt")
    };

    private static string? ReadStringOrNull(IReadOnlyDictionary<string, object?> row, string key)
    {
        var value = ReadString(row, key);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static DateTime? ReadNullableDateTime(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : null;

    private static StandardCrudError MutationDetailMissing() => new()
    {
        StatusCode = 500,
        Code = "StandardCrud.MutationDetailMissing",
        Message = "标准 CRUD 写入成功但未返回详情。",
        ReasonKey = "standardCrud.mutationDetailMissing"
    };
}
