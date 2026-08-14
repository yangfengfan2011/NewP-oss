using System.Security.Cryptography;
using System.Text;

namespace ICP.Platform.Acceptance;

internal sealed record AcceptanceRepositorySnapshot(
    string HeadCommit,
    string HeadTree,
    string TrackedStateDigest,
    string UntrackedBuildInputDigest,
    bool HasTrackedChanges,
    bool HasUntrackedBuildInputs,
    string TrackedStatus,
    string UntrackedBuildInputs)
{
    internal bool IsClean => !HasTrackedChanges && !HasUntrackedBuildInputs;

    private static readonly IReadOnlySet<string> BuildInputExtensions = new HashSet<string>(
        [
            ".cs", ".fs", ".vb", ".csproj", ".fsproj", ".vbproj", ".sln", ".slnx",
            ".props", ".targets", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs",
            ".css", ".scss", ".less", ".vue", ".razor", ".cshtml", ".json", ".xml",
            ".config", ".yaml", ".yml", ".toml", ".ps1", ".sh", ".sql", ".proto",
            ".graphql", ".gql", ".resx", ".lock"
        ],
        StringComparer.OrdinalIgnoreCase);

    internal static async Task<AcceptanceRepositorySnapshot> CaptureAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var headCommit = await AcceptanceInfrastructure.RunGitAsync(
            repositoryRoot,
            ["rev-parse", "--verify", "HEAD"],
            cancellationToken);
        var headTree = await AcceptanceInfrastructure.RunGitAsync(
            repositoryRoot,
            ["rev-parse", "HEAD^{tree}"],
            cancellationToken);
        var trackedStatus = await AcceptanceInfrastructure.RunGitRawAsync(
            repositoryRoot,
            ["status", "--porcelain=v2", "-z", "--untracked-files=no"],
            cancellationToken);
        var trackedDiff = await AcceptanceInfrastructure.RunGitRawAsync(
            repositoryRoot,
            ["diff", "--binary", "--full-index", "--no-textconv", "HEAD", "--"],
            cancellationToken);
        var untracked = await AcceptanceInfrastructure.RunGitRawAsync(
            repositoryRoot,
            ["ls-files", "--others", "--exclude-standard", "-z"],
            cancellationToken);
        var buildInputs = untracked
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(IsBuildInput)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new AcceptanceRepositorySnapshot(
            headCommit,
            headTree,
            Digest($"{trackedStatus}\0{trackedDiff}"),
            await DigestBuildInputsAsync(repositoryRoot, buildInputs, cancellationToken),
            trackedStatus.Length != 0 || trackedDiff.Length != 0,
            buildInputs.Length != 0,
            trackedStatus,
            string.Join('\n', buildInputs));
    }

    private static bool IsBuildInput(string relativePath)
    {
        var normalizedPath = relativePath.Replace('\\', '/');
        // CodeGraph 会在索引查询期间短暂创建运行态 lock；它不参与构建，不能把索引器活动误报为源码漂移。
        if (normalizedPath.Equals(".codegraph/codegraph.lock", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(relativePath);
        return BuildInputExtensions.Contains(Path.GetExtension(fileName))
            || fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("global.json", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("nuget.config", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("package-lock.", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("yarn.lock", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> DigestBuildInputsAsync(
        string repositoryRoot,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken)
    {
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relativePath in relativePaths)
        {
            var fullPath = Path.GetFullPath(Path.Combine(
                repositoryRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!fullPath.StartsWith(repositoryRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                || !File.Exists(fullPath))
            {
                aggregate.AppendData(Encoding.UTF8.GetBytes($"missing\0{relativePath}\0"));
                continue;
            }

            aggregate.AppendData(Encoding.UTF8.GetBytes($"file\0{relativePath}\0"));
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                aggregate.AppendData(buffer, 0, read);
            }
            aggregate.AppendData([0]);
        }

        return Convert.ToHexString(aggregate.GetHashAndReset()).ToLowerInvariant();
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal sealed record AcceptanceRepositoryChangeEvidence(
    DateTimeOffset ObservedAtUtc,
    string Kind,
    string? HeadCommit,
    string? HeadTree,
    IReadOnlyList<string> TrackedStatus,
    IReadOnlyList<string> UntrackedBuildInputs,
    string? Diagnostic);

internal sealed class AcceptanceRepositoryGuard : IAsyncDisposable
{
    private readonly string _repositoryRoot;
    private readonly TimeSpan _pollInterval;
    private readonly Func<string, CancellationToken, Task<AcceptanceRepositorySnapshot>> _snapshotCapture;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _monitor;
    private AcceptanceRepositoryChangeEvidence? _firstChange;
    private int _changed;
    private int _stopped;

    private AcceptanceRepositoryGuard(
        string repositoryRoot,
        AcceptanceRepositorySnapshot baseline,
        TimeSpan pollInterval,
        Func<string, CancellationToken, Task<AcceptanceRepositorySnapshot>> snapshotCapture)
    {
        _repositoryRoot = repositoryRoot;
        Baseline = baseline;
        _pollInterval = pollInterval;
        _snapshotCapture = snapshotCapture;
        _monitor = MonitorAsync();
    }

    internal AcceptanceRepositorySnapshot Baseline { get; }

    internal bool HasChanged => Volatile.Read(ref _changed) != 0;

    internal AcceptanceRepositoryChangeEvidence? FirstChange => Volatile.Read(ref _firstChange);

    internal static async Task<AcceptanceRepositoryGuard> StartAsync(
        string repositoryRoot,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default,
        Func<string, CancellationToken, Task<AcceptanceRepositorySnapshot>>? snapshotCapture = null)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        snapshotCapture ??= AcceptanceRepositorySnapshot.CaptureAsync;
        var baseline = await snapshotCapture(repositoryRoot, cancellationToken);
        if (!baseline.IsClean)
        {
            throw new InvalidOperationException(
                "ACC_REPOSITORY_NOT_CLEAN: acceptance requires no tracked changes or untracked build inputs.");
        }

        return new AcceptanceRepositoryGuard(
            repositoryRoot,
            baseline,
            pollInterval ?? TimeSpan.FromMilliseconds(100),
            snapshotCapture);
    }

    internal async Task<bool> StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
        {
            _stop.Cancel();
            try
            {
                await _monitor;
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }

            try
            {
                // 先停止后台采样，再以调用方 token 做最后一次同步快照，确保结束判定是
                // 本次运行的最后一个仓库观察点，而不是在取消 monitor 之前留下时序窗口。
                await CompareCurrentAsync(cancellationToken);
            }
            catch
            {
                LatchChange(CreateCaptureFailureEvidence("Final repository snapshot failed."));
            }
        }

        return !HasChanged;
    }

    internal async Task<bool> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await CompareCurrentAsync(cancellationToken);
        }
        catch
        {
            LatchChange(CreateCaptureFailureEvidence("Explicit repository snapshot failed."));
        }

        return !HasChanged;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stop.Dispose();
    }

    private async Task MonitorAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await CompareCurrentAsync(_stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                LatchChange(CreateCaptureFailureEvidence("Background repository snapshot failed."));
            }

            try
            {
                await Task.Delay(_pollInterval, _stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task CompareCurrentAsync(CancellationToken cancellationToken)
    {
        AcceptanceRepositorySnapshot? current = null;
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                current = await _snapshotCapture(_repositoryRoot, cancellationToken);
                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
                if (attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
                }
            }
        }

        if (current is null)
        {
            LatchChange(CreateCaptureFailureEvidence(lastError?.Message ?? "Repository snapshot failed twice."));
            return;
        }

        if (current != Baseline)
        {
            LatchChange(new AcceptanceRepositoryChangeEvidence(
                DateTimeOffset.UtcNow,
                current.HeadCommit != Baseline.HeadCommit || current.HeadTree != Baseline.HeadTree
                    ? "head-or-tree"
                    : current.HasTrackedChanges
                        ? "tracked"
                        : "untracked-build-input",
                current.HeadCommit,
                current.HeadTree,
                SplitEvidence(current.TrackedStatus, '\0'),
                SplitEvidence(current.UntrackedBuildInputs, '\n'),
                null));
        }
    }

    private void LatchChange(AcceptanceRepositoryChangeEvidence evidence)
    {
        Interlocked.CompareExchange(ref _firstChange, evidence, null);
        Interlocked.Exchange(ref _changed, 1);
    }

    private static AcceptanceRepositoryChangeEvidence CreateCaptureFailureEvidence(string diagnostic) =>
        new(
            DateTimeOffset.UtcNow,
            "capture-error",
            null,
            null,
            [],
            [],
            diagnostic);

    private static IReadOnlyList<string> SplitEvidence(string value, char separator) =>
        value.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
