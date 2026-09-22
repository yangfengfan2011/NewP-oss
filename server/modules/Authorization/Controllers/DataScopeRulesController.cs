using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Authorization.Controllers;

/// <summary>独立规则管理资格由正式功能流水线验证，Controller 不拥有规则语义。</summary>
[ApiController, Route("api/authorization/data-scope"), EndpointModule("Authorization")]
public sealed class DataScopeRulesController(DataScopeCatalog catalog, DataScopeRules rules,
    ICP.Platform.Kernel.ModulePlanning.CanonicalModulePlan plan) : ControllerBase
{
    /// <summary>管理目录包含显式资源身份和动作开关；停用动作仍可配置。</summary>
    [HttpGet("catalog"), RequireFeature("authorization.dataScope.read")]
    public IActionResult Catalog() => Ok(new
    {
        actions = catalog.Actions,
        features = plan.FeatureCatalog.Where(point => point.BehaviorKey is "authorization.dataScope.read" or "authorization.dataScope.save"),
        resources = catalog.Resources.Select(resource => new { resource, fields = catalog.Fields(resource.ResourceKey).Select(field =>
            new { name = field.Name, type = (Nullable.GetUnderlyingType(field.ClrType) ?? field.ClrType).Name }) })
    });

    /// <summary>规则根列表使用管理分页。</summary>
    [HttpGet("rules"), RequireFeature("authorization.dataScope.read")]
    public async Task<IActionResult> List([FromQuery] int page = 1, CancellationToken cancellationToken = default) =>
        Ok(await rules.ListAsync(page, cancellationToken));

    /// <summary>完整校验后保存整根；只有配置校验错误翻译为 400，执行故障继续传播。</summary>
    [HttpPut("rules/{id}"), RequireFeature("authorization.dataScope.save")]
    public async Task<IActionResult> Save(string id, DataScopeRuleDto rule, CancellationToken cancellationToken)
    {
        try
        {
            if (id != rule.Id) throw new RolePermissionValidationException("规则身份与目标不一致。");
            await rules.SaveAsync(rule, cancellationToken);
            return NoContent();
        }
        catch (RolePermissionValidationException exception)
        { return BadRequest(new ProblemDetails { Status = 400, Title = exception.Message, Extensions = { ["code"] = "DataScope.InvalidConfiguration" } }); }
    }
}
