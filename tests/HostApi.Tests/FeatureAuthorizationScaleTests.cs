using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.Authorization;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class FeatureAuthorizationScaleTests
{
    [EnvironmentGatedFact("NEWP_RUN_FEATURE_SCALE_INTEGRATION", "同一真实Host与SQL规模样本的实际计划、查询成本及生产Shell虚拟树观察")]
    public async Task RealHostAndSqlSample_MeasuresStartupDeletionDecisionSaveAndBrowser()
    {
        // 参数只缩小开发夹具；默认正式样本为新增5000点及100个普通角色，不构成产品容量限制。
        var extraPoints = PositiveEnvironment("NEWP_FEATURE_SCALE_POINTS", 5000);
        var ordinaryRoles = PositiveEnvironment("NEWP_FEATURE_SCALE_ROLES", 100);
        Assert.True(ordinaryRoles >= 21, "Sample needs a second role page.");
        var declarations = new JsonArray(Enumerable.Range(0, extraPoints)
            .Select(index => (JsonNode)FeatureCatalogHostLifecycleTests.Declaration($"scale-{index:D5}", "navigation")).ToArray());
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync(declarations);
        var candidate = await GitAsync(fixture.RepositoryRoot, "rev-parse", "HEAD");
        var http = new FeatureHttpTestRequests(fixture.Catalog);
        var positions = fixture.Catalog.Where(point => point.ModuleId == "Example" && point.Key.StartsWith("scale-", StringComparison.Ordinal)).Select(point => point.Position).ToArray();
        Assert.Equal(extraPoints, positions.Length);
        var roles = Enumerable.Range(0, ordinaryRoles).Select(index => new ScaleRole("Role_" + Guid.NewGuid().ToString("N"), $"scale-{index:D3}", $"规模角色 {index:D3}")).ToArray();
        await using (var db = fixture.CreateContext())
        {
            foreach (var role in roles) db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Role")).Add(new()
            {
                ["EID"] = role.Id, ["ECODE"] = role.Code, ["ENAME"] = role.Name, ["IsEnabled"] = true, ["IsBuiltIn"] = false
            });
            await db.SaveChangesAsync();
        }
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = new SqlCommand("""
                INSERT dbo.RoleFunctionGrant(EID,CreatedAt,CreatedBy,UpdatedAt,UpdatedBy,RoleId,FunctionPointId,GrantedAt,GrantedBy)
                SELECT 'RoleFunctionGrant_'+REPLACE(CONVERT(varchar(36),NEWID()),'-',''),SYSUTCDATETIME(),'scale-seed',SYSUTCDATETIME(),'scale-seed',r.EID,p.EID,SYSUTCDATETIME(),'scale-seed'
                FROM dbo.Role r CROSS JOIN dbo.FunctionPoint p
                WHERE r.EID IN (SELECT value FROM OPENJSON(@roles)) AND p.PositionKey IN (SELECT value FROM OPENJSON(@positions));
                """, connection) { CommandTimeout = 180 };
            seed.Parameters.AddWithValue("@roles", JsonSerializer.Serialize(roles.Select(role => role.Id)));
            seed.Parameters.AddWithValue("@positions", JsonSerializer.Serialize(positions));
            Assert.Equal(checked(extraPoints * ordinaryRoles), await seed.ExecuteNonQueryAsync());
        }
        var account = await FeatureHttpTestRequests.CreateAsync(fixture.Administrator, http.Identity("create"), "/api/identity/accounts", new { ECODE = "scale-user", ENAME = "规模授权账号" });
        var userId = account.GetProperty("account").GetProperty("eid").GetString()!;
        using (var assignment = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, http.Role("userRoles"), HttpMethod.Put,
            $"/api/authorization/users/{userId}/roles", new { RoleCodes = roles.Select(role => role.Code).ToArray() })) Assert.Equal(HttpStatusCode.NoContent, assignment.StatusCode);
        var before = await CountsAsync(fixture);
        Assert.Equal(extraPoints * ordinaryRoles + fixture.Catalog.Count, before.Grants);
        var measurements = new List<object>();
        var pagination = await FeatureSqlMeasurement.CaptureAsync(fixture, "role-pagination", async () =>
        {
            foreach (var page in new[] { 1, 2 })
            {
                var result = await FeatureHttpTestRequests.ReadAsync(fixture.Administrator, http.Workbench("authorization.workbench.read"),
                    $"/api/authorization/workbench/roles?query=scale-&pageIndex={page}&pageSize=20");
                Assert.Equal(ordinaryRoles, result.GetProperty("total").GetInt32());
                Assert.InRange(result.GetProperty("items").GetArrayLength(), 1, 20);
            }
        });
        measurements.Add(pagination);
        var pagePlans = JsonSerializer.SerializeToElement(pagination).GetProperty("plans").EnumerateArray()
            .Where(plan => plan.GetProperty("sql").GetString()?.Contains("OFFSET", StringComparison.Ordinal) == true
                && plan.GetProperty("indexes").EnumerateArray().Any(index => index.GetProperty("table").GetString() == "[Role]"));
        Assert.Contains(pagePlans, plan => plan.GetProperty("operators")[0].GetProperty("actualRows").GetInt64() == 20);
        await ObserveBrowserAsync(fixture, roles, before);

        await fixture.StopAsync();
        var unchanged = await FeatureSqlMeasurement.CaptureAsync(fixture, "unchanged-startup", () => fixture.StartAsync());
        measurements.Add(unchanged);
        Assert.DoesNotContain(JsonSerializer.SerializeToElement(unchanged).GetProperty("plans").EnumerateArray(), plan =>
            plan.GetProperty("statementType").GetString() is "INSERT" or "UPDATE" or "DELETE"
            && plan.GetProperty("indexes").EnumerateArray().Any(index => index.GetProperty("table").GetString() is "[FunctionPoint]" or "[RoleFunctionGrant]"));
        Assert.Equal(before, await CountsAsync(fixture));

        // 判权只测授权决策本身；连接先打开，认证会话/前端完整授权投影不混入查询数。
        await using (var db = fixture.CreateContext())
        {
            await db.Database.OpenConnectionAsync();
            var index = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("FunctionPoint"))
                .AsNoTracking().Select(row => new { Key = EF.Property<string>(row, "PositionKey"), Id = EF.Property<string>(row, "EID") }).ToArrayAsync();
            var active = new ActiveFeatureCatalog();
            active.Publish(fixture.Catalog, index.ToDictionary(row => row.Key, row => row.Id, StringComparer.Ordinal));
            var decision = new FeatureDecisionSource(db, active);
            var single = await FeatureSqlMeasurement.CaptureAsync(fixture, "single-decision", async () =>
                Assert.Equal(PermissionDecision.Granted, await decision.EvaluateAsync(userId, positions[0], "Example", "example.runtime-workload.read", CancellationToken.None)));
            Assert.Equal(1, JsonSerializer.SerializeToElement(single).GetProperty("queryCount").GetInt32());
            measurements.Add(single);
        }

        var deleteCount = Math.Max(1, extraPoints / 50);
        var removed = positions.Take(deleteCount).ToHashSet(StringComparer.Ordinal);
        var remaining = new JsonArray(declarations.Skip(deleteCount).Select(node => node!.DeepClone()).ToArray());
        await fixture.StopAsync();
        fixture.SetDeclarations(remaining);
        var deletion = await FeatureSqlMeasurement.CaptureAsync(fixture, "delete-startup", () => fixture.StartAsync());
        measurements.Add(deletion);
        Assert.Contains(JsonSerializer.SerializeToElement(deletion).GetProperty("plans").EnumerateArray(), plan =>
            plan.GetProperty("statementType").GetString() == "DELETE" && plan.GetProperty("operators").EnumerateArray().Any(operation =>
                operation.GetProperty("physical").GetString() == "Index Seek" && operation.GetProperty("indexes").EnumerateArray()
                    .Any(index => index.GetProperty("index").GetString() == "[IX_RoleFunctionGrant_Function_Role]")));
        var afterDelete = await CountsAsync(fixture);
        Assert.Equal(before.Points - deleteCount, afterDelete.Points);
        Assert.Equal(before.Grants - deleteCount * (ordinaryRoles + 1), afterDelete.Grants);
        var requested = positions.Where(position => !removed.Contains(position)).Skip(1).ToArray();
        measurements.Add(await FeatureSqlMeasurement.CaptureAsync(fixture, "role-save", async () =>
        {
            using var response = await FeatureHttpTestRequests.SendAsync(fixture.Administrator, http.Workbench("authorization.workbench.save"), HttpMethod.Put,
                $"/api/authorization/workbench/roles/{roles[0].Id}", new { positions = requested });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }));
        var final = await CountsAsync(fixture);
        Assert.Equal(afterDelete.Grants - 1, final.Grants);
        await using var finalDb = fixture.CreateContext();
        var distribution = await finalDb.Set<Dictionary<string, object?>>(finalDb.ResolveModelKey("RoleFunctionGrant"))
            .GroupBy(row => EF.Property<string>(row, "RoleId")).Select(group => new { roleId = group.Key, grants = group.Count() }).ToArrayAsync();
        var summary = new
        {
            artifacts = new
            {
                head = candidate.Trim(), dirty = !string.IsNullOrWhiteSpace(await GitAsync(fixture.RepositoryRoot, "status", "--porcelain")),
                prerequisite = "Build Host Release, Acceptance.Runner Release and production Shell from the candidate before running. Parent full verify supplies these assets for the formal sample.",
                hostAssembly = Asset(Path.Combine(fixture.HostRoot, "HostApi.dll")),
                shellIndex = Asset(Path.Combine(fixture.RepositoryRoot, "frontend", "packages", "shell", "dist", "index.html"))
            },
            sample = new { addedPoints = extraPoints, ordinaryRoles, ordinaryGrants = extraPoints * ordinaryRoles, initial = before, deletedPoints = deleteCount, afterDelete, final },
            distribution, measurements,
            browser = Path.Combine(fixture.EvidenceRoot, "browser", "feature-scale-browser.json"),
            scope = "Development parameters use representative data; default is 5000 added points / 500000 ordinary grants plus the real catalog and LocalAdmin. No latency pass threshold is asserted."
        };
        await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "scale-summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("FEATURE_SCALE_EVIDENCE: " + fixture.EvidenceRoot);
    }

    private static object Asset(string path) => new { path, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), lastWriteUtc = File.GetLastWriteTimeUtc(path) };

    private static async Task<string> GitAsync(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Candidate probe failed.");
        var result = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException("Candidate probe failed: " + await process.StandardError.ReadToEndAsync());
        return result;
    }

    private static int PositiveEnvironment(string name, int fallback) => Environment.GetEnvironmentVariable(name) is { } value
        ? int.TryParse(value, out var parsed) && parsed > 0 ? parsed : throw new InvalidOperationException(name + " must be positive.") : fallback;

    private static async Task<ScaleCounts> CountsAsync(FeatureCatalogHostFixture fixture)
    {
        await using var db = fixture.CreateContext();
        return new(await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("FunctionPoint")).CountAsync(),
            await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Role")).CountAsync(),
            await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("RoleFunctionGrant")).CountAsync(),
            await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("UserRole")).CountAsync());
    }

    private static async Task ObserveBrowserAsync(FeatureCatalogHostFixture fixture, ScaleRole[] roles, ScaleCounts counts)
    {
        var runId = Guid.NewGuid().ToString("N");
        var ready = Path.Combine(fixture.EvidenceRoot, "shell-ready.json");
        var start = new ProcessStartInfo("node") { WorkingDirectory = fixture.RepositoryRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { Path.Combine(fixture.RepositoryRoot, "scripts", "performance", "production-shell-server.mjs"),
            "--assets-root", Path.Combine(fixture.RepositoryRoot, "frontend", "packages", "shell", "dist"), "--host-origin", fixture.HostUrl,
            "--port", "0", "--run-id", runId, "--ready-file", ready }) start.ArgumentList.Add(argument);
        using var shell = Process.Start(start) ?? throw new InvalidOperationException("Production Shell failed to start.");
        var shellOut = shell.StandardOutput.ReadToEndAsync();
        var shellError = shell.StandardError.ReadToEndAsync();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!File.Exists(ready) && !shell.HasExited && DateTime.UtcNow < deadline) await Task.Delay(100);
            if (!File.Exists(ready)) throw new InvalidOperationException("Production Shell did not publish readiness.");
            using var lease = JsonDocument.Parse(await File.ReadAllTextAsync(ready));
            Assert.Equal(shell.Id, lease.RootElement.GetProperty("pid").GetInt32());
            Assert.Equal(runId, lease.RootElement.GetProperty("runId").GetString());
            var observerStart = new ProcessStartInfo("dotnet") { WorkingDirectory = fixture.RepositoryRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            observerStart.ArgumentList.Add(Path.Combine(fixture.RepositoryRoot, "tests", "acceptance", "Acceptance.Runner", "bin", "Release", "net10.0", "Acceptance.Runner.dll"));
            observerStart.ArgumentList.Add("--observe-feature-scale");
            using var observer = Process.Start(observerStart) ?? throw new InvalidOperationException("Browser observer failed to start.");
            var stdout = observer.StandardOutput.ReadToEndAsync();
            var stderr = observer.StandardError.ReadToEndAsync();
            try
            {
                await observer.StandardInput.WriteAsync(JsonSerializer.Serialize(new
                {
                    hostUrl = fixture.HostUrl, shellUrl = "http://127.0.0.1:" + lease.RootElement.GetProperty("port").GetInt32(),
                    outputDirectory = Path.Combine(fixture.EvidenceRoot, "browser"), administratorECode = "admin",
                    administratorPassword = fixture.AdministratorPassword + "-ready",
                    roles = roles.Take(2).Select(role => new { id = role.Id, code = role.Code, name = role.Name }),
                    sample = new { pointCount = counts.Points, roleCount = counts.Roles, grantCount = counts.Grants }
                }));
                observer.StandardInput.Close();
                await observer.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(3));
                await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "browser.stdout.log"), await stdout);
                await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "browser.stderr.log"), await stderr);
                Assert.True(observer.ExitCode == 0, "Browser observation failed; inspect " + fixture.EvidenceRoot);
            }
            finally
            {
                if (!observer.HasExited) observer.Kill(entireProcessTree: true);
                await observer.WaitForExitAsync();
            }
        }
        finally
        {
            if (!shell.HasExited) shell.Kill(entireProcessTree: false);
            await shell.WaitForExitAsync();
            await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "shell.stdout.log"), await shellOut);
            await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "shell.stderr.log"), await shellError);
        }
    }

    private sealed record ScaleRole(string Id, string Code, string Name);
    private sealed record ScaleCounts(int Points, int Roles, int Grants, int UserRoles);
}
