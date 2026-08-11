using System.Security.Cryptography;
using System.Text;

namespace ICP.Platform.Acceptance;

internal sealed record AcceptanceRepositorySnapshot(
    string HeadCommit,
    string HeadTree,
    string TrackedStateDigest,
    string UntrackedBuildInputDigest,
    bool HasTrackedChanges,
    bool HasUntrackedBuildInputs)
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
            buildInputs.Length != 0);
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

internal sealed class AcceptanceRepositoryGuard : IAsyncDisposable
{
    private readonly string _repositoryRoot;
    private readonly TimeSpan _pollInterval;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _monitor;
    private int _changed;
    private int _stopped;

    private AcceptanceRepositoryGuard(
        string repositoryRoot,
        AcceptanceRepositorySnapshot baseline,
        TimeSpan pollInterval)
    {
        _repositoryRoot = repositoryRoot;
        Baseline = baseline;
        _pollInterval = pollInterval;
        _monitor = MonitorAsync();
    }

    internal AcceptanceRepositorySnapshot Baseline { get; }

    internal bool HasChanged => Volatile.Read(ref _changed) != 0;

    internal static async Task<AcceptanceRepositoryGuard> StartAsync(
        string repositoryRoot,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var baseline = await AcceptanceRepositorySnapshot.CaptureAsync(repositoryRoot, cancellationToken);
        if (!baseline.IsClean)
        {
            throw new InvalidOperationException(
                "ACC_REPOSITORY_NOT_CLEAN: acceptance requires no tracked changes or untracked build inputs.");
        }

        return new AcceptanceRepositoryGuard(
            repositoryRoot,
            baseline,
            pollInterval ?? TimeSpan.FromMilliseconds(100));
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
                Interlocked.Exchange(ref _changed, 1);
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
            Interlocked.Exchange(ref _changed, 1);
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
                Interlocked.Exchange(ref _changed, 1);
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
        var current = await AcceptanceRepositorySnapshot.CaptureAsync(_repositoryRoot, cancellationToken);
        if (current != Baseline)
        {
            Interlocked.Exchange(ref _changed, 1);
        }
    }
}
