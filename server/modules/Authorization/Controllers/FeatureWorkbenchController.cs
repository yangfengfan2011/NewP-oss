using ICP.Platform.Authorization;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ICP.Platform.Modules.Authorization.Controllers;

/// <summary>工作台业务只在 Authorization 内部组合；位置判权由平台在请求执行前完成。</summary>
[ApiController, Route("api/authorization/workbench"), EndpointModule("Authorization")]
public sealed class FeatureWorkbenchController(RoleManagementQuery roles, ActiveFeatureCatalog catalog,
    RoleFeatureWriter writer, EfPlatformDbContext db) : ControllerBase
{
    /// <summary>公开完整活动目录，不以操作者已有授权裁剪可配置目录；动态资格由当前主体投影提供。</summary>
    [HttpGet("catalog"), RequireFeature("authorization.workbench.read")]
    public IActionResult Catalog() => Ok(new FeatureCatalogResponse(catalog.Snapshot.Points));

    /// <summary>角色名称或代码搜索复用正式数据库分页查询。</summary>
    [HttpGet("roles"), RequireFeature("authorization.workbench.read")]
    public async Task<IActionResult> Roles([FromQuery] string? query, [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await roles.ListAsync(query, pageIndex, pageSize, cancellationToken));

    /// <summary>只读取目标角色授权，切换角色不重新装载目录。</summary>
    [HttpGet("roles/{roleId}"), RequireFeature("authorization.workbench.read")]
    public async Task<IActionResult> Role(string roleId, CancellationToken cancellationToken)
    {
        var role = await roles.GetAsync(roleId, cancellationToken);
        if (role is null) return NotFound();
        var snapshot = catalog.Snapshot;
        var assigned = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant")).AsNoTracking()
            .Where(row => EF.Property<string>(row, "RoleId") == roleId)
            .Select(row => EF.Property<string>(row, "FunctionPointId")).ToArrayAsync(cancellationToken);
        return Ok(new { role, positions = assigned.Where(snapshot.ById.ContainsKey).Select(id => snapshot.ById[id].Point.Position).ToArray() });
    }

    /// <summary>独立保存动作一次提交完整集合；校验失败保留明确 Problem Details。</summary>
    [HttpPut("roles/{roleId}"), RequireFeature("authorization.workbench.save")]
    public async Task<IActionResult> Save(string roleId, [FromBody] FeatureGrantRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Positions is null) throw new RolePermissionValidationException("必须提供完整功能集合。");
            await writer.ReplaceAsync(roleId, request.Positions, cancellationToken);
            return NoContent();
        }
        catch (RolePermissionValidationException exception)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = exception.Message });
        }
    }
}
/// <summary>角色授权的完整替换请求，空数组明确清空，省略集合无效。</summary>
public sealed record FeatureGrantRequest(IReadOnlyList<string>? Positions);
/// <summary>工作台的完整活动目录；不混入过期主体授权快照。</summary>
public sealed record FeatureCatalogResponse(IReadOnlyList<ICP.Platform.Kernel.ModulePlanning.FeaturePoint> Points);
