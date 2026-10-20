using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using static ICP.Platform.HostApi.Tests.FeatureHttpTestRequests;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class OrganizationAdmissionSqlServerHttpApiTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "代表性子树局部写入的真实SQL返回量、数据库成本及索引证据")]
    public async Task SubtreeChanges_RecordActualDatabaseCostAndKeepUnchangedRecords()
    {
        foreach (var width in new[] { 4, 16 })
        {
            var costs = new SubtreeConnectionCosts();
            var commands = new SubtreeCommands();
            await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
                services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(costs, commands)));
            using var admin = await fixture.LoginAdministratorAsync();
            var http = new FeatureHttpTestRequests(fixture);
            var prepared = await PrepareAsync(admin, http);
            var target = await CreateDepartmentAsync(admin, http, prepared.Company, "TARGET");
            var branch = await CreateDepartmentAsync(admin, http, prepared.Company, "BRANCH", prepared.Department);
            var leaves = new List<string>();
            var persons = new List<string> { prepared.Person };
            for (var index = 0; index < width; index++)
            {
                var leaf = await CreateDepartmentAsync(admin, http, prepared.Company, "LEAF-" + index, branch);
                leaves.Add(leaf);
                for (var person = 0; person < 2; person++)
                    persons.Add(BusinessAuditSqlServerHttpApiTests.Id(await http.CreateButtonAsync(admin, http.Organization("persons", "create", "list.pageActions"),
                        "/api/organization/persons", new { PersonNo = $"COST-{index}-{person}", ENAME = "局部维护人员", CompanyId = prepared.Company,
                            DepartmentId = leaf, EmploymentStatus = "Active", IsEnabled = true })));
            }
            var actor = await PrepareAccountAsync(admin, http, prepared.Company, leaves[^1], "cost-leaf");
            persons.Add(actor.Person);
            using var rootClient = await fixture.LoginAsync("admitted", prepared.Password);
            using var leafClient = await fixture.LoginAsync("cost-leaf", actor.Password);
            var query = fixture.Services.GetRequiredService<IOrganizationQuery>();
            var before = await query.ReadAsync();
            await Measure("disable", () => SetOrganizationStateAsync(admin, http, "departments", prepared.Department, false));
            var disabled = await query.ReadAsync();
            foreach (var person in persons) Assert.Same(before.Find(OrganizationRecordKind.Person, person), disabled.Find(OrganizationRecordKind.Person, person));
            foreach (var leaf in leaves) Assert.Same(before.Find(OrganizationRecordKind.Department, leaf), disabled.Find(OrganizationRecordKind.Department, leaf));
            Assert.Equal(true, before.Find(OrganizationRecordKind.Department, prepared.Department)!["IsEnabled"]);
            Assert.Equal(false, disabled.Find(OrganizationRecordKind.Department, prepared.Department)!["IsEnabled"]);
            Assert.Single(commands.Sql, sql => sql.Contains("UPDATE", StringComparison.Ordinal) && sql.Contains("[AuthenticationSession]", StringComparison.Ordinal));
            Assert.DoesNotContain(commands.Sql, sql => sql.Contains("FROM [dbo].[Person]", StringComparison.Ordinal));

            await Measure("reparent", async () =>
            {
                using var response = await SendAsync(admin, http.Organization("departments", "edit", "list.rowActions"), HttpMethod.Put,
                    $"/api/organization/departments/{branch}", new { ParentDepartmentId = target });
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            });
            var moved = await query.ReadAsync();
            Assert.Equal(new[] { branch, target }, moved.FindByAccount(actor.Account)!.Ancestors.Select(row => (string)row["EID"]!));
            Assert.Equal(new[] { branch, prepared.Department }, before.FindByAccount(actor.Account)!.Ancestors.Select(row => (string)row["EID"]!));
            foreach (var person in persons) Assert.Same(before.Find(OrganizationRecordKind.Person, person), moved.Find(OrganizationRecordKind.Person, person));
            Assert.DoesNotContain(commands.Sql, sql => sql.Contains("UPDATE", StringComparison.Ordinal) && sql.Contains("[AuthenticationSession]", StringComparison.Ordinal));
            Assert.Equal(HttpStatusCode.Unauthorized, (await leafClient.GetAsync("/api/auth/current-user")).StatusCode);
            await AssertIndexesAsync();

            async Task Measure(string operation, Func<Task> mutation)
            {
                costs.Reset(); commands.Sql.Clear(); costs.Enabled = true; commands.Enabled = true;
                var watch = Stopwatch.StartNew();
                try { await mutation(); }
                finally { watch.Stop(); costs.Enabled = false; commands.Enabled = false; }
                Assert.NotEmpty(costs.Samples);
                // 既有CRUD唯一性检查完整读取部门表；如实记录，与缓存全量重建区分。
                output.WriteLine(JsonSerializer.Serialize(new { width, departments = width + 3, persons = persons.Count, depth = 3, operation,
                    wallMs = watch.Elapsed.TotalMilliseconds, roundTrips = costs.Samples.Sum(s => s.RoundTrips), rows = costs.Samples.Sum(s => s.Rows),
                    bytes = costs.Samples.Sum(s => s.Bytes), sqlCpuMs = costs.Samples.Sum(s => s.Cpu), logicalReads = costs.Samples.Sum(s => s.LogicalReads),
                    physicalReads = costs.Samples.Sum(s => s.PhysicalReads), sqlCommands = commands.Sql.Count,
                    fullDepartmentReads = commands.Sql.Count(sql => sql.Contains("FROM [dbo].[Department]", StringComparison.Ordinal) && !sql.Contains("WHERE", StringComparison.Ordinal)) }));
            }

            async Task AssertIndexesAsync()
            {
                await using var scope = fixture.Services.CreateAsyncScope();
                var connectionString = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT t.name, i.name, c.name, ic.key_ordinal
                    FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id
                    JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
                    JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                    WHERE t.name IN ('Company','Department','Person','AuthenticationSession') AND ic.key_ordinal>0
                    ORDER BY t.name,i.name,ic.key_ordinal;
                    """;
                var indexes = new List<(string Table, string Index, string Column, int Ordinal)>();
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) indexes.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), Convert.ToInt32(reader.GetValue(3))));
                Assert.Contains(indexes, i => i.Table == "Department" && i.Column == "EID" && i.Ordinal == 1);
                Assert.Contains(indexes, i => i.Table == "Department" && i.Column == "ParentDepartmentId");
                Assert.Contains(indexes, i => i.Table == "Person" && i.Column == "DepartmentId");
                Assert.Contains(indexes, i => i.Table == "AuthenticationSession" && i.Column == "AccountEID" && i.Ordinal == 1);
                output.WriteLine(JsonSerializer.Serialize(new { width, indexes = indexes.Select(i => new { i.Table, i.Index, i.Column, i.Ordinal }) }));
            }
        }
    }

    private sealed class SubtreeCommands : DbCommandInterceptor
    {
        public bool Enabled;
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { if (Enabled) Sql.Add(command.CommandText); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (Enabled) Sql.Add(command.CommandText); return ValueTask.FromResult(result); }
    }

    private sealed record SqlCost(long RoundTrips, long Rows, long Bytes, long Cpu, long LogicalReads, long PhysicalReads);

    private sealed class SubtreeConnectionCosts : DbConnectionInterceptor
    {
        public bool Enabled;
        public List<SqlCost> Samples { get; } = [];
        private readonly Dictionary<DbConnection, long[]> _started = [];
        public void Reset() { Samples.Clear(); _started.Clear(); }
        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!Enabled) return;
            var sql = (SqlConnection)connection;
            _started.Add(connection, await ReadServerCost(sql));
            sql.StatisticsEnabled = true;
            sql.ResetStatistics();
        }
        public override async ValueTask<InterceptionResult> ConnectionClosingAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            if (!_started.Remove(connection, out var before)) return result;
            var sql = (SqlConnection)connection;
            var after = await ReadServerCost(sql);
            var statistics = sql.RetrieveStatistics();
            Samples.Add(new(Convert.ToInt64(statistics["ServerRoundtrips"]), Convert.ToInt64(statistics["SelectRows"]), Convert.ToInt64(statistics["BytesReceived"]),
                after[0] - before[0], after[1] - before[1], after[2] - before[2]));
            return result;
        }
        // 由独立连接观察被测会话成本，避免把诊断SQL计入应用数据库往返与返回行数。
        private static async Task<long[]> ReadServerCost(SqlConnection target)
        {
            await using var observer = new SqlConnection(target.ConnectionString);
            await observer.OpenAsync();
            await using var command = observer.CreateCommand();
            command.CommandText = "SELECT cpu_time, logical_reads, reads FROM sys.dm_exec_sessions WHERE session_id=@id";
            command.Parameters.AddWithValue("@id", target.ServerProcessId);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return Enumerable.Range(0, 3).Select(index => Convert.ToInt64(reader.GetValue(index))).ToArray();
        }
    }
}
