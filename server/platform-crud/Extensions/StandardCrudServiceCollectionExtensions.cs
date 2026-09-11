using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Exports;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Query.Runtime;
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
        services.AddPlatformQueryKernel();
        services.TryAddSingleton<IStandardCrudResourceRegistry, StandardCrudResourceRegistry>();
        services.TryAddSingleton<StandardCrudXlsxWriter>();
        services.AddScoped<IStandardCrudRepository, EfStandardCrudRepository>();
        services.AddScoped<IStandardCrudService, StandardCrudService>();
        services.AddScoped<StandardCrudExportService>();
        services.AddScoped<StandardCrudEndpoint>();
        return services;
    }

    /// <summary>
    /// 显式注册一个标准 CRUD 资源；descriptor 是标准路由和字段边界的唯一真源。
    /// </summary>
    public static IServiceCollection AddStandardCrudResource(
        this IServiceCollection services,
        StandardCrudDescriptor descriptor)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        services.TryAddSingleton<IStandardCrudResourceRegistry, StandardCrudResourceRegistry>();
        services.AddSingleton(descriptor);
        return services;
    }

    /// <summary>
    /// 注册一个条件删除资源的删除守卫与原子条件删除执行器；内核要求二者成对存在，
    /// 同一请求作用域内两个接口解析到同一个实例。
    /// </summary>
    public static IServiceCollection AddStandardCrudConditionalDelete<TExecutor>(this IServiceCollection services)
        where TExecutor : class, IStandardCrudDeleteGuard, IStandardCrudConditionalDeleteExecutor
    {
        services.AddScoped<TExecutor>();
        services.AddScoped<IStandardCrudDeleteGuard>(static provider => provider.GetRequiredService<TExecutor>());
        services.AddScoped<IStandardCrudConditionalDeleteExecutor>(static provider => provider.GetRequiredService<TExecutor>());
        return services;
    }
}
