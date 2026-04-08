using ICP.Platform.HostApi.PluginOperations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.HostApi.Controllers;

/// <summary>
/// 插件运维只读接口：提供模块状态列表和单模块详情查询。
/// 仅限 Admin 角色访问。
/// </summary>
[ApiController]
[Route("api/plugin-ops")]
[Authorize(Policy = "AdminOnly")]
public class PluginOperationsController : ControllerBase
{
    private readonly PluginOperationsAdminService _adminService;
    private readonly PluginUploadService _uploadService;
    private readonly PluginOperationsLifecycleService _lifecycleService;

    public PluginOperationsController(
        PluginOperationsAdminService adminService,
        PluginUploadService uploadService,
        PluginOperationsLifecycleService lifecycleService)
    {
        _adminService = adminService;
        _uploadService = uploadService;
        _lifecycleService = lifecycleService;
    }

    /// <summary>
    /// 获取所有已安装模块的状态列表。
    /// </summary>
    [HttpGet("modules")]
    public IActionResult GetModules()
    {
        var result = _adminService.GetAllModules();
        return Ok(result);
    }

    /// <summary>
    /// 根据 moduleId 获取单个模块的状态详情。
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
    /// 上传并验证插件 zip 包。
    /// </summary>
    [HttpPost("uploads/verify")]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> UploadVerify()
    {
        var file = Request.Form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return BadRequest(new PluginUploadVerifyResultDto
            {
                IsValid = false,
                Errors = new[] { "未上传文件。" }
            });
        }

        using var stream = file.OpenReadStream();
        var result = await _uploadService.UploadAndVerifyAsync(stream, file.FileName);

        if (!result.IsValid)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// 从已验证的 upload 区域安装插件包。
    /// </summary>
    [HttpPost("uploads/{uploadId}/install")]
    public IActionResult Install(string uploadId)
    {
        var result = _lifecycleService.Install(uploadId);
        if (result.Status == "Failed")
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// 启用指定模块的指定版本。
    /// </summary>
    [HttpPost("modules/{moduleId}/enable")]
    public IActionResult Enable(string moduleId, [FromBody] PluginEnableRequest request)
    {
        var result = _lifecycleService.Enable(moduleId, request.Version);
        if (result.Status == "Failed")
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// 禁用指定模块。
    /// </summary>
    [HttpPost("modules/{moduleId}/disable")]
    public IActionResult Disable(string moduleId)
    {
        var result = _lifecycleService.Disable(moduleId);
        if (result.Status == "Failed")
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// 回滚指定模块到上一个已安装版本。
    /// </summary>
    [HttpPost("modules/{moduleId}/rollback")]
    public IActionResult Rollback(string moduleId)
    {
        var result = _lifecycleService.Rollback(moduleId);
        if (result.Status == "Failed")
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

}
