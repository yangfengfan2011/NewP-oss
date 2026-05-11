using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;

namespace ICP.Platform.Modules.Organization.Services;

public interface IPersonUserLinkService
{
    /// <summary>
    /// 将人员绑定到身份账号；人员存在性通过标准 CRUD 资源校验，不依赖旧 Person 仓储。
    /// </summary>
    Task<PersonUserLinkDto> LinkUserAsync(
        string personId,
        LinkPersonUserRequest request,
        CancellationToken cancellationToken = default);
}
