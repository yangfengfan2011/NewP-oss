using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Exports;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Query.Runtime;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Kernel.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
        services.TryAddScoped<IEntityAuditValueProvider>(static provider =>
            new EntityAuditValueProvider(provider.GetService<IEntityAuditStampProvider>()));
        services.TryAddSingleton<IStandardCrudResourceRegistry, StandardCrudResourceRegistry>();
        services.TryAddSingleton<StandardCrudXlsxWriter>();
        services.AddScoped<EfStandardCrudRepository>();
        services.AddScoped<IStandardCrudRepository>(static provider =>
            provider.GetRequiredService<EfStandardCrudRepository>());
        services.AddScoped<StandardCrudCreateRequestPreparer>();
        services.AddOptions<ObjectOperationAssemblyOptions>();
        services.TryAddSingleton<ObjectOperationAssemblyPlan>();
        services.TryAddScoped<ObjectOperationContributionSet>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ObjectOperationAssemblyStartupValidator>());
        services.TryAddScoped<ObjectOperationExecutionContext>();
        services.TryAddScoped<IObjectOperationExecutionContext>(static provider =>
            provider.GetRequiredService<ObjectOperationExecutionContext>());
        services.AddScoped<ObjectOperationExecutor>(static provider =>
            new ObjectOperationExecutor(
                provider.GetRequiredService<ObjectOperationContributionSet>(),
                provider.GetRequiredService<IEntityAuditValueProvider>(),
                provider.GetService<ICP.Platform.Persistence.Abstractions.IPlatformTransactionExecutor>(),
                provider.GetRequiredService<ObjectOperationExecutionContext>()));
        services.AddScoped<IObjectOperationExecutor>(static provider =>
            provider.GetRequiredService<ObjectOperationExecutor>());
        services.TryAddScoped<Func<IObjectOperationExecutor>>(static provider =>
            () => provider.GetRequiredService<IObjectOperationExecutor>());
        services.AddScoped<StandardCrudService>();
        services.AddScoped<IStandardCrudService>(static provider =>
            provider.GetRequiredService<StandardCrudService>());
        services.AddScoped<StandardCrudExportService>();
        services.AddScoped<StandardCrudEndpoint>();
        return services;
    }

    /// <summary>
    /// 以对象拥有模块的正式 metadata 注册一个对象行为及其静态声明。
    /// </summary>
    public static IServiceCollection AddObjectBehavior<TBehavior>(
        this IServiceCollection services,
        IModuleMetadata owner,
        ObjectBehaviorDefinition definition)
        where TBehavior : class, IObjectBehavior =>
        services.AddObjectBehavior<TBehavior>(
            owner,
            definition,
            static (provider, _) => ActivatorUtilities.CreateInstance<TBehavior>(provider));

    /// <summary>
    /// 以显式工厂注册可复用对象行为；同一实现类型可按不同对象声明创建独立实例。
    /// </summary>
    public static IServiceCollection AddObjectBehavior<TBehavior>(
        this IServiceCollection services,
        IModuleMetadata owner,
        ObjectBehaviorDefinition definition,
        Func<IServiceProvider, ObjectBehaviorDefinition, TBehavior> factory)
        where TBehavior : class, IObjectBehavior
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddOptions<ObjectOperationAssemblyOptions>()
            .Configure(options => options.Behaviors.Add(new(
                owner.ModuleId,
                definition)));
        services.AddScoped<IObjectBehaviorContribution>(provider =>
            new ObjectBehaviorContribution(
                definition.ObjectKey,
                factory(provider, definition)));
        return services;
    }

    /// <summary>
    /// 注册可直接复用的标准 CRUD 对象行为；同一实现按 object identity 提供默认创建、更新和删除。
    /// </summary>
    public static IServiceCollection AddStandardCrudObjectBehavior(
        this IServiceCollection services,
        IModuleMetadata owner,
        ObjectBehaviorDefinition definition) =>
        services.AddObjectBehavior<StandardCrudObjectBehavior>(
            owner,
            definition,
            static (provider, declaredDefinition) => new StandardCrudObjectBehavior(
                declaredDefinition,
                provider.GetRequiredService<IStandardCrudResourceRegistry>(),
                () => provider.GetRequiredService<StandardCrudService>()));

    /// <summary>
    /// 以扩展提供模块的正式 metadata 注册一个对象动作扩展；模块依赖由启动期装配校验。
    /// </summary>
    public static IServiceCollection AddObjectOperationExtension<TExtension>(
        this IServiceCollection services,
        IModuleMetadata provider,
        ObjectOperationExtensionDefinition definition)
        where TExtension : class, IObjectOperationExtension =>
        services.AddObjectOperationExtension<TExtension>(
            provider,
            definition,
            static (serviceProvider, _) => ActivatorUtilities.CreateInstance<TExtension>(serviceProvider));

    /// <summary>
    /// 以显式工厂注册可复用扩展实现；实例始终与声明的扩展 identity 绑定。
    /// </summary>
    public static IServiceCollection AddObjectOperationExtension<TExtension>(
        this IServiceCollection services,
        IModuleMetadata provider,
        ObjectOperationExtensionDefinition definition,
        Func<IServiceProvider, ObjectOperationExtensionDefinition, TExtension> factory)
        where TExtension : class, IObjectOperationExtension
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(factory);
        var dependencies = provider.Dependencies.ToArray();
        services.AddOptions<ObjectOperationAssemblyOptions>()
            .Configure(options => options.Extensions.Add(new(
                provider.ModuleId,
                dependencies,
                definition)));
        var identity = new ObjectOperationExtensionIdentity(
            definition.ObjectKey,
            definition.Action,
            definition.ExtensionKey);
        services.AddScoped<IObjectOperationExtensionContribution>(serviceProvider =>
            new ObjectOperationExtensionContribution(
                identity,
                factory(serviceProvider, definition)));
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
