using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.DevSync.Coordination;
using ICP.Platform.Persistence.EfCore.Extensions;

namespace ICP.Platform.HostApi.Extensions;

public static class PersistenceBootstrapExtensions
{
    public static IServiceCollection AddPlatformPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddEfCorePersistence(configuration, GetPlatformModelDefinitions());
        if (configuration.GetValue<bool>("DevelopmentSchemaSync:Enabled")
            && string.Equals(configuration["Persistence:Provider"], "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton(_ => BuildDevelopmentSchemaSyncOptions(configuration));
            services.AddScoped(static provider =>
                DevelopmentSchemaSyncCoordinator.Create(provider.GetRequiredService<DevelopmentSchemaSyncOptions>()));
        }

        return services;
    }

    public static async Task ApplyPlatformPersistenceAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var scope = app.Services.CreateScope();
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        if (environment.IsDevelopment()
            && configuration.GetValue<bool>("DevelopmentSchemaSync:Enabled")
            && string.Equals(configuration["Persistence:Provider"], "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            var coordinator = scope.ServiceProvider.GetRequiredService<DevelopmentSchemaSyncCoordinator>();
            var result = await coordinator.ExecuteAsync(cancellationToken);
            if (!result.StartupAllowed)
            {
                throw new InvalidOperationException(result.BuildMessage());
            }

            return;
        }

        var migrationRunner = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationRunner>();
        await migrationRunner.MigrateAsync(cancellationToken);
    }

    public static IReadOnlyList<ICP.Platform.Modeling.Definitions.ModelDefinition> GetPlatformModelDefinitions() =>
        PlatformPersistenceModelCatalog.GetDefinitions();

    private static DevelopmentSchemaSyncOptions BuildDevelopmentSchemaSyncOptions(IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? string.Empty;
        var connectionString = configuration.GetConnectionString("SqlServer") ?? string.Empty;

        return new DevelopmentSchemaSyncOptions(
            LocateRepositoryRootFromAppBase(),
            connectionString,
            provider,
            ApplyChanges: true,
            GenerateSqlPreview: false,
            // Host 启动没有交互输入；Confirm 风险已在 Prepare 阶段阻断，这里只允许 Safe plan 进入执行层。
            ConfirmApply: static _ => true);
    }

    private static string LocateRepositoryRootFromAppBase()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "apps"))
                && Directory.Exists(Path.Combine(current.FullName, "server"))
                && Directory.Exists(Path.Combine(current.FullName, "modules")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        // 开发态发布输出可只携带复制后的 modules；此时仍以 AppBase 为根，缺失的 authoring/DTO 依赖会由 coordinator 给出阻断信号。
        return AppContext.BaseDirectory;
    }
}
