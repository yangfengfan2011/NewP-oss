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
            "/api/organization/persons?page=1&pageSize=100&" + filter);
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

        using (var scope = fixture.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database
                .ExecuteSqlRawAsync("ALTER DATABASE SCOPED CONFIGURATION SET LAST_QUERY_PLAN_STATS = ON");
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
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            await using var connection = new SqlConnection(db.Database.GetConnectionString());
            await connection.OpenAsync();
            await using var plan = connection.CreateCommand();
            plan.CommandText = """
                SELECT TOP (1) CONVERT(nvarchar(max), p.query_plan)
                FROM sys.dm_exec_query_stats s
                CROSS APPLY sys.dm_exec_sql_text(s.sql_handle) t
                CROSS APPLY sys.dm_exec_query_plan_stats(s.plan_handle) p
                WHERE t.text LIKE N'%Person%'
                  AND t.text LIKE N'%EmploymentStartedAt%'
                  AND t.text LIKE N'%OFFSET%'
                  AND t.text LIKE N'%EXPORT238%'
                  AND t.text NOT LIKE N'%sys.dm_exec_query_stats%'
                ORDER BY s.last_execution_time DESC
                """;
            var planXml = await plan.ExecuteScalarAsync() as string;
            Assert.NotNull(planXml);
            var actualPlan = XDocument.Parse(planXml);
            var operators = actualPlan.Descendants().Where(node => node.Name.LocalName == "RelOp").ToArray();
            var counters = operators.SelectMany(node => node.Descendants())
                .Where(node => node.Name.LocalName == "RunTimeCountersPerThread")
                .Select(node => new { rows = (long?)node.Attribute("ActualRows") ?? 0 }).ToArray();
            Assert.Contains(counters, counter => counter.rows > 0);
            output.WriteLine($"Person export actual plan: operators={operators.Length}, maxActualRows={counters.Max(counter => counter.rows)}");
        }

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
            "/api/organization/persons/export", request))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.NotEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                failed.Content.Headers.ContentType?.MediaType);
        }
        fault.ResourceKey = null;

        await fixture.RestartWithActionAsync("read", false);
        using var readDisabled = fixture.CreateClient();
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(readDisabled);
        using (var login = await readDisabled.PostAsJsonAsync("/api/auth/login",
            new { ECODE = "export-238", Password = "Audit-test-secure-938!" }))
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await BusinessAuditHostFixture.RefreshAntiforgeryAsync(readDisabled);
        using (var restored = await FeatureHttpTestRequests.SendAsync(readDisabled, export, HttpMethod.Post,
            "/api/organization/persons/export", request))
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
                $"/api/authorization/data-scope/objects/organization.person/{objectId}", grant);
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }

        async Task<byte[]> ExportAsync(object body)
        {
            using var response = await FeatureHttpTestRequests.SendAsync(ordinary, export, HttpMethod.Post,
                "/api/organization/persons/export", body);
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                response.StatusCode == HttpStatusCode.OK ? null : await response.Content.ReadAsStringAsync());
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                response.Content.Headers.ContentType?.MediaType);
            return await response.Content.ReadAsByteArrayAsync();
        }
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
        public List<string> Commands { get; } = [];

        /// <inheritdoc/>
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled) Commands.Add(command.CommandText);
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
