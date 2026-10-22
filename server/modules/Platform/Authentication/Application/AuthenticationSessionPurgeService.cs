using ICP.Platform.Modules.Authentication.Repositories;
using ICP.Platform.Persistence.Abstractions;

namespace ICP.Platform.Modules.Authentication.Application;

public interface IAuthenticationSessionPurgeService
{
    ValueTask<int> PurgeAsync(CancellationToken cancellationToken = default);
}

public sealed class AuthenticationSessionPurgeService : IAuthenticationSessionPurgeService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private readonly IAuthenticationSessionRepository _repository;
    private readonly IPlatformTransactionExecutor _transactionExecutor;
    private readonly TimeProvider _timeProvider;
    private readonly AuthenticationSessionOptions _options;

    public AuthenticationSessionPurgeService(
        IAuthenticationSessionRepository repository,
        IPlatformTransactionExecutor transactionExecutor,
        TimeProvider timeProvider,
        AuthenticationSessionOptions options)
    {
        _repository = repository;
        _transactionExecutor = transactionExecutor;
        _timeProvider = timeProvider;
        _options = options;
    }

    public ValueTask<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        if (_options.PurgeBatchSize is < 1 or > 1000)
        {
            throw new InvalidOperationException("Authentication purge batch size must be between 1 and 1000.");
        }

        var cutoff = _timeProvider.GetUtcNow().UtcDateTime.Subtract(Retention);
        return _transactionExecutor.ExecuteAsync(
            token => _repository.PurgeAsync(cutoff, _options.PurgeBatchSize, token),
            cancellationToken);
    }
}
