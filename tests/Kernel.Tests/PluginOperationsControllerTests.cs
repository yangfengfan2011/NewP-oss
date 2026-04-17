using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ICP.Platform.HostApi.PluginOperations;
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ICP.Platform.Modules.Audit.Services;
using ICP.Platform.Modules.Audit.DTOs;
using ICP.Platform.Modules.Audit.Models;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class PluginOperationsControllerTests : IClassFixture<PluginOperationsControllerTests.TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PluginOperationsControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetModules_Returns401_WhenNotAuthenticated()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync("/api/plugin-ops/modules");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetModule_Returns401_WhenNotAuthenticated()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync("/api/plugin-ops/modules/SomeModule");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetModules_Returns403_WhenAuthenticatedWithoutAdminRole()
    {
        using var client = await CreateAuthenticatedClientAsync(roles: new List<string> { "Viewer" });
        var response = await client.GetAsync("/api/plugin-ops/modules");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetModules_ReturnsInstalledModules_WhenAdmin()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/plugin-ops/modules");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var modules = doc.RootElement.EnumerateArray().ToList();
        Assert.Contains(modules, m => m.GetProperty("moduleId").GetString() == "Audit");
    }

    [Fact]
    public async Task GetModule_ReturnsDetail_WhenModuleExists()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/plugin-ops/modules/Audit");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var dto = doc.RootElement;
        Assert.Equal("Audit", dto.GetProperty("moduleId").GetString());
        Assert.Equal("Audit", dto.GetProperty("displayName").GetString());
        Assert.Equal("Enabled", dto.GetProperty("status").GetString());
        Assert.True(dto.GetProperty("canEnable").GetBoolean());
        Assert.True(dto.GetProperty("lastOperation").ValueKind == JsonValueKind.Null);
        Assert.True(dto.GetProperty("lastError").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task GetModule_Returns404_WhenModuleDoesNotExist()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/plugin-ops/modules/NonExistent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Lifecycle Operations ─────────────────────────────────────────────────

    [Fact]
    public async Task Install_Returns401_WhenNotAuthenticated()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.PostAsync("/api/plugin-ops/uploads/test-upload-id/install", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Install_Returns403_WhenNotAdmin()
    {
        using var client = await CreateAuthenticatedClientAsync(roles: new List<string> { "Viewer" });
        var response = await client.PostAsync("/api/plugin-ops/uploads/test-upload-id/install", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Install_Returns400_WhenUploadIdInvalid()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsync("/api/plugin-ops/uploads/nonexistent/install", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Install_Returns200_WhenValidUpload()
    {
        using var client = await CreateAuthenticatedClientAsync();
        // First upload a valid package to get an uploadId
        var packageDir = _factory.CreateValidTestPackage("Audit", "2.0.0");
        var zipStream = CreateZipFromDirectory(packageDir);
        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new StreamContent(zipStream), "file", "audit-2.0.0.zip");
        var uploadResponse = await client.PostAsync("/api/plugin-ops/uploads/verify", uploadContent);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        var uploadJson = await uploadResponse.Content.ReadAsStringAsync();
        using var uploadDoc = JsonDocument.Parse(uploadJson);
        var uploadId = uploadDoc.RootElement.GetProperty("uploadId").GetString()!;

        var response = await client.PostAsync($"/api/plugin-ops/uploads/{uploadId}/install", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Enable_Returns401_WhenNotAuthenticated()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.PostAsJsonAsync("/api/plugin-ops/modules/Audit/enable", new { version = "1.0.0" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Enable_Returns200_WhenModuleInstalled()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/plugin-ops/modules/Audit/enable", new { version = "1.0.0" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Enable_Returns400_WhenModuleNotInstalled()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsJsonAsync("/api/plugin-ops/modules/NonExistent/enable", new { version = "1.0.0" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Enable_Returns400_WhenRuntimeViewMaterializerRefreshFails()
    {
        var armFailure = false;
        using var factory = new TestWebApplicationFactory
        {
            RuntimeViewMaterializerFactory = stateStore =>
                CreateMaterializerWithConditionalSwapFailure(stateStore, () => armFailure, 4, "Simulated public swap failure")
        };
        using var client = await CreateAuthenticatedClientAsync(factory);

        await InstallModuleViaApiAsync(factory, client, "Audit", "2.0.0");

        armFailure = true;
        var response = await client.PostAsJsonAsync("/api/plugin-ops/modules/Audit/enable", new { version = "2.0.0" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var dto = doc.RootElement;
        Assert.Equal("Failed", dto.GetProperty("status").GetString());
        Assert.Contains("restore", dto.GetProperty("message").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var moduleResponse = await client.GetAsync("/api/plugin-ops/modules/Audit");
        Assert.Equal(HttpStatusCode.OK, moduleResponse.StatusCode);
        var moduleJson = await moduleResponse.Content.ReadAsStringAsync();
        using var moduleDoc = JsonDocument.Parse(moduleJson);
        var moduleDto = moduleDoc.RootElement;
        Assert.Equal("Enabled", moduleDto.GetProperty("status").GetString());
        Assert.Equal("1.0.0", moduleDto.GetProperty("currentEnabledVersion").GetString());
    }

    [Fact]
    public async Task Disable_Returns401_WhenNotAuthenticated()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.PostAsync("/api/plugin-ops/modules/Audit/disable", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Disable_Returns200_WhenModuleInstalled()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsync("/api/plugin-ops/modules/Audit/disable", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Re-enable to restore shared fixture state
        await client.PostAsJsonAsync("/api/plugin-ops/modules/Audit/enable", new { version = "1.0.0" });
    }

    [Fact]
    public async Task Disable_Returns400_WhenModuleNotInstalled()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsync("/api/plugin-ops/modules/NonExistent/disable", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Disable_Returns400_WhenRuntimeViewMaterializerRefreshFails()
    {
        var armFailure = false;
        using var factory = new TestWebApplicationFactory
        {
            RuntimeViewMaterializerFactory = stateStore =>
                CreateMaterializerWithConditionalSwapFailure(stateStore, () => armFailure, 4, "Simulated public swap failure")
        };
        using var client = await CreateAuthenticatedClientAsync(factory);

        armFailure = true;
        var response = await client.PostAsync("/api/plugin-ops/modules/Audit/disable", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var dto = doc.RootElement;
        Assert.Equal("Failed", dto.GetProperty("status").GetString());
        Assert.Contains("restore", dto.GetProperty("message").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var moduleResponse = await client.GetAsync("/api/plugin-ops/modules/Audit");
        Assert.Equal(HttpStatusCode.OK, moduleResponse.StatusCode);
        var moduleJson = await moduleResponse.Content.ReadAsStringAsync();
        using var moduleDoc = JsonDocument.Parse(moduleJson);
        var moduleDto = moduleDoc.RootElement;
        Assert.Equal("Enabled", moduleDto.GetProperty("status").GetString());
        Assert.Equal("1.0.0", moduleDto.GetProperty("currentEnabledVersion").GetString());
    }

    [Fact]
    public async Task Delete_Returns401_WhenNotAuthenticated()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.DeleteAsync("/api/plugin-ops/modules/Audit");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Returns200_WhenModuleIsInstalled()
    {
        using var client = await CreateAuthenticatedClientAsync();
        await InstallModuleViaApiAsync(client, "Cleanup", "1.0.0");

        var response = await client.DeleteAsync("/api/plugin-ops/modules/Cleanup");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var dto = doc.RootElement;
        Assert.Equal("Cleanup", dto.GetProperty("moduleId").GetString());
        Assert.Equal("Uninstalled", dto.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Delete_Returns400_WhenModuleIsEnabled()
    {
        using var client = await CreateAuthenticatedClientAsync();
        await InstallModuleViaApiAsync(client, "Guarded", "1.0.0");

        var enableResponse = await client.PostAsJsonAsync("/api/plugin-ops/modules/Guarded/enable", new { version = "1.0.0" });
        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        var response = await client.DeleteAsync("/api/plugin-ops/modules/Guarded");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Returns400_WhenRuntimeViewMaterializerRefreshFails()
    {
        var armFailure = false;
        using var factory = new TestWebApplicationFactory
        {
            RuntimeViewMaterializerFactory = stateStore =>
                CreateMaterializerWithConditionalSwapFailure(stateStore, () => armFailure, 4, "Simulated public swap failure")
        };
        using var client = await CreateAuthenticatedClientAsync(factory);

        await InstallModuleViaApiAsync(factory, client, "Cleanup", "1.0.0");

        armFailure = true;
        var response = await client.DeleteAsync("/api/plugin-ops/modules/Cleanup");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var dto = doc.RootElement;
        Assert.Equal("Failed", dto.GetProperty("status").GetString());
        Assert.Contains("restore", dto.GetProperty("message").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var moduleResponse = await client.GetAsync("/api/plugin-ops/modules/Cleanup");
        Assert.Equal(HttpStatusCode.OK, moduleResponse.StatusCode);
        var moduleJson = await moduleResponse.Content.ReadAsStringAsync();
        using var moduleDoc = JsonDocument.Parse(moduleJson);
        var moduleDto = moduleDoc.RootElement;
        Assert.Equal("Installed", moduleDto.GetProperty("status").GetString());
        Assert.Null(moduleDto.GetProperty("currentEnabledVersion").GetString());
        var installedVersions = moduleDto.GetProperty("installedVersions")
            .EnumerateArray()
            .Select(v => v.GetString())
            .ToList();
        Assert.Contains("1.0.0", installedVersions);
    }

    [Fact]
    public async Task Rollback_Returns401_WhenNotAuthenticated()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.PostAsync("/api/plugin-ops/modules/Audit/rollback", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rollback_Returns400_WhenNoPreviousVersion()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsync("/api/plugin-ops/modules/Audit/rollback", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rollback_Returns200_WhenPreviousVersionExists()
    {
        using var client = await CreateAuthenticatedClientAsync();

        // Install Audit 2.0.0 via upload+install flow
        var packageDir = _factory.CreateValidTestPackage("Audit", "2.0.0");
        var zipStream = CreateZipFromDirectory(packageDir);
        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new StreamContent(zipStream), "file", "audit-2.0.0.zip");
        var uploadResponse = await client.PostAsync("/api/plugin-ops/uploads/verify", uploadContent);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        var uploadJson = await uploadResponse.Content.ReadAsStringAsync();
        using var uploadDoc = JsonDocument.Parse(uploadJson);
        var uploadId = uploadDoc.RootElement.GetProperty("uploadId").GetString()!;

        var installResponse = await client.PostAsync($"/api/plugin-ops/uploads/{uploadId}/install", null);
        Assert.Equal(HttpStatusCode.OK, installResponse.StatusCode);

        // Enable version 2.0.0 so there is a previous version (1.0.0) to roll back to
        var enableResponse = await client.PostAsJsonAsync("/api/plugin-ops/modules/Audit/enable", new { version = "2.0.0" });
        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        // Rollback should succeed: 2.0.0 → 1.0.0
        var response = await client.PostAsync("/api/plugin-ops/modules/Audit/rollback", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var dto = doc.RootElement;
        Assert.Equal("Enabled", dto.GetProperty("status").GetString());
        Assert.Equal("Audit", dto.GetProperty("moduleId").GetString());
        Assert.Equal("1.0.0", dto.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Rollback_Returns400_WhenRuntimeViewMaterializerRefreshFails()
    {
        var armFailure = false;
        using var factory = new TestWebApplicationFactory
        {
            RuntimeViewMaterializerFactory = stateStore =>
                CreateMaterializerWithConditionalSwapFailure(stateStore, () => armFailure, 1, "Simulated public swap failure")
        };
        using var client = await CreateAuthenticatedClientAsync(factory);

        await InstallModuleViaApiAsync(factory, client, "Audit", "2.0.0");

        var enableResponse = await client.PostAsJsonAsync("/api/plugin-ops/modules/Audit/enable", new { version = "2.0.0" });
        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);

        armFailure = true;
        var response = await client.PostAsync("/api/plugin-ops/modules/Audit/rollback", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var dto = doc.RootElement;
        Assert.Equal("Failed", dto.GetProperty("status").GetString());
        Assert.Contains("restore", dto.GetProperty("message").GetString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var moduleResponse = await client.GetAsync("/api/plugin-ops/modules/Audit");
        Assert.Equal(HttpStatusCode.OK, moduleResponse.StatusCode);
        var moduleJson = await moduleResponse.Content.ReadAsStringAsync();
        using var moduleDoc = JsonDocument.Parse(moduleJson);
        var moduleDto = moduleDoc.RootElement;
        Assert.Equal("Enabled", moduleDto.GetProperty("status").GetString());
        Assert.Equal("2.0.0", moduleDto.GetProperty("currentEnabledVersion").GetString());
        var installedVersions = moduleDto.GetProperty("installedVersions")
            .EnumerateArray()
            .Select(v => v.GetString())
            .ToList();
        Assert.Contains("1.0.0", installedVersions);
        Assert.Contains("2.0.0", installedVersions);
    }

    // ── Upload Verify ───────────────────────────────────────────────────

    [Fact]
    public async Task UploadVerify_Returns401_WhenNotAuthenticated()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var content = new MultipartFormDataContent();
        using var emptyStream = new MemoryStream();
        content.Add(new StreamContent(emptyStream), "file", "test.zip");
        var response = await client.PostAsync("/api/plugin-ops/uploads/verify", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadVerify_Returns200_WhenValidZipUploaded()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var packageDir = _factory.CreateValidTestPackage("Audit", "1.0.0");
        var zipStream = CreateZipFromDirectory(packageDir);
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(zipStream), "file", "audit-1.0.0.zip");
        var response = await client.PostAsync("/api/plugin-ops/uploads/verify", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var dto = doc.RootElement;
        Assert.True(dto.GetProperty("isValid").GetBoolean());
        Assert.Equal("Audit", dto.GetProperty("moduleId").GetString());
        Assert.Equal("1.0.0", dto.GetProperty("version").GetString());
        Assert.NotEmpty(dto.GetProperty("uploadId").GetString()!);
    }

    [Fact]
    public async Task UploadVerify_Returns400_WhenInvalidZipUploaded()
    {
        using var client = await CreateAuthenticatedClientAsync();
        using var content = new MultipartFormDataContent();
        var invalidBytes = System.Text.Encoding.UTF8.GetBytes("this is not a zip file");
        content.Add(new ByteArrayContent(invalidBytes), "file", "bad.zip");
        var response = await client.PostAsync("/api/plugin-ops/uploads/verify", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var dto = doc.RootElement;
        Assert.False(dto.GetProperty("isValid").GetBoolean());
        Assert.NotEmpty(dto.GetProperty("errors").EnumerateArray().ToList());
    }

    private static MemoryStream CreateZipFromDirectory(string directoryPath)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(directoryPath, file);
                var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(file);
                fileStream.CopyTo(entryStream);
            }
        }
        ms.Position = 0;
        return ms;
    }

    private async Task InstallModuleViaApiAsync(HttpClient client, string moduleId, string version)
    {
        var packageDir = _factory.CreateValidTestPackage(moduleId, version);
        var zipStream = CreateZipFromDirectory(packageDir);
        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new StreamContent(zipStream), "file", $"{moduleId}-{version}.zip");
        var uploadResponse = await client.PostAsync("/api/plugin-ops/uploads/verify", uploadContent);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        var uploadJson = await uploadResponse.Content.ReadAsStringAsync();
        using var uploadDoc = JsonDocument.Parse(uploadJson);
        var uploadId = uploadDoc.RootElement.GetProperty("uploadId").GetString()!;

        var installResponse = await client.PostAsync($"/api/plugin-ops/uploads/{uploadId}/install", null);
        Assert.Equal(HttpStatusCode.OK, installResponse.StatusCode);
    }

    private async Task InstallModuleViaApiAsync(TestWebApplicationFactory factory, HttpClient client, string moduleId, string version)
    {
        var packageDir = factory.CreateValidTestPackage(moduleId, version);
        var zipStream = CreateZipFromDirectory(packageDir);
        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new StreamContent(zipStream), "file", $"{moduleId}-{version}.zip");
        var uploadResponse = await client.PostAsync("/api/plugin-ops/uploads/verify", uploadContent);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        var uploadJson = await uploadResponse.Content.ReadAsStringAsync();
        using var uploadDoc = JsonDocument.Parse(uploadJson);
        var uploadId = uploadDoc.RootElement.GetProperty("uploadId").GetString()!;

        var installResponse = await client.PostAsync($"/api/plugin-ops/uploads/{uploadId}/install", null);
        Assert.Equal(HttpStatusCode.OK, installResponse.StatusCode);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(IReadOnlyList<string>? roles = null)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        _factory.StubRoles = roles ?? new List<string> { "LocalAdmin" };
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Username = "admin",
            Password = "admin123"
        });
        Assert.True(loginResponse.IsSuccessStatusCode,
            string.Format("Login failed with status {0}: {1}", loginResponse.StatusCode, await loginResponse.Content.ReadAsStringAsync()));
        return client;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(TestWebApplicationFactory factory, IReadOnlyList<string>? roles = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        factory.StubRoles = roles ?? new List<string> { "LocalAdmin" };
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Username = "admin",
            Password = "admin123"
        });
        Assert.True(loginResponse.IsSuccessStatusCode,
            string.Format("Login failed with status {0}: {1}", loginResponse.StatusCode, await loginResponse.Content.ReadAsStringAsync()));
        return client;
    }

    public class TestWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
    {
        private readonly UnifiedPluginPackageTestData _testData = new();
        private readonly string _installRoot;

        public TestWebApplicationFactory()
        {
            _installRoot = Path.Combine(Path.GetTempPath(), "plugin_ops_ctrl_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_installRoot);
            InstallTestPackages();
        }

        /// <summary>
        /// Controls what roles the StubAuthorizationService returns for the current test.
        /// </summary>
        internal IReadOnlyList<string> StubRoles { get; set; } = new List<string> { "LocalAdmin" };

        /// <summary>
        /// Creates a valid plugin package directory for use in upload tests.
        /// </summary>
        internal string CreateValidTestPackage(string moduleId, string version)
        {
            return _testData.CreateValidPackage(moduleId, version);
        }

        internal Func<InstalledPluginStateStore, RuntimeViewMaterializer>? RuntimeViewMaterializerFactory { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Platform:PluginInstallRoot", _installRoot);
            builder.UseSetting("Platform:SkipLocalAuthSeeding", "true");
            builder.UseSetting("Persistence:AutoMigrate", "false");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
            builder.ConfigureServices(services =>
            {
                var seederDesc = services.FirstOrDefault(d =>
                    d.ServiceType == typeof(ICP.Platform.HostApi.Authentication.LocalAuthSeeder));
                if (seederDesc is not null) services.Remove(seederDesc);
                services.AddSingleton<IAuthorizationService>(_ =>
                    new StubAuthorizationService(() => StubRoles));
                services.TryAddSingleton<IPermissionService, StubPermissionService>();
                services.TryAddSingleton<IRoleService, StubRoleService>();
                services.TryAddSingleton<IAuditService, StubAuditService>();
                services.AddSingleton(sp =>
                    new PluginUploadService(
                        sp.GetRequiredService<InstalledPluginStateStore>(),
                        _installRoot));
                services.AddSingleton<UnifiedPluginPackageInstaller>();
                services.AddSingleton<UnifiedPluginPackageOperations>();
                services.AddSingleton(sp =>
                    RuntimeViewMaterializerFactory?.Invoke(sp.GetRequiredService<InstalledPluginStateStore>())
                    ?? new RuntimeViewMaterializer(sp.GetRequiredService<InstalledPluginStateStore>()));
                services.AddSingleton(sp =>
                    new PluginOperationsLifecycleService(
                        sp.GetRequiredService<InstalledPluginStateStore>(),
                        sp.GetRequiredService<UnifiedPluginPackageInstaller>(),
                        sp.GetRequiredService<UnifiedPluginPackageOperations>(),
                        sp.GetRequiredService<RuntimeViewMaterializer>(),
                        sp.GetRequiredService<PluginUploadService>(),
                        _installRoot));
            });
        }

        private void InstallTestPackages()
        {
            var stateStore = new InstalledPluginStateStore(_installRoot);
            var installer = new UnifiedPluginPackageInstaller(stateStore);
            var auditRoot = _testData.CreateValidPackage("Audit", "1.0.0");
            var result = installer.Install(auditRoot, _installRoot);
            Assert.Equal("Installed", result.Status);
            Assert.True(stateStore.TryLoad("Audit", out var record, out var loadError));
            Assert.Null(loadError);
            Assert.NotNull(record);
            record!.CurrentEnabledVersion = "1.0.0";
            Assert.True(stateStore.TrySave(record, out var saveError));
            Assert.Null(saveError);
        }

        public new void Dispose()
        {
            _testData.Dispose();
            if (Directory.Exists(_installRoot))
                Directory.Delete(_installRoot, recursive: true);
            base.Dispose();
        }
    }

    private static RuntimeViewMaterializer CreateMaterializerWithSwapFailure(InstalledPluginStateStore stateStore, int failOnCallNumber, string message)
    {
        var constructor = typeof(RuntimeViewMaterializer).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(InstalledPluginStateStore), typeof(FrontendRuntimeManifestBuilder), typeof(Action<string, string>)],
            modifiers: null);

        Assert.NotNull(constructor);

        var callCount = 0;
        return (RuntimeViewMaterializer)constructor!.Invoke(
            [
                stateStore,
                new FrontendRuntimeManifestBuilder(),
                (Action<string, string>)((source, destination) =>
                {
                    callCount++;
                    if (callCount == failOnCallNumber)
                    {
                        throw new IOException(message);
                    }

                    Directory.Move(source, destination);
                })
            ]);
    }

    private static RuntimeViewMaterializer CreateMaterializerWithConditionalSwapFailure(
        InstalledPluginStateStore stateStore,
        Func<bool> shouldFail,
        int failOnCallNumber,
        string message)
    {
        var constructor = typeof(RuntimeViewMaterializer).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(InstalledPluginStateStore), typeof(FrontendRuntimeManifestBuilder), typeof(Action<string, string>)],
            modifiers: null);

        Assert.NotNull(constructor);

        var callCount = 0;
        return (RuntimeViewMaterializer)constructor!.Invoke(
            [
                stateStore,
                new FrontendRuntimeManifestBuilder(),
                (Action<string, string>)((source, destination) =>
                {
                    if (shouldFail())
                    {
                        callCount++;
                        if (callCount == failOnCallNumber)
                        {
                            throw new IOException(message);
                        }
                    }

                    Directory.Move(source, destination);
                })
            ]);
    }

    private class StubAuthorizationService : IAuthorizationService
    {
        private readonly Func<IReadOnlyList<string>> _rolesProvider;

        public StubAuthorizationService(Func<IReadOnlyList<string>> rolesProvider)
        {
            _rolesProvider = rolesProvider;
        }

        public Task<bool> HasPermissionAsync(string userId, string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> HasAnyPermissionAsync(string userId, IEnumerable<string> permissionCodes, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> HasAllPermissionsAsync(string userId, IEnumerable<string> permissionCodes, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<List<string>> GetUserPermissionsAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(new List<string> { "test:read" });
        public Task<List<string>> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(_rolesProvider().ToList());
    }

    private class StubPermissionService : IPermissionService
    {
        public Task<List<PermissionDto>> GetAllPermissionsAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<PermissionDto>());
        public Task<List<PermissionGroupDto>> GetPermissionsByModuleAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<PermissionGroupDto>());
        public Task SyncPermissionsAsync(IEnumerable<PermissionDescriptor> descriptors, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class StubRoleService : IRoleService
    {
        public Task<List<RoleDto>> GetAllRolesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new List<RoleDto>());
        public Task<RoleDto?> GetRoleByIdAsync(string roleId, CancellationToken cancellationToken = default) => Task.FromResult<RoleDto?>(null);
        public Task<RoleDto?> GetRoleByCodeAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult<RoleDto?>(null);
        public Task<RoleDto> CreateRoleAsync(RoleDto dto, string? createdBy = null, CancellationToken cancellationToken = default) => Task.FromResult(new RoleDto { Id = "stub", Code = dto.Code, Name = dto.Name });
        public Task<RoleDto> UpdateRoleAsync(string roleId, RoleDto dto, string? updatedBy = null, CancellationToken cancellationToken = default) => Task.FromResult(new RoleDto { Id = roleId, Code = "stub", Name = dto.Name });
        public Task DeleteRoleAsync(string roleId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<RolePermissionsDto> GetRolePermissionsAsync(string roleId, CancellationToken cancellationToken = default) => Task.FromResult(new RolePermissionsDto { RoleId = roleId, PermissionCodes = new List<string>() });
        public Task AssignRolePermissionsAsync(string roleId, AssignRolePermissionsRequest request, string? grantedBy = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<UserRoleDto> GetUserRolesAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(new UserRoleDto { UserId = userId, RoleCodes = new List<string>() });
        public Task AssignUserRolesAsync(string userId, AssignUserRolesRequest request, string? assignedBy = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>
    /// Stub 审计服务：满足 AuditMiddleware 的 DI 依赖。
    /// </summary>
    private class StubAuditService : IAuditService
    {
        public Task<long> LogOperationAsync(CreateOperationLogRequest request, string? userId, string? userName, string? clientIp) => Task.FromResult(0L);
        public Task<long> LogAccessAsync(AccessLog accessLog) => Task.FromResult(0L);
        public Task<(List<OperationLogDto> Items, int Total)> QueryOperationLogsAsync(QueryOperationLogRequest request) => Task.FromResult((new List<OperationLogDto>(), 0));
        public Task<(List<AccessLogDto> Items, int Total)> QueryAccessLogsAsync(QueryAccessLogRequest request) => Task.FromResult((new List<AccessLogDto>(), 0));
    }
}
