using System.Data.Common;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopePersonExportSqlServerTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "正式人员 current/all 文件与平面读取共享最终范围和 Display")]
    public async Task PersonExport_UsesFinalReadBeforePagingAndWritesOnlyReadableDisplay()
    {
        var observer = new ExportSqlObserver();
        var fault = new ExportDisplayFault();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
        {
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(observer));
            services.AddSingleton(fault);
            services.AddScoped<IDataScopeReadableIds>(provider => new FaultingReadableIds(
                (IDataScopeReadableIds)provider.GetRequiredService<IDataScopeReadPlanProvider>(),
                provider.GetRequiredService<ExportDisplayFault>()));
        });
        // 在本fixture的任何人员查询编译前启用采集，避免复用启用前缓存的无运行计数计划。
        using (var scope = fixture.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database
                .ExecuteSqlRawAsync("ALTER DATABASE SCOPED CONFIGURATION SET LAST_QUERY_PLAN_STATS = ON");
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var read = http.Organization("persons");
        var export = http.Organization("persons", "export", "list");
        var actor = await HttpTestActors.CreateAuthorizedActorAsync(fixture, admin, "Export238", "导出范围",
            "export-238", "导出人员", read, export);
        using var ordinary = actor.Client;
        var company = BusinessAuditSqlServerHttpApiTests.Id(await http.CreateButtonAsync(admin,
            http.Organization("companies", "create", "list.pageActions"), "/api/organization/companies",
            new { ECODE = "EXPORT-238", ENAME = "不可读公司238", SortOrder = "0100" }));
        var department = BusinessAuditSqlServerHttpApiTests.Id(await http.CreateButtonAsync(admin,
            http.Organization("departments", "create", "list.pageActions"), "/api/organization/departments",
            new { CompanyId = company, ECODE = "EXPORT-238", ENAME = "导出部门238", SortOrder = "0100" }));
        var ids = new List<string>();
        for (var index = 0; index < 32; index++)
            ids.Add(BusinessAuditSqlServerHttpApiTests.Id(await http.CreateButtonAsync(admin,
                http.Organization("persons", "create", "list.pageActions"), "/api/organization/persons",
                new { PersonNo = $"EXPORT238-{index:00}", ENAME = $"导出人员{index:00}", CompanyId = company,
                    DepartmentId = department,
                    BirthDate = index == 1 ? null : "2000-02-29",
                    EmploymentStartedAt = index == 2 ? "2020-01-02T00:04:05Z" : null,
                    EmploymentStatus = "Active", IsEnabled = true })));

        var save = fixture.FeatureCatalog.Single(point => point.BehaviorKey == "authorization.dataScope.save");
        await SaveDenyAsync("organization.person", ids[0], "ExportPerson238");
        await SaveDenyAsync("organization.company", company, "ExportCompany238");
        var filter = "keyword=EXPORT238&sortBy=PersonNo&sortDirection=asc";
        var flat = await FeatureHttpTestRequests.ReadAsync(ordinary, read,
            "/api/organization/persons?page=1&pageSize=100&" + filter, http.ReadDeclaration("organization.person.list", read));
        Assert.Equal(31, flat.GetProperty("total").GetInt32());
        Assert.DoesNotContain(flat.GetProperty("items").EnumerateArray(), row => row.GetProperty("EID").GetString() == ids[0]);
        var rows = flat.GetProperty("items").EnumerateArray().ToArray();
        Assert.All(rows, row => Assert.Equal("***", row.GetProperty("CompanyId_DisplayValue").GetString()));
        var request = new
        {
            mode = "all",
            columns = Columns,
            query = new { pageIndex = 2, pageSize = 2, sortBy = "PersonNo", sortDirection = "asc",
                filters = new Dictionary<string, string?> { ["keyword"] = "EXPORT238" } }
        };

        // 每个实际API仍要求当前导出身份，读取、其他资源或混写旧身份都不能借用查询适配。
        var exportButton = http.Declaration("organization.person.export", export);
        using (var missing = await FeatureHttpTestRequests.SendAsync(ordinary, export, HttpMethod.Post,
            "/api/organization/persons/export", request)) Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var borrowed = await FeatureHttpTestRequests.SendAsync(ordinary, export, HttpMethod.Post,
            "/api/organization/persons/export", request, http.ReadDeclaration("organization.person.list", read)))
            Assert.Equal(HttpStatusCode.BadRequest, borrowed.StatusCode);
        using (var foreign = await FeatureHttpTestRequests.SendAsync(ordinary, export, HttpMethod.Post,
            "/api/organization/persons/export", request, http.ReadDeclaration("organization.company.list", http.Organization("companies"))))
            Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        using (var wrongFeature = await FeatureHttpTestRequests.SendAsync(ordinary, read, HttpMethod.Post,
            "/api/organization/persons/export", request, exportButton))
            Assert.Equal(HttpStatusCode.Forbidden, wrongFeature.StatusCode);
        using (var mixed = new HttpRequestMessage(HttpMethod.Post, "/api/organization/persons/export") { Content = JsonContent.Create(request) })
        {
            mixed.Headers.Add("X-NewP-Feature", export.Position);
            mixed.Headers.Add("X-NewP-Button", exportButton.Id);
            mixed.Headers.Add("X-NewP-Data-Declaration", http.ReadDeclaration("organization.person.list", read).Id);
            using var rejected = await ordinary.SendAsync(mixed);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        // 测试库启用AUTO_CLOSE且禁用连接池；保持目标库打开，避免请求结束时计划缓存随库关闭而清空。
        using var planScope = fixture.Services.CreateScope();
        await using var planConnection = new SqlConnection(planScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString());
        await planConnection.OpenAsync();
        observer.Enabled = true;
        byte[] all;
        try { all = await ExportAsync(request); }
        finally { observer.Enabled = false; }
        var worksheet = ReadRows(all);
        Assert.Equal(32, worksheet.Count);
        Assert.Equal(["工号", "姓名", "公司", "出生日期", "入职时间"], worksheet[0]);
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            Assert.Equal(row.GetProperty("PersonNo").GetString(), worksheet[index + 1][0]);
            Assert.Equal(row.GetProperty("ENAME").GetString(), worksheet[index + 1][1]);
            Assert.Equal(row.GetProperty("CompanyId_DisplayValue").GetString(), worksheet[index + 1][2]);
            Assert.Equal(row.GetProperty("BirthDate_DisplayValue").GetString() ?? "", worksheet[index + 1][3]);
            Assert.Equal(row.GetProperty("EmploymentStartedAt_DisplayValue").GetString() ?? "", worksheet[index + 1][4]);
        }
        Assert.DoesNotContain("不可读公司238", string.Join('|', worksheet.SelectMany(static row => row)));
        Assert.DoesNotContain(company, string.Join('|', worksheet.SelectMany(static row => row)));
        var personSql = observer.Commands.Where(command => command.Contains("FROM [dbo].[Person]", StringComparison.Ordinal)).ToArray();
        Assert.Contains(personSql, command => command.Contains("WHERE", StringComparison.Ordinal)
            && command.Contains("OFFSET", StringComparison.Ordinal));
        Assert.InRange(personSql.Length, 1, 3);
        output.WriteLine($"Person export all: filteredRows={rows.Length}, workbookRows={worksheet.Count - 1}, sqlCommands={observer.Commands.Count}, personQueries={personSql.Length}");
        output.WriteLine("Person page SQL: " + personSql.First(command => command.Contains("OFFSET", StringComparison.Ordinal)));
        var planXml = await CaptureExportPlanAsync(fixture,
            personSql.Single(command => command.Contains("OFFSET", StringComparison.Ordinal)), observer.Database);
        await planConnection.CloseAsync();

        observer.Commands.Clear();
        observer.Enabled = true;
        IReadOnlyList<IReadOnlyList<string>> current;
        try { current = ReadRows(await ExportAsync(new { mode = "current", columns = Columns,
            query = new { pageIndex = 2, pageSize = 2, sortBy = "PersonNo", sortDirection = "asc",
                filters = new Dictionary<string, string?> { ["keyword"] = "EXPORT238" } } })); }
        finally { observer.Enabled = false; }
        Assert.Equal(20, observer.Commands.Count);
        output.WriteLine($"Person export current: workbookRows={current.Count - 1}, sqlCommands={observer.Commands.Count}");
        Assert.Equal(3, current.Count);
        Assert.Equal(rows[2].GetProperty("PersonNo").GetString(), current[1][0]);
        Assert.Equal(rows[3].GetProperty("PersonNo").GetString(), current[2][0]);

        // 同一 Cookie 的下一请求立即看到对象覆盖，不借助重新登录或新导出动作。
        await SaveObjectAsync(ids[0], "allow");
        var afterAllow = ReadRows(await ExportAsync(request));
        Assert.Equal(33, afterAllow.Count);
        Assert.Equal("EXPORT238-00", afterAllow[1][0]);
        await SaveObjectAsync(ids[0], "deny");
        var afterDeny = ReadRows(await ExportAsync(request));
        Assert.Equal(32, afterDeny.Count);
        fault.ResourceKey = "organization.company";
        using (var failed = await FeatureHttpTestRequests.SendAsync(ordinary, export, HttpMethod.Post,
            "/api/organization/persons/export", request, exportButton))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.NotEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                failed.Content.Headers.ContentType?.MediaType);
        }
        fault.ResourceKey = null;

        Assert.NotNull(planXml);
        var actualPlan = XDocument.Parse(planXml);
        var operators = actualPlan.Descendants().Where(node => node.Name.LocalName == "RelOp").ToArray();
        var counters = operators.SelectMany(node => node.Descendants())
            .Where(node => node.Name.LocalName == "RunTimeCountersPerThread")
            .Select(node => new { rows = (long?)node.Attribute("ActualRows") ?? 0 }).ToArray();
        Assert.Contains(counters, counter => counter.rows > 0);
        output.WriteLine($"Person export actual plan: operators={operators.Length}, maxActualRows={counters.Max(counter => counter.rows)}");


        await fixture.RestartWithActionAsync("read", false);
        using var readDisabled = fixture.CreateClient();
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(readDisabled);
        using (var login = await readDisabled.PostAsJsonAsync("/api/auth/login",
            new { ECODE = "export-238", Password = "Audit-test-secure-938!" }))
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(readDisabled);
        using (var restored = await FeatureHttpTestRequests.SendAsync(readDisabled, export, HttpMethod.Post,
            "/api/organization/persons/export", request, exportButton))
        {
            Assert.True(restored.StatusCode == HttpStatusCode.OK,
                restored.StatusCode == HttpStatusCode.OK ? null : await restored.Content.ReadAsStringAsync());
            var restoredRows = ReadRows(await restored.Content.ReadAsByteArrayAsync());
            Assert.Equal(33, restoredRows.Count);
            Assert.Equal("不可读公司238", restoredRows[1][2]);
        }

        async Task SaveDenyAsync(string resourceKey, string objectId, string ruleId)
        {
            var rule = new DataScopeRuleDto(ruleId, ruleId, null, ruleId, true, resourceKey,
                new("attribute", "EID", "equal", JsonSerializer.SerializeToElement(objectId)),
                [new("Subject_" + ruleId, "all", null, 0,
                    new Dictionary<string, string> { ["read"] = "deny" })], []);
            using var response = await new FeatureHttpTestRequests(fixture).SendButtonAsync(admin, save, HttpMethod.Put,
                "/api/authorization/data-scope/rules/" + ruleId, rule);
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }

        async Task SaveObjectAsync(string objectId, string decision)
        {
            var grant = new DataScopeObjectDto("organization.person", objectId,
                [new("ExportObject238", "user", actor.AccountId, 0,
                    new Dictionary<string, string> { ["read"] = decision })]);
            using var response = await FeatureHttpTestRequests.SendAsync(admin,
                fixture.FeatureCatalog.First(point => point.BehaviorKey == "organization.person.authorize"), HttpMethod.Put,
                $"/api/authorization/data-scope/objects/organization.person/{objectId}", grant,
                http.Declaration("organization.person.authorization.manage",
                    fixture.FeatureCatalog.First(point => point.BehaviorKey == "organization.person.authorize")));
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }

        async Task<byte[]> ExportAsync(object body)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, export, HttpMethod.Post,
                "/api/organization/persons/export", body, exportButton);
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                response.StatusCode == HttpStatusCode.OK ? null : await response.Content.ReadAsStringAsync());
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                response.Content.Headers.ContentType?.MediaType);
            return await response.Content.ReadAsByteArrayAsync();
        }
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "单条实际人员导出精确关联已观测数据库和SQL语句的运行计划，不重放大规模准备")]
    public async Task ActualPersonExportStatement_PlanCaptureUsesTheObservedDatabaseAndStatement()
    {
        var observer = new ExportSqlObserver();
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services =>
            services.AddDbContext<EfPlatformDbContext>((_, options) => options.AddInterceptors(observer)));
        using (var scope = fixture.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database
                .ExecuteSqlRawAsync("ALTER DATABASE SCOPED CONFIGURATION SET LAST_QUERY_PLAN_STATS = ON");
        var http = new FeatureHttpTestRequests(fixture);
        using var admin = await fixture.LoginAdministratorAsync();
        var create = http.Organization("persons", "create", "list.pageActions");
        await http.CreateButtonAsync(admin, create, "/api/organization/persons",
            new { PersonNo = "PLAN336", ENAME = "计划采集人员", EmploymentStatus = "Active", IsEnabled = true });
        var export = http.Organization("persons", "export", "list");
        using var planScope = fixture.Services.CreateScope();
        await using var planConnection = new SqlConnection(planScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString());
        await planConnection.OpenAsync();
        observer.Commands.Clear();
        observer.Enabled = true;
        using var response = await FeatureHttpTestRequests.SendAsync(admin, export, HttpMethod.Post,
            "/api/organization/persons/export", new { mode = "all", columns = Columns,
                query = new { filters = new Dictionary<string, string?> { ["keyword"] = "PLAN336" } } },
            http.Declaration("organization.person.export", export));
        observer.Enabled = false;
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            response.StatusCode == HttpStatusCode.OK ? null : await response.Content.ReadAsStringAsync());
        Assert.Equal(2, ReadRows(await response.Content.ReadAsByteArrayAsync()).Count);
        var statement = Assert.Single(observer.Commands, command => command.Contains("FROM [dbo].[Person]", StringComparison.Ordinal)
            && command.Contains("OFFSET", StringComparison.Ordinal));
        output.WriteLine($"Actual one-row export plan capture database={observer.Database}");
        var plan = await CaptureExportPlanAsync(fixture, statement, observer.Database);
        Assert.NotNull(plan);
        Assert.Contains(XDocument.Parse(plan).Descendants(), node => node.Name.LocalName == "RunTimeCountersPerThread"
            && ((long?)node.Attribute("ActualRows") ?? 0) > 0);
    }

    // 只核对本次实际导出命令；即时保存计划，业务断言完成后再裁决采集，不把诊断请求计入导出开销。
    private async Task<string?> CaptureExportPlanAsync(BusinessAuditHostFixture fixture, string statement, string? database)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        await using var connection = new SqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        Assert.Equal(database, connection.Database);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name, CONVERT(int,value) FROM sys.database_scoped_configurations WHERE name IN (N'LAST_QUERY_PLAN_STATS',N'LIGHTWEIGHT_QUERY_PROFILING')";
        await using (var settings = await command.ExecuteReaderAsync())
            while (await settings.ReadAsync()) output.WriteLine($"Export plan setting {settings.GetString(0)}={settings.GetInt32(1)}");
        command.CommandText = """
            SELECT TOP (20) t.text, CONVERT(nvarchar(max), p.query_plan), CONVERT(varchar(18), s.query_hash, 1),
                s.statement_start_offset, s.statement_end_offset, CONVERT(int, a.value), CONVERT(int, DB_ID())
            FROM sys.dm_exec_query_stats s
            CROSS APPLY sys.dm_exec_sql_text(s.sql_handle) t
            OUTER APPLY sys.dm_exec_query_plan_stats(s.plan_handle) p
            OUTER APPLY (SELECT value FROM sys.dm_exec_plan_attributes(s.plan_handle) WHERE attribute = N'dbid') a
            WHERE t.text LIKE N'%Person%' AND t.text LIKE N'%OFFSET%'
              AND t.text NOT LIKE N'%sys.dm_exec_query_stats%'
            ORDER BY s.last_execution_time DESC
            """;
        string? result = null;
        var candidates = 0; var sameDatabase = 0; var sameStatement = 0; var actualPlans = 0;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ++candidates;
            var text = reader.GetString(0);
            var sameDb = !reader.IsDBNull(5) && reader.GetInt32(5) == reader.GetInt32(6);
            var sameSql = text.Replace("\r\n", "\n", StringComparison.Ordinal).Contains(statement.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
            if (sameDb) ++sameDatabase;
            if (sameDb && sameSql) ++sameStatement;
            if (sameDb && sameSql && !reader.IsDBNull(1)) { ++actualPlans; result ??= reader.GetString(1); }
            output.WriteLine($"Export plan candidate hash={reader.GetString(2)}, offsets={reader.GetInt32(3)}/{reader.GetInt32(4)}, dbMatch={sameDb}, sqlMatch={sameSql}, hasPlan={!reader.IsDBNull(1)}, prefix={(sameDb ? text[..Math.Min(text.Length, 140)] : "other database")}");
        }
        output.WriteLine($"Export plan candidates={candidates}, sameDatabase={sameDatabase}, exactStatement={sameStatement}, actualPlans={actualPlans}, statementSha256={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(statement)))}");
        return result;
    }

    private static readonly object[] Columns =
    [
        new { key = "personNo", field = "PersonNo", title = "工号" },
        new { key = "name", field = "ENAME", title = "姓名" },
        new { key = "company", field = "CompanyId", title = "公司" },
        new { key = "birthDate", field = "BirthDate", title = "出生日期" },
        new { key = "startedAt", field = "EmploymentStartedAt", title = "入职时间" }
    ];

    private static IReadOnlyList<IReadOnlyList<string>> ReadRows(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var document = XDocument.Parse(reader.ReadToEnd());
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Descendants(spreadsheet + "row")
            .Select(row => (IReadOnlyList<string>)row.Elements(spreadsheet + "c")
                .Select(cell => cell.Descendants(spreadsheet + "t").Single().Value).ToArray())
            .ToArray();
    }

    private sealed class ExportSqlObserver : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public string? Database { get; private set; }
        public List<string> Commands { get; } = [];

        /// <inheritdoc/>
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                Commands.Add(command.CommandText);
                if (command.CommandText.Contains("FROM [dbo].[Person]", StringComparison.Ordinal) && command.CommandText.Contains("OFFSET", StringComparison.Ordinal))
                    Database = command.Connection!.Database;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ExportDisplayFault
    {
        public string? ResourceKey { get; set; }
    }

    private sealed class FaultingReadableIds(IDataScopeReadableIds inner, ExportDisplayFault control) : IDataScopeReadableIds
    {
        /// <inheritdoc/>
        public Task<IReadOnlySet<string>> GetReadableIdsAsync(string resourceKey, IReadOnlyCollection<string> objectIds,
            CancellationToken cancellationToken = default) =>
            resourceKey == control.ResourceKey
                ? Task.FromException<IReadOnlySet<string>>(new DataScopeReadExecutionException(resourceKey, new IOException("injected")))
                : inner.GetReadableIdsAsync(resourceKey, objectIds, cancellationToken);
    }
}
