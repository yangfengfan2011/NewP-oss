using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.Audit.Repositories;
using ICP.Platform.Modules.Audit.Services;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Audit;

public class AuditModule : ModuleBase, IPermissionProvider, IMenuProvider
{
    public override string ModuleId => "Audit";
    public override string DisplayName => "审计日志";
    public override string Version => "1.0.0";
    public override string Description => "平台审计与日志管理模块";

    public override void ConfigureServices(IServiceCollection services)
    {
        // 注册核心服务
        services.AddScoped<IAuditService, AuditService>();

        // 注册仓储
        services.AddSingleton<IOperationLogRepository, OperationLogRepository>();
        services.AddSingleton<IAccessLogRepository, AccessLogRepository>();
    }

    public IReadOnlyList<PermissionDescriptor> GetPermissions()
    {
        return new[]
        {
            new PermissionDescriptor
            {
                Code = "Audit.OperationLog.View",
                DisplayName = "查看操作日志",
                Description = "允许查看操作日志列表和详情",
                ModuleId = ModuleId,
                Group = "操作日志"
            },
            new PermissionDescriptor
            {
                Code = "Audit.AccessLog.View",
                DisplayName = "查看访问日志",
                Description = "允许查看访问日志列表和详情",
                ModuleId = ModuleId,
                Group = "访问日志"
            }
        };
    }

    public IReadOnlyList<MenuDescriptor> GetMenus()
    {
        return new[]
        {
            new MenuDescriptor
            {
                Id = "Audit",
                DisplayName = "审计日志",
                ParentId = null,
                Route = "/audit",
                Icon = "document-text",
                Order = 20,
                RequiredPermission = "Audit.OperationLog.View",
                ModuleId = ModuleId
            },
            new MenuDescriptor
            {
                Id = "Audit.OperationLogs",
                DisplayName = "操作日志",
                ParentId = "Audit",
                Route = "/audit/operation-logs",
                Icon = "clipboard-list",
                Order = 1,
                RequiredPermission = "Audit.OperationLog.View",
                ModuleId = ModuleId
            },
            new MenuDescriptor
            {
                Id = "Audit.AccessLogs",
                DisplayName = "访问日志",
                ParentId = "Audit",
                Route = "/audit/access-logs",
                Icon = "globe-alt",
                Order = 2,
                RequiredPermission = "Audit.AccessLog.View",
                ModuleId = ModuleId
            }
        };
    }
}
