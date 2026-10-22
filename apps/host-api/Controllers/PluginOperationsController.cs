using ICP.Platform.HostApi.PluginOperations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.HostApi.Controllers;

/// <summary>
/// 提供离线部署结果的只读观察，并稳定拒绝运行时插件变更。
/// 仅限 Admin 角色访问；写入口保留用于向旧调用方返回明确的不支持语义。
/// </summary>
[ApiController]
[Route("api/plugin-ops")]
[Authorize(Policy = "AdminOnly")]
public class PluginOperationsController : ControllerBase
{
    private const string RuntimeOperationsUnsupportedCode = "PLUGIN_RUNTIME_OPERATIONS_UNSUPPORTED";
    private readonly PluginOperationsAdminService _adminService;

    /// <summary>
    /// 创建只读插件部署观察入口。
    /// </summary>
    /// <param name="adminService">读取已部署插件状态的服务。</param>
    public PluginOperationsController(PluginOperationsAdminService adminService)
    {
        _adminService = adminService;
    }

    /// <summary>
    /// 获取 Host 启动前离线部署形成的只读模块状态列表。
    /// </summary>
    [HttpGet("modules")]
    public IActionResult GetModules()
    {
        var result = _adminService.GetAllModules();
        return Ok(result);
    }

    /// <summary>
    /// 根据 moduleId 获取 Host 启动前离线部署形成的只读模块状态详情。
    /// </summary>
    [HttpGet("modules/{moduleId}")]
    public IActionResult GetModule(string moduleId)
    {
        var module = _adminService.GetModule(moduleId);
        if (module is null)
        {
            return NotFound(new { message = $"模块 '{moduleId}' 不存在。" });
        }

        return Ok(module);
    }

    /// <summary>
    /// 拒绝在运行中的 Host 上传或验证会改变部署候选的插件包。
    /// </summary>
    [HttpPost("uploads/verify")]
    public IActionResult UploadVerify()
    {
        return RuntimeOperationsUnsupported();
    }

    /// <summary>
    /// 拒绝在运行中的 Host 安装插件包。
    /// </summary>
    [HttpPost("uploads/{uploadId}/install")]
    public IActionResult Install(string uploadId)
    {
        return RuntimeOperationsUnsupported();
    }

    /// <summary>
    /// 拒绝在运行中的 Host 启用插件版本。
    /// </summary>
    [HttpPost("modules/{moduleId}/enable")]
    public IActionResult Enable(string moduleId)
    {
        return RuntimeOperationsUnsupported();
    }

    /// <summary>
    /// 拒绝在运行中的 Host 禁用插件。
    /// </summary>
    [HttpPost("modules/{moduleId}/disable")]
    public IActionResult Disable(string moduleId)
    {
        return RuntimeOperationsUnsupported();
    }

    /// <summary>
    /// 拒绝在运行中的 Host 卸载插件。
    /// </summary>
    [HttpDelete("modules/{moduleId}")]
    public IActionResult Uninstall(string moduleId)
    {
        return RuntimeOperationsUnsupported();
    }

    /// <summary>
    /// 拒绝在运行中的 Host 回滚插件。
    /// </summary>
    [HttpPost("modules/{moduleId}/rollback")]
    public IActionResult Rollback(string moduleId)
    {
        return RuntimeOperationsUnsupported();
    }

    /// <summary>
    /// 返回运行时插件变更统一的不支持 ProblemDetails。
    /// </summary>
    private ObjectResult RuntimeOperationsUnsupported()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status501NotImplemented,
            Title = "运行时插件变更不受支持",
            Detail = "插件变更只允许在 Host 停止期间通过离线部署完成，并在重启后生效。"
        };
        problem.Extensions["code"] = RuntimeOperationsUnsupportedCode;

        return StatusCode(StatusCodes.Status501NotImplemented, problem);
    }
}
