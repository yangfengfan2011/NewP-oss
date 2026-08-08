using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.DevSync.Coordination;
using ICP.Platform.Persistence.EfCore.Extensions;
using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.HostApi.Readiness;

namespace ICP.Platform.HostApi.Extensions;

public static class PersistenceBootstrapExtensions
{
    public static IServiceCollection AddPlatformPersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        CompiledPersistencePlan plan)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(plan);

        var repositoryRoot = LocateRepositoryRootFromAppBase();
        services.AddEfCorePersistence(
            configuration,
            plan,
            WorktreeDatabaseIdentity.Resolve(repositoryRoot));
        services.AddSingleton<PersistenceReadinessState>();
        services.AddSingleton<IReadinessContributor, PersistenceReadinessContributor>();
        if (configuration.GetValue<bool>("DevelopmentSchemaSync:Enabled")
            && string.Equals(configuration["Persistence:Provider"], "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton(_ => BuildDevelopmentSchemaSyncOptions(configuration));
            services.AddScoped(provider =>
                DevelopmentSchemaSyncCoordinator.Create(
                    provider.GetRequiredService<DevelopmentSchemaSyncOptions>(),
                    plan));
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
        var readiness = scope.ServiceProvider.GetRequiredService<PersistenceReadinessState>();

        try
        {
            var migrationRunner = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationRunner>();
            await migrationRunner.MigrateAsync(cancellationToken);

            if (environment.IsDevelopment()
                && configuration.GetValue<bool>("DevelopmentSchemaSync:Enabled")
                && string.Equals(configuration["Persistence:Provider"], "SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                var coordinator = scope.ServiceProvider.GetRequiredService<DevelopmentSchemaSyncCoordinator>();
                // Host 永远只准备和报告计划；所有 schema 写入只允许由 EF migrations 完成。
                var result = await coordinator.PrepareAsync(cancellationToken);
                if (!result.StartupAllowed)
                {
                    throw new InvalidOperationException(result.BuildMessage());
                }

                if (result.Plan is { } plan
                    && (plan.Summary.OperationCount > 0 || plan.Summary.UnmanagedTableCount > 0))
                {
                    app.Logger.LogWarning(
                        "Development schema drift reported after migrations: {OperationCount} operations ({SafeCount} safe, {ConfirmCount} confirm), {UnmanagedCount} unmanaged tables. No changes were applied.",
                        plan.Summary.OperationCount,
                        plan.Summary.SafeOperationCount,
                        plan.Summary.ConfirmOperationCount,
                        plan.Summary.UnmanagedTableCount);
                }
            }

            readiness.MarkReady(migrationRunner.DatabaseAhead);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            readiness.MarkStarting();
            throw;
        }
        catch (Exception exception)
        {
            readiness.MarkFailed(exception is DatabaseOwnershipException ownershipException
                ? ownershipException.FailureCode
                : PersistenceReadinessFailureCodes.BootstrapFailed);
            throw;
        }
    }

    public static IReadOnlyList<ICP.Platform.Modeling.Definitions.ModelDefinition> GetPlatformModelDefinitions(
        CompiledPersistencePlan plan) =>
        PlatformPersistenceModelCatalog.GetDefinitions(plan);

    private static DevelopmentSchemaSyncOptions BuildDevelopmentSchemaSyncOptions(IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? string.Empty;
        var connectionString = configuration.GetConnectionString("SqlServer") ?? string.Empty;

        return new DevelopmentSchemaSyncOptions(
            LocateRepositoryRootFromAppBase(),
            connectionString,
            provider,
            ApplyChanges: false,
            GenerateSqlPreview: false,
            // Host 只需要拿到完整 drift 报告；Confirm 计划不再获得执行权，也不替代 EF migration 阻断启动。
            AllowConfirmOperations: true);
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
