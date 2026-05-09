using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Crud.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ICP.Platform.Crud.Extensions;

public static class StandardCrudServiceCollectionExtensions
{
    /// <summary>
    /// 注册平台对象 ID 生成能力；该能力独立于标准 CRUD，供模块 service 写入 EID 时复用。
    /// </summary>
    public static IServiceCollection AddPlatformObjectIdGeneration(this IServiceCollection services)
    {
        services.TryAddSingleton<IPlatformObjectIdGenerator, PlatformObjectIdGenerator>();
        return services;
    }

    /// <summary>
    /// 注册标准 CRUD 内核基础服务；模块扩展点仍由各模块显式注册，避免平台默认耦合具体业务。
    /// </summary>
    public static IServiceCollection AddStandardCrudKernel(this IServiceCollection services)
    {
        services.AddPlatformObjectIdGeneration();
        services.AddScoped<IStandardCrudRepository, EfStandardCrudRepository>();
        services.AddScoped<IStandardCrudService, StandardCrudService>();
        services.AddScoped<StandardCrudEndpoint>();
        return services;
    }
}
