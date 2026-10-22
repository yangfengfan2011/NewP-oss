using ICP.Platform.Modules.Files.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Modules.Files.Services;

/// <summary>Files 自有启动恢复及周期有界回收，不依赖通用 Jobs 或 Messaging。</summary>
public sealed class FileCleanupWorker(IServiceScopeFactory scopes, IOptions<FilesOptions> options,
    ILogger<FileCleanupWorker> logger) : BackgroundService
{
    /// <inheritdoc/>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _ = options.Value;
        using var scope = scopes.CreateScope();
        var requirements = scope.ServiceProvider.GetServices<FileOwnerPolicyRequirement>().Select(requirement => requirement.OwnerKey).ToArray();
        var policies = scope.ServiceProvider.GetServices<IFileOwnerPolicy>().ToDictionary(policy => policy.OwnerKey, StringComparer.Ordinal);
        if (requirements.Distinct(StringComparer.Ordinal).Count() != requirements.Length || requirements.Any(key => !policies.ContainsKey(key)))
            throw new InvalidOperationException("Files.RequiredOwnerPolicyMissingOrDuplicated");
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.CleanupIntervalMinutes));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<FileCleanupService>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogWarning(exception, "Files cleanup scan will retry on its next interval."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
