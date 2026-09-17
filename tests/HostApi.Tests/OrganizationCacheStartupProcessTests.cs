using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Persistence.EfCore;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ICP.Platform.Testing;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class HostAuthenticationProcessRestartTests
{
    [EnvironmentGatedFact(RunEnvironmentVariable, "正式Host初始缓存校验失败保留健康观察并自动恢复")]
    public async Task InitialCacheFailure_RealHostKeepsHealthAndRetriesUntilValidated()
    {
        var catalog = $"NewPCache214_{Guid.NewGuid():N}";
        var root = Path.Combine(Path.GetTempPath(), $"newp-cache214-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var admin = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("NEWP_ORGANIZATION_SQLSERVER_ADMIN_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true") { InitialCatalog = "master" }.ConnectionString;
        var database = new SqlConnectionStringBuilder(admin) { InitialCatalog = catalog }.ConnectionString;
        var port = ReservePort();
        Process? process = null;
        await CreateCatalogAsync(admin, catalog);
        try
        {
            process = StartHost(root, Path.Combine(root, "keys"), Path.Combine(root, "plugins"), database, port, "migration");
            await WaitForHostAsync(process, port);
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            using var bootstrapResponse = await client.GetAsync("/api/health/ready");
            var bootstrap = await bootstrapResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("IDENTITY_BOOTSTRAP_REQUIRED", bootstrap.GetProperty("failureCode").GetString());
            StopExactProcess(process); process = null;
            await BootstrapAdministratorAsync(database);

            // 只在归属明确的一次性库注入组织关系损坏；正式迁移已成功，无产品测试开关。
            var options = new DbContextOptionsBuilder<EfPlatformDbContext>().UseSqlServer(database)
                .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options;
            await using (var db = new EfPlatformDbContext(options, OrganizationModelDefinitions.All.Select(m => m with { OwnerModuleId = "Organization" }).ToArray()))
            {
                db.Set<Dictionary<string, object?>>("Organization/Company").Add(new()
                { ["EID"] = "company", ["ECODE"] = "CACHE", ["ENAME"] = "启动故障公司", ["SortOrder"] = "01", ["IsEnabled"] = true });
                db.Set<Dictionary<string, object?>>("Organization/Department").Add(new()
                { ["EID"] = "department", ["ECODE"] = "CACHE", ["ENAME"] = "启动故障部门", ["SortOrder"] = "01", ["IsEnabled"] = true,
                    ["CompanyId"] = "company", ["ParentDepartmentId"] = "department" });
                await db.SaveChangesAsync();
            }
            process = StartHost(root, Path.Combine(root, "keys"), Path.Combine(root, "plugins"), database, port, "cache-failure");
            await WaitForHostAsync(process, port);
            using (var ready = await client.GetAsync("/api/health/ready"))
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
                var body = await ready.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("starting", body.GetProperty("status").GetString());
                Assert.Equal("CACHE_UNAVAILABLE", body.GetProperty("failureCode").GetString());
            }
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
            await BusinessAuditHostFixture.RefreshAntiforgeryAsync(client);
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { ECODE = "admin", Password = "restart-secret-123" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

            await using (var sql = new SqlConnection(database))
            {
                await sql.OpenAsync();
                await using var repair = sql.CreateCommand();
                repair.CommandText = "UPDATE [dbo].[Department] SET [ParentDepartmentId]=NULL WHERE [EID]='department'";
                Assert.Equal(1, await repair.ExecuteNonQueryAsync());
            }
            using var health = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = client.BaseAddress };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true)
            {
                using var ready = await health.GetAsync("/api/health/ready", timeout.Token);
                if (ready.IsSuccessStatusCode) break;
                Assert.False(process.HasExited);
                await Task.Delay(100, timeout.Token);
            }
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/current-user")).StatusCode);
        }
        finally
        {
            if (process is not null) StopExactProcess(process);
            var evidence = Path.Combine(FindRepositoryRoot(), ".codex-temp", "issue214-startup-process");
            Directory.CreateDirectory(evidence);
            foreach (var log in Directory.GetFiles(root, "*.log")) File.Copy(log, Path.Combine(evidence, Path.GetFileName(log)), true);
            SqlConnection.ClearAllPools();
            await DropCatalogAsync(admin, catalog);
            Directory.Delete(root, recursive: true);
        }
    }
}
