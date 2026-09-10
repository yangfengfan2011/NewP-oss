using ICP.Platform.Crud.Extensions;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.Audit.Repositories;
using ICP.Platform.Modules.Audit.Services;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Audit;

public sealed class AuditModule : ModuleCapsuleBindingBase
{
    public override string BehaviorKey => "audit.backend";
    /// <inheritdoc/>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddStandardCrudKernel();
        services.AddScoped<IBusinessAuditRecorder, BusinessAuditRecorder>();
        services.AddScoped<BusinessAuditQuery>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddSingleton<IOperationLogRepository, OperationLogRepository>();
        services.AddSingleton<IAccessLogRepository, AccessLogRepository>();
    }
}
