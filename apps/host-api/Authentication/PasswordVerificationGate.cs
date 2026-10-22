using Microsoft.Extensions.Options;

namespace ICP.Platform.HostApi.Authentication;

public sealed class PasswordVerificationGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    public PasswordVerificationGate(IOptions<HostAuthenticationOptions> options)
    {
        _semaphore = new SemaphoreSlim(options.Value.MaximumConcurrentPasswordVerifications);
    }

    public async ValueTask<IDisposable?> TryEnterAsync(CancellationToken cancellationToken)
    {
        if (!await _semaphore.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            return null;
        }

        return new Releaser(_semaphore);
    }

    public void Dispose() => _semaphore.Dispose();

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
