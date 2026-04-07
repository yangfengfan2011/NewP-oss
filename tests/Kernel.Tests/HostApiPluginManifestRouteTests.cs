using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class HostApiPluginManifestRouteTests : IDisposable
{
    private readonly UnifiedPluginPackageTestData _testData = new();
    private readonly List<string> _tempRoots = new();

    [Fact]
    public async Task HostApiStarts_WhenInstalledPluginStateIsMissing_UsingBuiltInModuleFallback()
    {
        var installRoot = CreateTempRoot();

        await using var host = await HostApiProcess.StartAsync(installRoot, skipLocalAuthSeeding: false);

        using var client = new HttpClient
        {
            BaseAddress = new Uri(host.BaseAddress),
            Timeout = TimeSpan.FromSeconds(2)
        };

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.IsSuccessStatusCode,
            $"Status: {response.StatusCode}\nBody: {body}\nSTDOUT:\n{host.StdOut}\nSTDERR:\n{host.StdErr}");
    }

    [Fact]
    public async Task FrontendPluginManifestRoute_ReturnsJson()
    {
        var installRoot = CreateTempRoot();
        var packageRoot = _testData.CreateValidPackage();
        EnableInstalledPackage(packageRoot, installRoot, "Audit");

        await using var host = await HostApiProcess.StartAsync(installRoot);

        using var client = new HttpClient
        {
            BaseAddress = new Uri(host.BaseAddress),
            Timeout = TimeSpan.FromSeconds(2)
        };

        var response = await client.GetAsync("/plugins/frontend-plugin-manifest.json");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.IsSuccessStatusCode,
            $"Status: {response.StatusCode}\nBody: {body}\nSTDOUT:\n{host.StdOut}\nSTDERR:\n{host.StdErr}");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.TryGetProperty("plugins", out var plugins));
        Assert.Equal(JsonValueKind.Array, plugins.ValueKind);
        Assert.NotEmpty(plugins.EnumerateArray());
    }

    public void Dispose()
    {
        _testData.Dispose();

        foreach (var tempRoot in _tempRoots)
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"host_api_plugin_route_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);
        return root;
    }

    private static void EnableInstalledPackage(string packageRoot, string installRoot, string moduleId)
    {
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var installResult = installer.Install(packageRoot, installRoot);
        Assert.Equal("Installed", installResult.Status);

        Assert.True(stateStore.TryLoad(moduleId, out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        record!.CurrentEnabledVersion = record.Version;
        Assert.True(stateStore.TrySave(record, out var saveError));
        Assert.Null(saveError);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "apps", "host-api", "Program.cs")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class HostApiProcess : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _stdout;
        private readonly StringBuilder _stderr;

        private HostApiProcess(Process process, string baseAddress, StringBuilder stdout, StringBuilder stderr)
        {
            _process = process;
            BaseAddress = baseAddress;
            _stdout = stdout;
            _stderr = stderr;
        }

        public string BaseAddress { get; }
        public string StdOut => _stdout.ToString();
        public string StdErr => _stderr.ToString();

        public static async Task<HostApiProcess> StartAsync(string installRoot, bool skipLocalAuthSeeding = true)
        {
            var repoRoot = FindRepositoryRoot();
            var hostApiDllPath = Path.Combine(repoRoot, "apps", "host-api", "bin", "Debug", "net10.0", "HostApi.dll");
            if (!File.Exists(hostApiDllPath))
            {
                throw new FileNotFoundException($"HostApi build output not found: {hostApiDllPath}");
            }

            var port = GetFreeTcpPort();
            var baseAddress = $"http://127.0.0.1:{port}";
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = repoRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.StartInfo.ArgumentList.Add(hostApiDllPath);
            process.StartInfo.ArgumentList.Add("--urls");
            process.StartInfo.ArgumentList.Add(baseAddress);
            process.StartInfo.Environment["Platform__PluginInstallRoot"] = installRoot;
            process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";
            process.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            process.StartInfo.Environment["Platform__SkipLocalAuthSeeding"] = skipLocalAuthSeeding ? "true" : "false";

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start HostApi.");
            }

            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    stdout.AppendLine(args.Data);
                }
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    stderr.AppendLine(args.Data);
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var host = new HostApiProcess(process, baseAddress, stdout, stderr);
            await host.WaitForStartupAsync();
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                }
            }
            finally
            {
                _process.Dispose();
            }
        }

        private async Task WaitForStartupAsync()
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(BaseAddress),
                Timeout = TimeSpan.FromSeconds(2)
            };

            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                if (_process.HasExited)
                {
                    throw new InvalidOperationException($"HostApi exited early with code {_process.ExitCode}.\nSTDOUT:\n{_stdout}\nSTDERR:\n{_stderr}");
                }

                try
                {
                    _ = await client.GetAsync("/");
                    return;
                }
                catch (HttpRequestException)
                {
                    // Not ready yet.
                }
                catch (TaskCanceledException)
                {
                    // Not ready yet.
                }

                await Task.Delay(250);
            }

            throw new TimeoutException($"HostApi did not become reachable in time.\nSTDOUT:\n{_stdout}\nSTDERR:\n{_stderr}");
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}

