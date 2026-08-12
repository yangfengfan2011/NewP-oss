using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore.Extensions;
using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.HostApi.Readiness;
using ICP.Platform.HostApi.Persistence;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        services.AddHttpContextAccessor();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<IEntityAuditStampProvider, HttpContextEntityAuditStampProvider>();
        var repositoryRoot = LocateRepositoryRootFromAppBase();
        services.AddEfCorePersistence(
            configuration,
            plan,
            WorktreeDatabaseIdentity.Resolve(repositoryRoot));
        services.AddSingleton<PersistenceReadinessState>();
        services.AddSingleton<IReadinessContributor, PersistenceReadinessContributor>();
        return services;
    }

    public static async Task ApplyPlatformPersistenceAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default,
        Action<bool>? observeDatabaseAhead = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var scope = app.Services.CreateScope();
        var readiness = scope.ServiceProvider.GetRequiredService<PersistenceReadinessState>();

        try
        {
            var migrationRunner = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationRunner>();
            try
            {
                await migrationRunner.MigrateAsync(cancellationToken);
                if (migrationRunner.DatabaseAhead)
                {
                    throw new PersistencePlanExecutionException(
                        PersistenceHistoryFailureCodes.FrontierMismatch,
                        "platform.persistence");
                }
            }
            finally
            {
                observeDatabaseAhead?.Invoke(migrationRunner.DatabaseAhead);
            }

            // migration 已完成但插件活动快照尚未提交；所有环境都在同一 seam 运行只读检查。
            var consistencyChecker = scope.ServiceProvider.GetRequiredService<IPersistenceConsistencyChecker>();
            await consistencyChecker.CheckAsync(cancellationToken);

            readiness.MarkReady();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            readiness.MarkStarting();
            throw;
        }
        catch (Exception exception)
        {
            readiness.MarkFailed(exception switch
            {
                DatabaseOwnershipException ownershipException => ownershipException.FailureCode,
                PersistencePlanExecutionException executionException => executionException.FailureCode,
                PersistenceHistoryException historyException => historyException.FailureCode,
                _ => PersistenceReadinessFailureCodes.BootstrapFailed
            });
            throw;
        }
    }

    public static IReadOnlyList<ICP.Platform.Modeling.Definitions.ModelDefinition> GetPlatformModelDefinitions(
        CompiledPersistencePlan plan) =>
        PlatformPersistenceModelCatalog.GetDefinitions(plan);

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
