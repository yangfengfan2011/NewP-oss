using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModuleLoader;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.HostApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[DenyAll("Legacy module introspection endpoints remain frozen in the current batch.")]
public class ModulesController : ControllerBase
{
    private readonly ModuleRegistry _registry;

    public ModulesController(ModuleRegistry registry)
    {
        _registry = registry;
    }

    [HttpGet]
    public IActionResult GetModules()
    {
        var modules = _registry.GetAllModules()
            .Select(m => new
            {
                m.Value.ModuleId,
                m.Value.DisplayName,
                m.Value.Version
            });

        return Ok(modules);
    }

    [HttpGet("permissions")]
    public IActionResult GetPermissions()
    {
        var permissions = _registry.GetAllPermissions();
        return Ok(permissions);
    }

    [HttpGet("menus")]
    public IActionResult GetMenus()
    {
        var menus = _registry.GetAllMenus();
        return Ok(menus);
    }

    [HttpGet("configs")]
    public IActionResult GetConfigs()
    {
        var configs = _registry.GetAllConfigs();
        return Ok(configs);
    }

    [HttpGet("migrations")]
    public IActionResult GetMigrations()
    {
        var migrations = _registry.GetAllMigrations();
        return Ok(migrations);
    }
}
