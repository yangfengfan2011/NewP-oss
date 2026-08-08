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

        await using var host = await HostApiProcess.StartAsync(installRoot);

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

    [Fact]
    public async Task ActiveModuleRuntimeCatalogRoute_BindsAllCompatibilityProjectionsToOneSnapshot()
    {
        var installRoot = CreateTempRoot();

        await using var host = await HostApiProcess.StartAsync(installRoot);

        using var client = new HttpClient
        {
            BaseAddress = new Uri(host.BaseAddress),
            Timeout = TimeSpan.FromSeconds(2)
        };

        var activeResponse = await client.GetAsync("/plugins/active-module-runtime.json");
        var activeBody = await activeResponse.Content.ReadAsStringAsync();
        Assert.True(
            activeResponse.IsSuccessStatusCode,
            $"Status: {activeResponse.StatusCode}\nBody: {activeBody}\nSTDOUT:\n{host.StdOut}\nSTDERR:\n{host.StdErr}");

        using var active = JsonDocument.Parse(activeBody);
        var snapshotId = active.RootElement.GetProperty("snapshotId").GetString();
        var projectionHash = active.RootElement.GetProperty("projections").GetProperty("projectionHash").GetString();
        Assert.False(string.IsNullOrWhiteSpace(snapshotId));
        Assert.Equal(active.RootElement.GetProperty("planHash").GetString(), projectionHash);
        Assert.Contains(
            active.RootElement.GetProperty("modules").EnumerateArray(),
            module => module.GetProperty("moduleId").GetString() == "Organization");

        foreach (var route in new[]
                 {
                     "/plugins/frontend-plugin-manifest.json",
                     "/plugins/metadata-runtime-manifest.json",
                     "/plugins/localization-manifest.json"
                 })
        {
            using var projection = JsonDocument.Parse(await client.GetStringAsync(route));
            Assert.Equal(snapshotId, projection.RootElement.GetProperty("snapshotId").GetString());
            Assert.Equal(projectionHash, projection.RootElement.GetProperty("projectionHash").GetString());
        }
    }

    [Fact]
    public async Task LocalizationManifestRoute_ReturnsJson()
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

        var response = await client.GetAsync("/plugins/localization-manifest.json");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.IsSuccessStatusCode,
            $"Status: {response.StatusCode}\nBody: {body}\nSTDOUT:\n{host.StdOut}\nSTDERR:\n{host.StdErr}");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.TryGetProperty("defaultLanguage", out _));
        Assert.True(document.RootElement.TryGetProperty("supportedLanguages", out _));
        Assert.True(document.RootElement.TryGetProperty("catalogs", out _));
    }

    [Fact]
    public async Task MetadataRuntimeManifestRoute_IncludesBuiltInModuleMetadata_WhenPluginStateIsMissing()
    {
        var installRoot = CreateTempRoot();

        await using var host = await HostApiProcess.StartAsync(installRoot);

        using var client = new HttpClient
        {
            BaseAddress = new Uri(host.BaseAddress),
            Timeout = TimeSpan.FromSeconds(2)
        };

        var manifestResponse = await client.GetAsync("/plugins/metadata-runtime-manifest.json");
        var manifestBody = await manifestResponse.Content.ReadAsStringAsync();

        Assert.True(
            manifestResponse.IsSuccessStatusCode,
            $"Status: {manifestResponse.StatusCode}\nBody: {manifestBody}\nSTDOUT:\n{host.StdOut}\nSTDERR:\n{host.StdErr}");

        using var manifestDocument = JsonDocument.Parse(manifestBody);
        var modules = manifestDocument.RootElement.GetProperty("modules").EnumerateArray().ToArray();
        Assert.Contains(modules, module => module.GetProperty("moduleId").GetString() == "Organization");

        var navResponse = await client.GetAsync("/plugins/runtime/modules/Organization/metadata/navigation/Organization.nav.json");
        var navBody = await navResponse.Content.ReadAsStringAsync();

        Assert.True(
            navResponse.IsSuccessStatusCode,
            $"Status: {navResponse.StatusCode}\nBody: {navBody}\nSTDOUT:\n{host.StdOut}\nSTDERR:\n{host.StdErr}");
        Assert.Contains("organization.personCenter", navBody);

        var localizationResponse = await client.GetAsync("/plugins/localization-manifest.json");
        var localizationBody = await localizationResponse.Content.ReadAsStringAsync();

        Assert.True(
            localizationResponse.IsSuccessStatusCode,
            $"Status: {localizationResponse.StatusCode}\nBody: {localizationBody}\nSTDOUT:\n{host.StdOut}\nSTDERR:\n{host.StdErr}");
        using var localizationDocument = JsonDocument.Parse(localizationBody);
        Assert.Equal(
            "人员中心",
            localizationDocument.RootElement
                .GetProperty("catalogs")
                .GetProperty("zhs")
                .GetProperty("Navigation.PersonCenter")
                .GetString());
    }

    [Fact]
    public async Task LocalizationManifestRoute_RefreshesBuiltInRuntimeMetadata_WhenStaleProjectionAlreadyExists()
    {
        var installRoot = CreateTempRoot();
        var stalePlatformMetadataPath = Path.Combine(installRoot, "runtime", "modules", "Platform", "metadata");
        Directory.CreateDirectory(stalePlatformMetadataPath);
        File.WriteAllText(
            Path.Combine(stalePlatformMetadataPath, "stale.txt"),
            "stale runtime metadata");

        await using var host = await HostApiProcess.StartAsync(installRoot);

        using var client = new HttpClient
        {
            BaseAddress = new Uri(host.BaseAddress),
            Timeout = TimeSpan.FromSeconds(2)
        };

        var localizationResponse = await client.GetAsync("/plugins/localization-manifest.json");
        var localizationBody = await localizationResponse.Content.ReadAsStringAsync();

        Assert.True(
            localizationResponse.IsSuccessStatusCode,
            $"Status: {localizationResponse.StatusCode}\nBody: {localizationBody}\nSTDOUT:\n{host.StdOut}\nSTDERR:\n{host.StdErr}");

        using var localizationDocument = JsonDocument.Parse(localizationBody);
        Assert.Equal(
            "插件运维",
            localizationDocument.RootElement
                .GetProperty("catalogs")
                .GetProperty("zhs")
                .GetProperty("Navigation.PluginOps")
                .GetString());

        Assert.False(File.Exists(Path.Combine(stalePlatformMetadataPath, "stale.txt")));
        Assert.True(File.Exists(Path.Combine(stalePlatformMetadataPath, "dictionaries", "Navigation.dic.json")));
    }

    [Fact]
    public async Task MetadataRuntimeManifestRoute_ReturnsJson_AndServesModuleMetadata()
    {
        var installRoot = CreateTempRoot();
        var packageRoot = _testData.CreateValidPackageWithMetadataAndResources("Organization");
        var packageMetadataRoot = Path.Combine(packageRoot, "modules", "Organization", "metadata");
        Directory.CreateDirectory(Path.Combine(packageMetadataRoot, "navigation"));
        File.WriteAllText(
            Path.Combine(packageMetadataRoot, "navigation", "Organization.nav.json"),
            """
            {
              "schemaVersion": "1.0",
              "type": "nav",
              "navigation": {
                "items": [
                  {
                    "key": "organization.personCenter",
                    "path": "/organization/persons",
                    "entryType": "standard-list",
                    "targetKey": "organization.person.list"
                  }
                ]
              }
            }
            """);
        EnableInstalledPackage(packageRoot, installRoot, "Organization");

        await using var host = await HostApiProcess.StartAsync(installRoot);

        using var client = new HttpClient
        {
            BaseAddress = new Uri(host.BaseAddress),
            Timeout = TimeSpan.FromSeconds(2)
        };

        var manifestResponse = await client.GetAsync("/plugins/metadata-runtime-manifest.json");
        var manifestBody = await manifestResponse.Content.ReadAsStringAsync();

        Assert.True(
            manifestResponse.IsSuccessStatusCode,
            $"Status: {manifestResponse.StatusCode}\nBody: {manifestBody}\nSTDOUT:\n{host.StdOut}\nSTDERR:\n{host.StdErr}");
        Assert.Equal("application/json", manifestResponse.Content.Headers.ContentType?.MediaType);

        using var manifestDocument = JsonDocument.Parse(manifestBody);
        var organizationModule = manifestDocument.RootElement
            .GetProperty("modules")
            .EnumerateArray()
            .Single(module => module.GetProperty("moduleId").GetString() == "Organization");
        Assert.Equal("navigation/Organization.nav.json", organizationModule.GetProperty("navFiles")[0].GetString());

        var navResponse = await client.GetAsync("/plugins/runtime/modules/Organization/metadata/navigation/Organization.nav.json");
        var navBody = await navResponse.Content.ReadAsStringAsync();

        Assert.True(
            navResponse.IsSuccessStatusCode,
            $"Status: {navResponse.StatusCode}\nBody: {navBody}\nSTDOUT:\n{host.StdOut}\nSTDERR:\n{host.StdErr}");
        Assert.Contains("organization.personCenter", navBody);
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
        var moduleAssemblyPath = moduleId switch
        {
            "Audit" => typeof(ICP.Platform.Modules.Audit.AuditModule).Assembly.Location,
            "Organization" => typeof(ICP.Platform.Modules.Organization.OrganizationModule).Assembly.Location,
            _ => throw new InvalidOperationException($"No test module assembly is registered for '{moduleId}'.")
        };
        File.Copy(moduleAssemblyPath, Path.Combine(packageRoot, "backend", "AuditModule.dll"), overwrite: true);

        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);
        var installResult = installer.Install(packageRoot, installRoot);
        Assert.Equal("Installed", installResult.Status);

        Assert.True(stateStore.TryLoad(moduleId, out var record, out var loadError));
        Assert.Null(loadError);
        Assert.NotNull(record);
        record!.CurrentEnabledVersion = record.Version;
        Assert.True(stateStore.TrySave(record, out var saveError), saveError);
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
            process.StartInfo.Environment["Persistence__AutoMigrate"] = "false";
            process.StartInfo.Environment["ConnectionStrings__SqlServer"] = "Server=localhost;Database=icp_platform_route_tests;Trusted_Connection=True;TrustServerCertificate=True;";

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
