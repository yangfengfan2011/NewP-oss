using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;

namespace ICP.Platform.Acceptance;

internal sealed record PublishedWorkspaceTarget(WorkspaceAcceptanceActor Actor, string ShellOrigin);

// 首片只运输 InforCenter/Part；完整 baseline 建立旧数据，固定裁剪发行物消费同一 catalog。
internal sealed class PublishedComponentAcceptanceFixture(
    string repositoryRoot, string runDirectory, string runId,
    string baselineHostRoot, string hostRoot, string shellRoot, string packagePath)
{
    private readonly Dictionary<string, string?> _environment = new();
    private ReusableSqlServerDatabase? _database;
    private OwnedProcess? _host;
    private OwnedProcess? _shell;
    private string? _ownership;
    private Func<string?, string> _sanitize = value => value ?? string.Empty;
    private int _hostPort;
    private string Resources => Path.Combine(runDirectory, "resources");
    private string InstallRoot => Path.Combine(Resources, "plugins");
    private string HostOrigin => $"http://127.0.0.1:{_hostPort}";
    internal string ExistingCode => "ACC-OLD-P-" + runId[..8];
    internal string ExistingName => "独立安装旧零组件 " + runId[..8];

    internal static PublishedComponentAcceptanceFixture FromEnvironment(string repositoryRoot, string runDirectory, string runId) => new(
        repositoryRoot, runDirectory, runId,
        RequiredPath("NEWP_ACCEPTANCE_NATIVE_BASELINE_HOST"), RequiredPath("NEWP_ACCEPTANCE_NATIVE_HOST"),
        RequiredPath("NEWP_ACCEPTANCE_NATIVE_SHELL"), RequiredPath("NEWP_ACCEPTANCE_NATIVE_PACKAGE"));

    private static string RequiredPath(string variable) => Path.GetFullPath(Environment.GetEnvironmentVariable(variable)
        ?? throw new InvalidOperationException($"固定发行物验收需要 {variable}。"));

    internal async Task<PublishedWorkspaceTarget> PrepareAsync(Action<string> registerSecret,
        Action<string, string?> addConnectionSecrets, Func<string?, string> sanitize, CancellationToken token)
    {
        _sanitize = sanitize;
        var before = ReadTargetIdentity();
        await WriteAsync("target-before.json", before, token);
        await WriteCandidateAsync("candidate-before.json", token);
        _database = await ReusableSqlServerDatabase.AcquireAsync(repositoryRoot, token);
        addConnectionSecrets(_database.ConnectionString, new SqlConnectionStringBuilder(_database.ConnectionString).Password);
        Directory.CreateDirectory(Resources);
        _environment["ConnectionStrings__SqlServer"] = _database.ConnectionString;
        _environment["Platform__PluginInstallRoot"] = InstallRoot;
        _environment["Platform__ExternalPluginsEnabled"] = "false";
        _environment["Platform__FrontendPluginBaseUrl"] = "/plugins/runtime/assets";
        _environment["Authentication__Host__KeyRingPath"] = Path.Combine(Resources, "key-ring");
        _environment["Authentication__Host__CookieName"] = WorkspaceAcceptanceFixture.CookieName;
        _environment["Authentication__Host__AllowInsecureDevelopmentCookie"] = "true";
        _environment["Identity__LocalAdministrator__ECODE"] = "admin";
        _environment["ASPNETCORE_ENVIRONMENT"] = "Acceptance";
        var bootstrap = await FormalIdentityBootstrap.RunPublishedAsync(repositoryRoot,
            Path.Combine(baselineHostRoot, "HostApi.dll"), baselineHostRoot,
            Path.Combine(Resources, "bootstrap"), _environment, token);
        registerSecret(bootstrap.TemporaryCredential);
        _ownership = await ReadSqlJsonAsync("SELECT * FROM dbo.__NewPDatabaseOwnership FOR JSON PATH;", null, token);
        _hostPort = ReservePort();
        await StartHostAsync(baselineHostRoot, "baseline-host", token);
        using var administrator = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        var administratorPassword = "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        registerSecret(administratorPassword);
        await WorkspaceAcceptanceFixture.LoginAndChangePasswordAsync(administrator, "admin", bootstrap.TemporaryCredential,
            administratorPassword, token);
        var features = await AcceptanceFeatureCatalog.LoadAsync(administrator, token);
        var part = features.Behavior("Part", "part.part.read");
        var actor = await WorkspaceAcceptanceFixture.PrepareAsync(administrator, features, runId, registerSecret,
            [part, features.Action(part, "create", "list.pageActions")], token);
        using var ordinary = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        await WorkspaceAcceptanceFixture.LoginAsync(ordinary, actor, token);
        var oldId = await WorkspaceAcceptanceFixture.CreateMetadataObjectAsync(ordinary, features,
            "Part", "part.part.read", "/api/part/parts", ExistingCode, ExistingName, token);
        var baseline = await ReadContinuityAsync(oldId, token);
        await WriteAsync("sql-before.json", baseline, token);
        await StopHostAsync(token);

        await RunOfflineAsync("install-native", ["--package", packagePath], token);
        await RunOfflineAsync("enable", ["--module-id", "InforCenter", "--version", "1.0.0"], token);
        await RunOfflineAsync("enable", ["--module-id", "Part", "--version", "1.0.0"], token);
        await StartHostAsync(hostRoot, "installed-host", token);
        var after = await ReadContinuityAsync(oldId, token);
        await WriteAsync("sql-after.json", after, token);
        if (baseline != after) throw new InvalidOperationException("独立安装改变了旧 Part 行、物理映射或已提交迁移账本。");
        using var installed = WorkspaceAcceptanceFixture.CreateClient(_hostPort);
        await WorkspaceAcceptanceFixture.LoginAsync(installed, actor, token);
        var installedFeatures = await AcceptanceFeatureCatalog.LoadAsync(installed, token);
        var detail = await WorkspaceAcceptanceFixture.SendAsync(installed, HttpMethod.Get,
            "/api/part/parts/" + Uri.EscapeDataString(oldId), null,
            installedFeatures.Behavior("Part", "part.part.read").Position, token);
        var fields = detail.GetProperty("fields");
        if (fields.GetProperty("EID").GetString() != oldId || fields.GetProperty("ECODE").GetString() != ExistingCode
            || fields.GetProperty("ENAME").GetString() != ExistingName)
            throw new InvalidOperationException("安装后的正式 Part API 未返回 baseline 的旧身份与字段。");
        var runtime = await WorkspaceAcceptanceFixture.SendAsync(installed, HttpMethod.Get,
            "/plugins/active-module-runtime.json", null, null, token);
        var modules = runtime.GetProperty("modules").EnumerateArray().ToArray();
        var parent = modules.Single(module => module.GetProperty("moduleId").GetString() == "InforCenter");
        if (!parent.GetProperty("compositionMembers").EnumerateArray().Select(member => member.GetString()).SequenceEqual(["Part"]))
            throw new InvalidOperationException("首片未使用只含 Part 的正式 InforCenter 包。");
        await WriteAsync("api-continuity.json", new { oldId, detail, runtime, externalPluginsEnabled = false }, token);
        var targetAfter = ReadTargetIdentity();
        await WriteAsync("target-after.json", targetAfter, token);
        if (before != targetAfter) throw new InvalidOperationException("安装期间固定 Host/Shell 发行物发生变化。");
        var readyFile = Path.Combine(Resources, "shell-ready.json");
        _shell = StartProcess("node", [Path.Combine(repositoryRoot, "scripts", "performance", "production-shell-server.mjs"),
            "--assets-root", shellRoot, "--host-origin", HostOrigin, "--port", "0", "--run-id", runId, "--ready-file", readyFile],
            repositoryRoot, "production-shell");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!File.Exists(readyFile))
        {
            if (_shell.Process.HasExited) throw new InvalidOperationException("固定 Shell 服务在 ready 前退出。");
            await Task.Delay(100, timeout.Token);
        }
        using var ready = JsonDocument.Parse(await File.ReadAllTextAsync(readyFile, token));
        if (ready.RootElement.GetProperty("runId").GetString() != runId || ready.RootElement.GetProperty("pid").GetInt32() != _shell.Process.Id)
            throw new InvalidOperationException("固定 Shell ready 归属不匹配。");
        var shellOrigin = $"http://127.0.0.1:{ready.RootElement.GetProperty("port").GetInt32()}";
        await WriteAsync("initialization.json", new { early271 = true, catalog = ReusableSqlServerDatabase.Catalog,
            hostOrigin = HostOrigin, shellOrigin, actorId = actor.AccountId, packagePath,
            packageSha256 = FileHash(packagePath), baselineHostRoot, hostRoot, shellRoot }, token);
        return new(actor, shellOrigin);
    }

    private sealed record Continuity(string PhysicalMapping, string PartRow, string History);

    private async Task<Continuity> ReadContinuityAsync(string oldId, CancellationToken token)
    {
        var mapping = await ReadSqlJsonAsync("SELECT SCHEMA_NAME(schema_id) AS schemaName, name AS tableName FROM sys.tables WHERE name=N'Part' ORDER BY schemaName, tableName FOR JSON PATH;", null, token);
        var row = await ReadSqlJsonAsync("SELECT * FROM dbo.Part WHERE EID=@id FOR JSON PATH;", oldId, token);
        var history = await ReadSqlJsonAsync("SELECT * FROM dbo.__NewPMigrationHistory ORDER BY Position FOR JSON PATH;", null, token);
        using var rows = JsonDocument.Parse(row);
        using var ledger = JsonDocument.Parse(history);
        if (rows.RootElement.GetArrayLength() != 1 || !ledger.RootElement.EnumerateArray()
                .Any(item => item.GetProperty("OwnerModuleId").GetString() == "Part"))
            throw new InvalidOperationException("正式 baseline 未形成旧 Part 行与真实迁移历史。");
        return new(mapping, row, history);
    }

    // SQL FOR JSON 会拆成多行传输；完整拼接后才比较，不能用 ExecuteScalar 丢弃后续账本。
    private async Task<string> ReadSqlJsonAsync(string sql, string? id, CancellationToken token)
    {
        await using var connection = new SqlConnection(_database!.ConnectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (id is not null) command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync(token);
        var json = new StringBuilder();
        while (await reader.ReadAsync(token)) json.Append(reader.GetString(0));
        return json.Length == 0 ? "[]" : json.ToString();
    }

    private sealed record TargetIdentity(string Host, string Shell);

    private TargetIdentity ReadTargetIdentity()
    {
        if (!File.Exists(Path.Combine(hostRoot, "HostApi.dll")) || !File.Exists(Path.Combine(shellRoot, "index.html")))
            throw new InvalidOperationException("固定 Host/Shell 发行物缺失，验收不自动重新构建。");
        if (Directory.EnumerateFiles(hostRoot, "PartModule.dll", SearchOption.AllDirectories).Any()
            || Directory.Exists(Path.Combine(hostRoot, "modules", "Part"))
            || Directory.Exists(Path.Combine(hostRoot, "modules", "InforCenter")))
            throw new InvalidOperationException("目标 Host 已预装 Part 或旧 InforCenter。");
        var manifest = File.ReadAllText(Path.Combine(shellRoot, ".vite", "manifest.json"));
        if (manifest.Contains("/part/src/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("目标 Shell 仍含构建期 Part 实现。");
        return new(HashTree(hostRoot, path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)),
            HashTree(shellRoot, _ => true));
    }

    private static string HashTree(string root, Func<string, bool> include) => string.Join('\n',
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(include)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/') + ":" + FileHash(path)).Order(StringComparer.Ordinal));

    private static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private async Task StartHostAsync(string root, string logName, CancellationToken token)
    {
        _environment["ASPNETCORE_URLS"] = HostOrigin;
        _host = StartProcess("dotnet", [Path.Combine(root, "HostApi.dll")], root, logName);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(180));
        while (!timeout.IsCancellationRequested)
        {
            if (_host.Process.HasExited) throw new InvalidOperationException($"固定 Host {logName} 在就绪前退出；读取本轮脱敏日志。");
            try
            {
                using var response = await client.GetAsync(HostOrigin + "/api/health", timeout.Token);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(200, timeout.Token);
        }
        timeout.Token.ThrowIfCancellationRequested();
    }

    private async Task RunOfflineAsync(string operation, IReadOnlyList<string> arguments, CancellationToken token)
    {
        var command = new[] { Path.Combine(hostRoot, "HostApi.dll"), "plugin-deploy", operation }.Concat(arguments).ToArray();
        var process = StartProcess("dotnet", command, hostRoot, operation + "-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(180));
            await process.Process.WaitForExitAsync(timeout.Token);
            if (process.Process.ExitCode != 0) throw new InvalidOperationException($"正式 plugin-deploy {operation} 返回 {process.Process.ExitCode}。");
        }
        finally { await StopProcessAsync(process, CancellationToken.None); }
        await WriteAsync(operation + "-command-" + Guid.NewGuid().ToString("N") + ".json", new { file = "dotnet", arguments = command, exitCode = 0 }, token);
    }

    private sealed record OwnedProcess(Process Process, Task<string> Output, Task<string> Error, string LogName);

    private OwnedProcess StartProcess(string file, IReadOnlyList<string> arguments, string cwd, string logName)
    {
        var start = new ProcessStartInfo(file) { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var pair in _environment) start.Environment[pair.Key] = pair.Value;
        var process = Process.Start(start) ?? throw new InvalidOperationException($"无法启动固定发行物 {file}。");
        return new(process, process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync(),
            string.Concat(logName.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_')));
    }

    private async Task StopHostAsync(CancellationToken token)
    {
        if (_host is null) return;
        await StopProcessAsync(_host, token);
        _host = null;
    }

    // Process 对象仅来自本 fixture 的 CreateProcess；不按进程名、端口或裸 PID 清理其他会话。
    private async Task StopProcessAsync(OwnedProcess owned, CancellationToken token)
    {
        try
        {
            if (!owned.Process.HasExited) owned.Process.Kill(entireProcessTree: true);
            await owned.Process.WaitForExitAsync(token);
            await File.WriteAllTextAsync(Path.Combine(runDirectory, owned.LogName + ".stdout.log"), _sanitize(await owned.Output), token);
            await File.WriteAllTextAsync(Path.Combine(runDirectory, owned.LogName + ".stderr.log"), _sanitize(await owned.Error), token);
        }
        finally { owned.Process.Dispose(); }
    }

    internal async Task<AcceptanceCleanupResult> TeardownAsync(CancellationToken token)
    {
        try
        {
            if (_shell is not null) { await StopProcessAsync(_shell, token); _shell = null; }
            await StopHostAsync(token);
            if (_database is not null)
            {
                if (_ownership is not null && _ownership != await ReadSqlJsonAsync("SELECT * FROM dbo.__NewPDatabaseOwnership FOR JSON PATH;", null, token))
                    return new("failed", AcceptanceFailureCodes.OwnershipMismatch);
                await _database.DisposeAsync();
                _database = null;
            }
            if (Directory.Exists(Resources))
            {
                foreach (var directory in Directory.EnumerateDirectories(Resources))
                { AcceptanceInfrastructure.EnsureOwnedResourceRoot(runDirectory, directory); Directory.Delete(directory, true); }
                foreach (var file in Directory.EnumerateFiles(Resources)) File.Delete(file);
                Directory.Delete(Resources);
            }
            await WriteCandidateAsync("candidate-after.json", token);
            await WriteAsync("cleanup.json", new { catalog = ReusableSqlServerDatabase.Catalog, schemaCleaned = true,
                ownedProcessesStopped = true, resourcesRemoved = true }, token);
            return new("completed", null);
        }
        finally
        {
            if (_database is not null) { await _database.ReleaseAsync(); _database = null; }
        }
    }

    private Task WriteAsync<T>(string name, T value, CancellationToken token) =>
        AcceptanceInfrastructure.WriteJsonAsync(Path.Combine(runDirectory, name), value, token);

    private async Task WriteCandidateAsync(string name, CancellationToken token) => await WriteAsync(name, new
    {
        head = await AcceptanceInfrastructure.RunGitAsync(repositoryRoot, ["rev-parse", "HEAD"], token),
        tree = await AcceptanceInfrastructure.RunGitAsync(repositoryRoot, ["rev-parse", "HEAD^{tree}"], token),
        index = await AcceptanceInfrastructure.RunGitAsync(repositoryRoot, ["write-tree"], token),
        status = await AcceptanceInfrastructure.RunGitRawAsync(repositoryRoot, ["status", "--porcelain=v2"], token),
        trackedDiffSha256 = AcceptanceInfrastructure.Sha256(await AcceptanceInfrastructure.RunGitRawAsync(repositoryRoot, ["diff", "--binary", "HEAD"], token)),
        untracked = await ReadUntrackedCandidateAsync(token)
    }, token);

    private sealed record CandidateFile(string Path, string Sha256);

    // status 只能证明未跟踪路径存在；内容摘要同时覆盖本片新增资产与尚未提交的产品文件。
    private async Task<CandidateFile[]> ReadUntrackedCandidateAsync(CancellationToken token)
    {
        var files = await AcceptanceInfrastructure.RunGitRawAsync(repositoryRoot,
            ["ls-files", "--others", "--exclude-standard", "-z"], token);
        return files.Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal)
            .Select(path => new CandidateFile(path, FileHash(Path.Combine(repositoryRoot, path)))).ToArray();
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
