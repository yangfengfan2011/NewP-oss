using ICP.Platform.Modules.Authentication.Application;

namespace ICP.Platform.HostApi.Authentication;

public sealed class AuthenticationSessionPurgeHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuthenticationSessionPurgeHostedService> _logger;
    private readonly TimeProvider _timeProvider;

    public AuthenticationSessionPurgeHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<AuthenticationSessionPurgeHostedService> logger,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var purge = scope.ServiceProvider.GetRequiredService<IAuthenticationSessionPurgeService>();
                await purge.PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Authentication session purge failed; the next scheduled run will retry.");
            }

            await Task.Delay(Interval, _timeProvider, stoppingToken);
        }
    }
}
