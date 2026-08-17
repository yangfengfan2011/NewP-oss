using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ICP.Platform.Kernel.PluginPackages;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class HostApiPluginManifestRouteTests : IDisposable
{
    private readonly UnifiedPluginPackageTestData _testData = new();
    private readonly List<string> _tempRoots = new();
    private readonly SqlServerTestCatalog _database = SqlServerTestCatalog.Create();

    [Fact]
    public async Task HostApiStarts_WhenInstalledPluginStateIsMissing_UsingBuiltInModuleFallback()
    {
        var installRoot = CreateTempRoot();

        await using var host = await StartHostAsync(installRoot);

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

        await using var host = await StartHostAsync(installRoot);

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

        await using var host = await StartHostAsync(installRoot);

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
    public async Task ActiveModuleRuntimeCatalog_WhenUsingBuiltIns_ProjectsOnlySupportedFrontendBindings()
    {
        var installRoot = CreateTempRoot();

        await using var host = await StartHostAsync(installRoot);

        using var client = new HttpClient
        {
            BaseAddress = new Uri(host.BaseAddress),
            Timeout = TimeSpan.FromSeconds(2)
        };

        var response = await client.GetAsync("/plugins/active-module-runtime.json");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.IsSuccessStatusCode,
            $"Status: {response.StatusCode}\nBody: {body}\nSTDOUT:\n{host.StdOut}\nSTDERR:\n{host.StdErr}");

        using var document = JsonDocument.Parse(body);
        var projectedBindings = document.RootElement
            .GetProperty("modules")
            .EnumerateArray()
            .Where(module => module.TryGetProperty("frontend", out var frontend)
                && frontend.ValueKind == JsonValueKind.Object)
            .SelectMany(module => module
                .GetProperty("frontend")
                .GetProperty("bindings")
                .EnumerateArray()
                .Select(binding => binding.GetString()))
            .Where(binding => binding is not null)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var expectedBindings = new[]
        {
            "identity.account.detail",
            "identity.account.list",
            "identity.accounts.page",
            "organization.person.detail",
            "organization.person.list"
        };
        Assert.True(
            expectedBindings.SequenceEqual(projectedBindings, StringComparer.Ordinal),
            $"Expected bindings: [{string.Join(", ", expectedBindings)}]\n"
            + $"Projected bindings: [{string.Join(", ", projectedBindings)}]");
    }

    [Fact]
    public async Task ActiveModuleRuntimeCatalog_WhenUsingBuiltIns_ExposesOnlySupportedFrontendModules()
    {
        var installRoot = CreateTempRoot();

        await using var host = await StartHostAsync(installRoot);

        using var client = new HttpClient
        {
            BaseAddress = new Uri(host.BaseAddress),
            Timeout = TimeSpan.FromSeconds(2)
        };

        var response = await client.GetAsync("/plugins/active-module-runtime.json");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.IsSuccessStatusCode,
            $"Status: {response.StatusCode}\nBody: {body}\nSTDOUT:\n{host.StdOut}\nSTDERR:\n{host.StdErr}");

        using var document = JsonDocument.Parse(body);
        var modules = document.RootElement
            .GetProperty("modules")
            .EnumerateArray()
            .ToArray();
        var frontendModuleIds = modules
            .Where(module => module.TryGetProperty("frontend", out var frontend)
                && frontend.ValueKind == JsonValueKind.Object)
            .Select(module => module.GetProperty("moduleId").GetString())
            .Where(moduleId => moduleId is not null)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var expectedModuleIds = new[] { "Identity", "Organization" };
        Assert.True(
            expectedModuleIds.SequenceEqual(frontendModuleIds, StringComparer.Ordinal),
            $"Expected frontend modules: [{string.Join(", ", expectedModuleIds)}]\n"
            + $"Projected frontend modules: [{string.Join(", ", frontendModuleIds)}]");

        var retiredFrontendModuleIds = new[]
        {
            "Audit",
            "Authorization",
            "ConfigCenter",
            "Demo",
            "Files",
            "MetadataConfigCenter",
            "ModuleTemplateSample",
            "PluginOps"
        };
        foreach (var moduleId in retiredFrontendModuleIds)
        {
            var module = Assert.Single(modules, candidate =>
                candidate.GetProperty("moduleId").GetString() == moduleId);
            Assert.Equal(JsonValueKind.Null, module.GetProperty("frontend").ValueKind);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task ActiveModuleRuntimeCatalog_WhenExternalPluginsDisabled_UsesBuiltInsOnly(
        bool? externalPluginsEnabled)
    {
        var installRoot = CreateTempRoot();
        var packageRoot = _testData.CreateValidPackage();
        EnableInstalledPackage(packageRoot, installRoot, "Audit");

        await using var host = await StartHostAsync(
            installRoot,
            externalPluginsEnabled: externalPluginsEnabled);

        using var client = new HttpClient
        {
            BaseAddress = new Uri(host.BaseAddress),
            Timeout = TimeSpan.FromSeconds(2)
        };

        var response = await client.GetAsync("/plugins/active-module-runtime.json");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.IsSuccessStatusCode,
            $"Status: {response.StatusCode}\nBody: {body}\nSTDOUT:\n{host.StdOut}\nSTDERR:\n{host.StdErr}");

        using var document = JsonDocument.Parse(body);
        var modules = document.RootElement.GetProperty("modules").EnumerateArray().ToArray();
        var audit = Assert.Single(modules, module =>
            module.GetProperty("moduleId").GetString() == "Audit");

        var auditFrontend = audit.GetProperty("frontend");
        Assert.Equal(JsonValueKind.Null, auditFrontend.ValueKind);
        Assert.Contains(modules, module => module.GetProperty("moduleId").GetString() == "Organization");
        Assert.DoesNotContain(modules, module => module.GetProperty("moduleId").GetString() == "PluginOps");
    }

    [Fact]
    public async Task ExternalPluginLifecycle_WhenDisabledThenReenabled_RestoresExistingActiveState()
    {
        var installRoot = CreateTempRoot();
        var packageRoot = _testData.CreateValidPackage();
        EnableInstalledPackage(packageRoot, installRoot, "Audit");

        string externalFrontendUrl;
        await using (var enabledHost = await StartHostAsync(
                         installRoot,
                         externalPluginsEnabled: true))
        {
            var audit = await ReadActiveModuleAsync(enabledHost, "Audit");
            externalFrontendUrl = audit.GetProperty("frontend").GetProperty("url").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(externalFrontendUrl));
        }

        var externalStateBeforeDisabledStartup = SnapshotExternalPluginState(installRoot);
        await using (var disabledHost = await StartHostAsync(
                         installRoot,
                         externalPluginsEnabled: false))
        {
            var audit = await ReadActiveModuleAsync(disabledHost, "Audit");
            Assert.Equal(JsonValueKind.Null, audit.GetProperty("frontend").ValueKind);
        }
        AssertExternalPluginStateUnchanged(
            externalStateBeforeDisabledStartup,
            SnapshotExternalPluginState(installRoot));

        await using (var reenabledHost = await StartHostAsync(
                         installRoot,
                         externalPluginsEnabled: true))
        {
            var audit = await ReadActiveModuleAsync(reenabledHost, "Audit");
            Assert.Equal(externalFrontendUrl, audit.GetProperty("frontend").GetProperty("url").GetString());
        }
    }

    private static IReadOnlyDictionary<string, PersistedFileSnapshot> SnapshotExternalPluginState(
        string installRoot)
    {
        var snapshot = new Dictionary<string, PersistedFileSnapshot>(StringComparer.Ordinal);
        foreach (var rootName in new[] { "packages", "state", "lifecycle" })
        {
            var root = Path.Combine(installRoot, rootName);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal))
            {
                var relativePath = Path.GetRelativePath(installRoot, path)
                    .Replace(Path.DirectorySeparatorChar, '/');
                snapshot.Add(
                    relativePath,
                    new PersistedFileSnapshot(
                        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
                        File.GetLastWriteTimeUtc(path)));
            }
        }

        return snapshot;
    }

    private static void AssertExternalPluginStateUnchanged(
        IReadOnlyDictionary<string, PersistedFileSnapshot> expected,
        IReadOnlyDictionary<string, PersistedFileSnapshot> actual)
    {
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, expectedFile) in expected)
        {
            Assert.Equal(expectedFile, actual[path]);
        }
    }

    private sealed record PersistedFileSnapshot(string Sha256, DateTime LastWriteTimeUtc);

    [Fact]
    public async Task LocalizationManifestRoute_ReturnsJson()
    {
        var installRoot = CreateTempRoot();
        var packageRoot = _testData.CreateValidPackage();
        EnableInstalledPackage(packageRoot, installRoot, "Audit");

        await using var host = await StartHostAsync(installRoot);

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

        await using var host = await StartHostAsync(installRoot);

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

        await using var host = await StartHostAsync(installRoot);

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

        await using var host = await StartHostAsync(installRoot);

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

        _database.Dispose();
    }

    private Task<HostApiProcess> StartHostAsync(
        string installRoot,
        bool skipLocalAuthSeeding = true,
        bool? externalPluginsEnabled = true) =>
        HostApiProcess.StartAsync(
            installRoot,
            _database.ConnectionString,
            skipLocalAuthSeeding,
            externalPluginsEnabled);

    private string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"host_api_plugin_route_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);
        return root;
    }

    private static async Task<JsonElement> ReadActiveModuleAsync(HostApiProcess host, string moduleId)
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri(host.BaseAddress),
            Timeout = TimeSpan.FromSeconds(2)
        };
        var response = await client.GetAsync("/plugins/active-module-runtime.json");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.IsSuccessStatusCode,
            $"Status: {response.StatusCode}\nBody: {body}\nSTDOUT:\n{host.StdOut}\nSTDERR:\n{host.StdErr}");

        using var document = JsonDocument.Parse(body);
        return Assert.Single(
                document.RootElement.GetProperty("modules").EnumerateArray(),
                module => module.GetProperty("moduleId").GetString() == moduleId)
            .Clone();
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

        public static async Task<HostApiProcess> StartAsync(
            string installRoot,
            string connectionString,
            bool skipLocalAuthSeeding = true,
            bool? externalPluginsEnabled = true)
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
            if (externalPluginsEnabled.HasValue)
            {
                process.StartInfo.Environment["Platform__ExternalPluginsEnabled"] =
                    externalPluginsEnabled.Value ? "true" : "false";
            }
            process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";
            process.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            process.StartInfo.Environment["Platform__SkipAuthorizationCatalogInitialization"] = skipLocalAuthSeeding ? "true" : "false";
            process.StartInfo.Environment["Authentication__Host__KeyRingPath"] = Path.Combine(installRoot, ".host-auth-key-ring");
            process.StartInfo.Environment["Authentication__Host__CookieName"] = "__Host-ICP.Platform.Auth";
            process.StartInfo.Environment["Authentication__Host__ProtectKeysAtRest"] = "true";
            process.StartInfo.Environment["ConnectionStrings__SqlServer"] = connectionString;

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

    private sealed class SqlServerTestCatalog : IDisposable
    {
        private const string CatalogPrefix = "newp_kernel_route_";
        private readonly string _adminConnectionString;
        private readonly string _catalog;

        private SqlServerTestCatalog(string adminConnectionString, string catalog)
        {
            _adminConnectionString = adminConnectionString;
            _catalog = catalog;
            ConnectionString = new SqlConnectionStringBuilder(adminConnectionString)
            {
                InitialCatalog = catalog
            }.ConnectionString;
        }

        public string ConnectionString { get; }

        public static SqlServerTestCatalog Create()
        {
            var catalog = CatalogPrefix + Guid.NewGuid().ToString("N");
            var adminConnectionString = new SqlConnectionStringBuilder
            {
                DataSource = "localhost",
                InitialCatalog = "master",
                IntegratedSecurity = true,
                TrustServerCertificate = true,
                ConnectTimeout = 15
            }.ConnectionString;
            using var connection = new SqlConnection(adminConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{catalog}];";
            command.ExecuteNonQuery();
            return new SqlServerTestCatalog(adminConnectionString, catalog);
        }

        public void Dispose()
        {
            if (!_catalog.StartsWith(CatalogPrefix, StringComparison.Ordinal)
                || _catalog.Length != CatalogPrefix.Length + 32)
            {
                throw new InvalidOperationException("Refusing to remove a catalog not owned by this test.");
            }

            using var connection = new SqlConnection(_adminConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_ID(N'{_catalog}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{_catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{_catalog}];
                END
                """;
            command.ExecuteNonQuery();
        }
    }
}
