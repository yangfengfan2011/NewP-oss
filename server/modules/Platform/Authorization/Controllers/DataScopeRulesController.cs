using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.Authorization.Controllers;

/// <summary>独立规则管理资格由正式功能流水线验证，Controller 不拥有规则语义。</summary>
[ApiController, Route("api/authorization/data-scope"), EndpointModule("Authorization")]
public sealed class DataScopeRulesController(DataScopeCatalog catalog, DataScopeRules rules, DataScopeIdentities identities,
    ICP.Platform.Kernel.ModulePlanning.CanonicalModulePlan plan) : ControllerBase
{
    /// <summary>管理目录包含显式资源身份和动作开关；停用动作仍可配置。</summary>
    [HttpGet("catalog"), RequireFeature("authorization.dataScope.read")]
    public IActionResult Catalog() => Ok(new
    {
        actions = catalog.Actions,
        subjectTypes = identities.SubjectTypes,
        contextValues = identities.ContextValues,
        contextBindings = identities.ContextBindings,
        businessConditions = catalog.BusinessConditions,
        features = plan.FeatureCatalog.Where(point => point.BehaviorKey is "authorization.dataScope.read" or "authorization.dataScope.save"),
        resources = catalog.Resources.Select(resource => new
        {
            resource,
            fields = catalog.Fields(resource.ResourceKey).Select(field =>
            new
            {
                name = field.Name,
                type = (Nullable.GetUnderlyingType(field.ClrType) ?? field.ClrType).Name,
                enumValues = catalog.EnumValues(resource.ResourceKey, field.Name)
            })
        })
    });

    /// <summary>独立管理资格下读取最小主体目录，已有名称与候选使用同一个 owner。</summary>
    [HttpGet("subjects/{type}"), RequireFeature("authorization.dataScope.read")]
    public async Task<IActionResult> Subjects(string type, [FromQuery] string? query, [FromQuery] int page = 1,
        [FromQuery] string[]? currentIds = null, CancellationToken cancellationToken = default)
    {
        try { return Ok(await identities.SearchAsync(type, query, page, currentIds ?? [], cancellationToken)); }
        catch (RolePermissionValidationException exception)
        { return BadRequest(AuthorizationProblem.From(exception, "DataScope.InvalidConfiguration")); }
    }

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
            if (id != rule.Id) throw new RolePermissionValidationException("规则身份与目标不一致。", "DATA_SCOPE_RULE_TARGET_MISMATCH", field: "id");
            await rules.SaveAsync(rule, cancellationToken);
            return NoContent();
        }
        catch (RolePermissionValidationException exception)
        { return BadRequest(AuthorizationProblem.From(exception, "DataScope.InvalidConfiguration")); }
    }

    /// <summary>删除根规则及完整子树，配置和实际旧值审计共用一次事务。</summary>
    [HttpDelete("rules/{id}"), RequireFeature("authorization.dataScope.save")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        try { await rules.DeleteAsync(id, cancellationToken); return NoContent(); }
        catch (RolePermissionValidationException exception)
        { return BadRequest(AuthorizationProblem.From(exception, "DataScope.InvalidConfiguration")); }
    }
}
