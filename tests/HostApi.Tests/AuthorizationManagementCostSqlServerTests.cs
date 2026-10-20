using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class DataScopePermissionCostSqlServerTests
{
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "独占记录角色正式分页和原管理主体目录的同样本成本")]
    public async Task AuthorizationManagement_RecordsRolePageAndSubjectDirectoryCosts()
    {
        using var capture = new AuthorizationCostCapture();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var read = http.Role(); var member = http.Role("userRoles");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Cost311Manager", "角色成本主体",
            "cost311-manager", "角色成本普通主体", read, member);
        using var ordinary = actor.Client;
        var prototype = await http.CreateButtonAsync(admin, http.Role("create"), "/api/authorization/roles",
            new { Code = "COST311-R000", Name = "COST311 Role 000" });
        var prototypeId = prototype.GetProperty("id").GetString()!;
        string connectionString;
        var codes = new List<string> { "COST311-R000", "Cost311Manager" };
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            connectionString = db.Database.GetConnectionString()!;
            var roles = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Role"));
            var original = await roles.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == prototypeId);
            for (var index = 1; index < 100; index++)
            {
                var code = "COST311-R" + index.ToString("000"); codes.Add(code);
                roles.Add(new(original) { ["EID"] = "Role_" + Guid.NewGuid().ToString("N"), ["ECODE"] = code, ["ENAME"] = "COST311 Role " + index.ToString("000") });
            }
            var accounts = db.Set<Dictionary<string, object?>>(db.ResolveModelKey("IdentityAccount"));
            var account = await accounts.AsNoTracking().SingleAsync(row => EF.Property<string>(row, "EID") == actor.AccountId);
            for (var index = 0; index < 20; index++)
                accounts.Add(new(account) { ["EID"] = "IdentityAccount_" + Guid.NewGuid().ToString("N"),
                    ["ECODE"] = "COST311-T" + index.ToString("000"), ["NormalizedECODE"] = "COST311-T" + index.ToString("000"), ["ENAME"] = "COST311 Target " + index.ToString("000") });
            await db.SaveChangesAsync();
        }
        using (var assigned = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, member, HttpMethod.Put,
            "/api/authorization/users/" + actor.AccountId + "/roles", new { RoleCodes = codes }))
            Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        var stage = Environment.GetEnvironmentVariable("NEWP_AUTHORIZATION_EXECUTION_COST_STAGE") ?? "candidate";
        var run = Path.Combine(FindRoot(), ".codex-temp", "issue-305", "311", "cost", stage + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var plan = fixture.Services.GetRequiredService<CanonicalModulePlan>();
        await MeasureAsync("role-page-100", read, "/api/authorization/roles/query?query=COST311-R&pageIndex=1&pageSize=100",
            http.Declaration("authorization.role.list", read), body =>
            { Assert.Equal(100, body.GetProperty("total").GetInt32()); Assert.Equal(100, body.GetProperty("items").GetArrayLength()); return 100; });
        await MeasureAsync("account-candidates-20", member, "/api/authorization/users/candidates?query=COST311-T", null,
            body => { Assert.Equal(20, body.GetArrayLength()); return 20; });
        await MeasureAsync("configured-roles-101", member, "/api/authorization/users/" + actor.AccountId + "/roles", null,
            body => { Assert.Equal(101, body.GetProperty("roleCodes").GetArrayLength()); return 101; });

        // 这四条只作同候选配对诊断；旧工作台断链请求不冒充历史性能基线。
        if (!stage.StartsWith("baseline", StringComparison.Ordinal))
        {
            var workbench = http.Workbench("authorization.workbench.read");
            await http.GrantAsync(admin, actor.RoleId, read, member, workbench);
            await MeasureAsync("paired-role-page-100", read, "/api/authorization/roles/query?query=COST311-R&pageIndex=1&pageSize=100",
                http.Declaration("authorization.role.list", read), body =>
                { Assert.Equal(100, body.GetProperty("items").GetArrayLength()); return 100; });
            await MeasureAsync("paired-workbench-page-100", workbench, "/api/authorization/workbench/roles?query=COST311-R&pageIndex=1&pageSize=100",
                http.Declaration("authorization.workbench.read", workbench), body =>
                { Assert.Equal(100, body.GetProperty("items").GetArrayLength()); return 100; });
            await MeasureAsync("paired-role-detail", read, "/api/authorization/roles/" + prototypeId,
                http.Declaration("authorization.role.read", read), body =>
                { Assert.Equal(prototypeId, body.GetProperty("id").GetString()); return 1; });
            await MeasureAsync("paired-workbench-detail", workbench, "/api/authorization/workbench/roles/" + prototypeId,
                http.Declaration("authorization.workbench.read", workbench), body =>
                { Assert.Equal(prototypeId, body.GetProperty("role").GetProperty("id").GetString()); return 1; });
        }

        async Task MeasureAsync(string operation, FeaturePoint point, string path, ActionDataAccessRequirement? binding, Func<JsonElement, int> check)
        {
            capture.Begin(); var watch = Stopwatch.StartNew(); JsonElement body;
            try
            {
                using var response = await FeatureHttpTestRequests.SendAsync(ordinary, point, HttpMethod.Get, path, dataAccess: binding);
                Assert.True(response.StatusCode == HttpStatusCode.OK, operation + ": HTTP " + (int)response.StatusCode);
                body = await response.Content.ReadFromJsonAsync<JsonElement>();
            }
            finally { watch.Stop(); capture.Active = false; }
            var returned = check(body);
            var observations = new List<ExampleExecutionSqlPlanTests.Measurement>();
            foreach (var sample in capture.Samples) observations.Add(await ExampleExecutionSqlPlanTests.ExplainAsync(connectionString, sample, requireIo: false));
            await File.WriteAllTextAsync(Path.Combine(run, operation + ".json"), JsonSerializer.Serialize(new
            {
                stage, operation, roleRows = 100, candidateAccountRows = 20, configuredRoles = 101, returned,
                elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, sqlCommands = capture.Commands.Count,
                commands = capture.Commands.ToArray(), connectionEvents = capture.ConnectionEvents.ToArray(), readPlans = observations
            }, new JsonSerializerOptions { WriteIndented = true }));
            output.WriteLine($"{stage}/{operation}: SQL={capture.Commands.Count}, returned={returned}, plans={observations.Count}, artifacts={run}");
        }
    }

    // 请求窗口结束后才回放只读查询；不把诊断连接或查询计入真实请求成本。
    private sealed class AuthorizationCostCapture : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly IDisposable all;
        private readonly List<IDisposable> subscriptions = [];
        internal bool Active;
        internal List<string> Commands { get; } = [];
        internal List<string> ConnectionEvents { get; } = [];
        internal List<ExampleExecutionSqlPlanTests.Sample> Samples { get; } = [];
        internal AuthorizationCostCapture() => all = DiagnosticListener.AllListeners.Subscribe(this);
        internal void Begin() { Commands.Clear(); ConnectionEvents.Clear(); Samples.Clear(); Active = true; }
        public void OnNext(DiagnosticListener listener)
        { if (listener.Name == "SqlClientDiagnosticListener") subscriptions.Add(listener.Subscribe(this)); }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (!Active) return;
            if (value.Key.EndsWith("WriteConnectionOpenAfter", StringComparison.Ordinal) || value.Key.EndsWith("WriteConnectionCloseAfter", StringComparison.Ordinal))
                ConnectionEvents.Add(value.Key);
            if (!value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal)
                || value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is not DbCommand command) return;
            Commands.Add(command.CommandText);
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && new[] { "DataScope", "[Role]", "[IdentityAccount]", "[UserRole]" }.Any(command.CommandText.Contains))
                Samples.Add(new(command.CommandText, command.Parameters.Cast<SqlParameter>().Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray()));
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose() { Active = false; foreach (var subscription in subscriptions) subscription.Dispose(); all.Dispose(); }
    }
}
