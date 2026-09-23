using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Authorization.Contracts;
using System.Linq.Expressions;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Kernel.ModulePlanning;
using System.Text.Json;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Persistence.Ef;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class DepartmentHierarchySqlServerIntegrationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const string RunEnvironmentVariable = "NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION";
    private const string AdminConnectionEnvironmentVariable = "NEWP_ORGANIZATION_SQLSERVER_ADMIN_CONNECTION";

    [EnvironmentGatedFact(RunEnvironmentVariable, "受控上级候选在 SQL 分页前过滤并清理临时层级集合")]
    [Trait("Category", "SqlServerSmoke")]
    public async Task ScopedParentLookup_FiltersBeforePagingMasksCurrentAndCleansUpAfterFailure()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        await using var context = CreateContext(fixture.ConnectionString);
        await context.Database.EnsureCreatedAsync();
        AddCompany(context, "Company_1");
        AddDepartment(context, "Department_000", "Company_1", null);
        AddDepartment(context, "Department_001", "Company_1", "Department_000");
        for (var index = 2; index < 1002; index++)
            AddDepartment(context, $"Department_{index:D3}", "Company_1", null);
        AddCompany(context, "Company_2");
        AddDepartment(context, "Department_Other", "Company_2", null);
        await context.SaveChangesAsync();

        var plans = new TestReadPlans();
        var directory = new EfDepartmentDirectory(context, new SqlServerDepartmentHierarchyQuery(context), plans);
        var connection = Assert.IsType<SqlConnection>(context.Database.GetDbConnection());
        await connection.OpenAsync();
        connection.StatisticsEnabled = true;
        var ioMessages = new List<string>();
        connection.InfoMessage += (_, message) => ioMessages.Add(message.Message);
        await using (var enableIo = connection.CreateCommand())
        {
            enableIo.CommandText = "SET STATISTICS IO ON";
            await enableIo.ExecuteNonQueryAsync();
        }
        await using var observer = new SqlConnection(fixture.ConnectionString);
        await observer.OpenAsync();
        connection.ResetStatistics();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var first = await directory.LookupParentCandidatesAsync("Company_1", "Department_000",
            new OrganizationDirectoryLookupRequest(null, 1, 1, ["Department_002"]));
        watch.Stop();
        var statistics = connection.RetrieveStatistics();
        await using (var disableIo = connection.CreateCommand())
        {
            disableIo.CommandText = "SET STATISTICS IO OFF";
            await disableIo.ExecuteNonQueryAsync();
        }
        await using var planCommand = observer.CreateCommand();
        planCommand.CommandText = """
            SELECT TOP (1) CONVERT(nvarchar(max), [plan].[query_plan])
            FROM sys.dm_exec_query_stats AS [stats]
            CROSS APPLY sys.dm_exec_sql_text([stats].[sql_handle]) AS [text]
            CROSS APPLY sys.dm_exec_query_plan([stats].[plan_handle]) AS [plan]
            WHERE [text].[text] LIKE N'%#DepartmentDescendants%'
              AND [text].[text] LIKE N'%ORDER BY%'
              AND [text].[text] NOT LIKE N'%sys.dm_exec_query_stats%'
            ORDER BY [stats].[last_execution_time] DESC
            """;
        var planXml = await planCommand.ExecuteScalarAsync() as string;
        string?[] operators = planXml is null ? [] : System.Xml.Linq.XDocument.Parse(planXml).Descendants()
            .Where(node => node.Name.LocalName == "RelOp")
            .Select(node => (string?)node.Attribute("PhysicalOp"))
            .Where(value => value is not null).Distinct().ToArray();
        output.WriteLine(JsonSerializer.Serialize(new {
            operation = "scoped-parent-page", rows = 1002, elapsedMs = watch.Elapsed.TotalMilliseconds,
            roundTrips = statistics["ServerRoundtrips"], selectedRows = statistics["SelectRows"],
            bytesReceived = statistics["BytesReceived"], ioMessages, operators }));
        Assert.Equal("Department_003", Assert.Single(first.Items).Id);
        Assert.True(first.HasMore);
        Assert.Equal("Department_002", Assert.Single(first.MaskedCurrentIds!));

        plans.BadField = true;
        await Assert.ThrowsAnyAsync<Exception>(() => directory.LookupParentCandidatesAsync("Company_1", "Department_000",
            new OrganizationDirectoryLookupRequest(null, 1, 1)));
        plans.BadField = false;
        var second = await directory.LookupParentCandidatesAsync("Company_1", "Department_000",
            new OrganizationDirectoryLookupRequest(null, 2, 1));
        Assert.Equal("Department_004", Assert.Single(second.Items).Id);
    }

    private sealed class TestReadPlans : IDataScopeReadPlanProvider
    {
        public bool BadField { get; set; }
        public Task<DataScopeReadPlan> GetReadPlanAsync(string resourceKey, CancellationToken cancellationToken = default)
        {
            Assert.Equal("organization.department", resourceKey);
            Expression<Func<Dictionary<string, object?>, bool>> predicate = BadField
                ? row => EF.Property<string>(row, "UnknownField") == "x"
                : row => EF.Property<string>(row, "EID") != "Department_002";
            return Task.FromResult(new DataScopeReadPlan(predicate));
        }
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "授权截断关系树的代表性规模、往返、读取量、耗时及缓存计划")]
    public async Task ScopedOrganizationTree_RecordsRepresentativeSqlCost()
    {
        foreach (var size in new[] { 128, 1024 })
        {
            await using var fixture = await SqlFixture.CreateAsync();
            await using var context = CreateContext(fixture.ConnectionString);
            await context.Database.EnsureCreatedAsync();
            AddCompany(context, "Company_1");
            for (var index = 0; index < size; index++)
                AddDepartment(context, $"Department_{index:D5}", "Company_1",
                    index > 0 && index < 10 ? $"Department_{index - 1:D5}" : null);
            await context.SaveChangesAsync();
            var connection = Assert.IsType<SqlConnection>(context.Database.GetDbConnection());
            await connection.OpenAsync();
            connection.StatisticsEnabled = true;
            var io = new List<string>();
            connection.InfoMessage += (_, message) => io.Add(message.Message);
            await using (var enable = connection.CreateCommand())
            {
                enable.CommandText = "SET STATISTICS IO ON";
                await enable.ExecuteNonQueryAsync();
            }
            var registry = new StandardCrudResourceRegistry([CompanyCrudDescriptorFactory.Create(),
                DepartmentCrudDescriptorFactory.Create(), PersonCrudDescriptorFactory.Create()]);
            var reader = new RelationTreeReader(context,
                new RelationTreeSql(context, registry, [new CompanyCrudQueryCustomizer(), new DepartmentCrudQueryCustomizer()]),
                new ScopedTreePlans());
            var definition = ReadRelationDefinition();
            async Task<T> Measure<T>(string operation, Func<Task<T>> run)
            {
                io.Clear();
                connection.ResetStatistics();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var result = await run();
                watch.Stop();
                var stats = connection.RetrieveStatistics();
                output.WriteLine(JsonSerializer.Serialize(new { size, operation, elapsedMs = watch.Elapsed.TotalMilliseconds,
                    roundTrips = stats["ServerRoundtrips"], selectedRows = stats["SelectRows"],
                    bytesReceived = stats["BytesReceived"], io = io.ToArray() }));
                return result;
            }
            Assert.Single(await Measure("roots", () => reader.RootsAsync(definition, default)));
            Assert.Equal(size - 10, (await Measure("children", () => reader.ChildrenAsync(definition, "company:Company_1", default)))!.Count);
            Assert.Null(await Measure("hidden-path", () => reader.PathAsync(definition, "department:Department_00009", default)));
            var search = await Measure("search-page", () => reader.SearchAsync(definition, "Department", 1, 20, default));
            Assert.Equal(size - 10, search.Total);
            Assert.Equal(20, search.Items.Count);
            await using var observer = new SqlConnection(fixture.ConnectionString);
            await observer.OpenAsync();
            await using var plan = observer.CreateCommand();
            plan.CommandText = """
                SELECT TOP (1) CONVERT(nvarchar(max), p.query_plan)
                FROM sys.dm_exec_query_stats s
                CROSS APPLY sys.dm_exec_sql_text(s.sql_handle) t
                CROSS APPLY sys.dm_exec_query_plan(s.plan_handle) p
                WHERE t.text LIKE N'%Reachable AS%' AND t.text LIKE N'%OFFSET%'
                  AND t.text NOT LIKE N'%sys.dm_exec_query_stats%'
                ORDER BY s.last_execution_time DESC
                """;
            var xml = await plan.ExecuteScalarAsync() as string;
            Assert.NotNull(xml);
            var operators = System.Xml.Linq.XDocument.Parse(xml).Descendants()
                .Where(node => node.Name.LocalName == "RelOp")
                .Select(node => (string?)node.Attribute("PhysicalOp")).Where(value => value is not null)
                .Distinct().ToArray();
            output.WriteLine(JsonSerializer.Serialize(new { size, plan = "cached", operators }));
        }
    }

    private sealed class ScopedTreePlans : IDataScopeReadPlanProvider
    {
        public Task<DataScopeReadPlan> GetReadPlanAsync(string resourceKey, CancellationToken cancellationToken = default)
        {
            Expression<Func<Dictionary<string, object?>, bool>>? predicate = resourceKey switch
            {
                "organization.company" => row => EF.Property<string>(row, "EID") != "Company_Hidden",
                "organization.department" => row => EF.Property<string>(row, "EID") != "Department_00000",
                _ => null
            };
            return Task.FromResult(new DataScopeReadPlan(predicate));
        }
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实组织树代表性规模、数据库成本与稳定分页")]
    public async Task OrganizationTreeRepresentativeScaleRecordsActualSqlCost()
    {
        foreach (var size in new[] { 100, 1000, 10000 })
        {
            await using var fixture = await SqlFixture.CreateAsync();
            await using (var setup = CreateContext(fixture.ConnectionString))
            {
                await setup.Database.EnsureCreatedAsync();
                AddCompany(setup, "Company_1");
                for (var index = 0; index < size - 1; index++)
                    AddDepartment(setup, $"Department_{index:D5}", "Company_1", index > 0 && index < 10 ? $"Department_{index - 1:D5}" : null);
                await setup.SaveChangesAsync();
            }
            await using var context = CreateContext(fixture.ConnectionString);
            var connection = Assert.IsType<SqlConnection>(context.Database.GetDbConnection());
            await connection.OpenAsync();
            connection.StatisticsEnabled = true;
            if (size == 10000)
            {
                await using var environment = connection.CreateCommand();
                environment.CommandText = "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)), CAST(SERVERPROPERTY('Edition') AS nvarchar(128)), compatibility_level FROM sys.databases WHERE name=DB_NAME()";
                await using var values = await environment.ExecuteReaderAsync();
                Assert.True(await values.ReadAsync());
                output.WriteLine(JsonSerializer.Serialize(new { environment = new { sqlVersion = values.GetString(0), edition = values.GetString(1), compatibilityLevel = Convert.ToInt32(values.GetValue(2)), dotnet = Environment.Version.ToString(), processorCount = Environment.ProcessorCount } }));
            }
            await using (var initialize = connection.CreateCommand())
            {
                initialize.CommandText = "SELECT 1";
                await initialize.ExecuteScalarAsync();
            }
            // 独立连接读取目标会话累计成本；不会把 observer 往返计进被测 Provider。
            await using var observer = new SqlConnection(fixture.ConnectionString);
            await observer.OpenAsync();
            async Task<long[]> ReadCost()
            {
                await using var command = observer.CreateCommand();
                command.CommandText = "SELECT cpu_time, reads, logical_reads, writes FROM sys.dm_exec_sessions WHERE session_id=@id";
                command.Parameters.AddWithValue("@id", connection.ServerProcessId);
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                return Enumerable.Range(0, 4).Select(index => Convert.ToInt64(reader.GetValue(index))).ToArray();
            }
            var sample = 0;
            async Task<T> Measure<T>(string operation, Func<Task<T>> read)
            {
                var before = await ReadCost(); connection.ResetStatistics();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var result = await read(); watch.Stop();
                var statistics = connection.RetrieveStatistics();
                var after = await ReadCost();
                output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {
                    size, operation, sample, elapsedMs = watch.Elapsed.TotalMilliseconds,
                    roundTrips = statistics["ServerRoundtrips"], rows = statistics["SelectRows"], bytes = statistics["BytesReceived"],
                    serverCpuMs = after[0] - before[0], physicalReads = after[1] - before[1], logicalReads = after[2] - before[2], writes = after[3] - before[3]
                }));
                return result;
            }
            var definition = ReadRelationDefinition();
            var registry = new StandardCrudResourceRegistry([CompanyCrudDescriptorFactory.Create(), DepartmentCrudDescriptorFactory.Create(), PersonCrudDescriptorFactory.Create()]);
            var sql = new RelationTreeSql(context, registry, [new CompanyCrudQueryCustomizer(), new DepartmentCrudQueryCustomizer()]);
            var standard = new RelationTreeReader(context, sql);
            Assert.Single(await Measure("standard-roots", () => standard.RootsAsync(definition, default)));
            Assert.Equal(size - 10, (await Measure("standard-wide-children", () => standard.ChildrenAsync(definition, "company:Company_1", default)))!.Count);
            Assert.Equal(11, (await Measure("standard-path-depth-10", () => standard.PathAsync(definition, "department:Department_00009", default)))!.Count);
            var first = await Measure("standard-search-page-1", () => standard.SearchAsync(definition, "Department", 1, 20, default));
            var second = await Measure("standard-search-page-2", () => standard.SearchAsync(definition, "Department", 2, 20, default));
            var repeated = await Measure("standard-search-page-2-repeat", () => standard.SearchAsync(definition, "Department", 2, 20, default));
            Assert.Equal(size - 1, first.Total); Assert.Equal(20, first.Items.Count);
            Assert.Equal("department:Department_00000", first.Items[0].Node.NodeKey);
            Assert.Equal("department:Department_00020", second.Items[0].Node.NodeKey);
            Assert.Empty(first.Items.Select(item => item.Node.NodeKey).Intersect(second.Items.Select(item => item.Node.NodeKey)));
            Assert.Equal(second.Items.Select(item => item.Node.NodeKey), repeated.Items.Select(item => item.Node.NodeKey));
            // 首轮只作功能与预热证据；保留各样本而不以单次耗时作结论。
            var operations = new (string Name, Func<Task<object>> Standard)[] {
                ("roots", async () => await standard.RootsAsync(definition, default)),
                ("wide-children", async () => (await standard.ChildrenAsync(definition, "company:Company_1", default))!),
                ("path-depth-10", async () => (await standard.PathAsync(definition, "department:Department_00009", default))!),
                ("search-page-1", async () => await standard.SearchAsync(definition, "Department", 1, 20, default)),
                ("search-page-2", async () => await standard.SearchAsync(definition, "Department", 2, 20, default))
            };
            for (sample = 1; sample <= 6; sample++)
                foreach (var operation in operations)
                    await Measure("standard-" + operation.Name, operation.Standard);

        }
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "配置生成 SQL 保留原生上下文类型、复合外键和损坏路径检查")]
    public async Task RelationTree_PreservesTypedContextAndCompositeParentIntegrity()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        await using var context = CreateContext(fixture.ConnectionString);
        await context.Database.EnsureCreatedAsync();
        AddCompany(context, "Company_A"); AddCompany(context, "Company_B");
        AddDepartment(context, "Parent|One", "Company_A", null);
        AddDepartment(context, "Child", "Company_A", "Parent|One");
        context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Person")).Add(new()
        {
            ["EID"] = "Person_1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["BirthDate"] = new DateOnly(2000, 2, 29),
            ["CompanyId"] = null, ["DepartmentId"] = null, ["PositionId"] = null, ["EmploymentStatus"] = "FullTime", ["IsEnabled"] = true,
            ["CreatedBy"] = "User_Admin", ["UpdatedBy"] = "User_Admin", ["CreatedAt"] = DateTime.UtcNow, ["UpdatedAt"] = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var registry = new StandardCrudResourceRegistry([CompanyCrudDescriptorFactory.Create(), DepartmentCrudDescriptorFactory.Create(), PersonCrudDescriptorFactory.Create()]);
        var reader = new RelationTreeReader(context, new RelationTreeSql(context, registry, []));
        var definition = ReadRelationDefinition();
        Assert.Equal(3, (await reader.PathAsync(definition, "department:Child", default))!.Count);
        Assert.Single((await reader.ChildrenAsync(definition, "department:Parent|One", default))!);
        var personTree = definition with { Source = new() { Nodes = [new()
        {
            Key = "person", Model = "Person", ResourceKey = "organization.person", IdField = "EID", LabelField = "ENAME", CodeField = "PersonNo", SortField = "PersonNo",
            ContextFields = new Dictionary<string, string> { ["birthDate"] = "BirthDate", ["enabled"] = "IsEnabled", ["department"] = "DepartmentId" }
        }] } };
        var person = Assert.Single(await reader.RootsAsync(personTree, default));
        Assert.Equal(new DateOnly(2000, 2, 29), Assert.IsType<DateOnly>(person.Context["birthDate"]));
        Assert.True(Assert.IsType<bool>(person.Context["enabled"])); Assert.Null(person.Context["department"]);
        Assert.Equal("2000-02-29", JsonSerializer.SerializeToElement(person.Context).GetProperty("birthDate").GetString());
        // 只在本测试独占库制造复合归属损坏；不能把它投影到另一个公司的父节点下。
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE Department NOCHECK CONSTRAINT ALL; UPDATE Department SET CompanyId=N'Company_B' WHERE EID=N'Child';");
        Assert.Empty((await reader.ChildrenAsync(definition, "department:Parent|One", default))!);
        Assert.False(Assert.Single((await reader.ChildrenAsync(definition, "company:Company_A", default))!).HasChildren);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.PathAsync(definition, "department:Child", default));
        await context.Database.ExecuteSqlRawAsync("UPDATE Department SET ParentDepartmentId=N'missing' WHERE EID=N'Child';");
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.PathAsync(definition, "department:Child", default));
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "人员按部门或公司进入真实配置树，并支持搜索和定位")]
    public async Task RelationTree_PersonsAppearUnderDepartmentOrCompany()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        await using var context = CreateContext(fixture.ConnectionString);
        await context.Database.EnsureCreatedAsync();
        AddCompany(context, "Company_A");
        AddDepartment(context, "Department_A", "Company_A", null);
        foreach (var (id, companyId, departmentId) in new[] {
            ("Person_Department", "Company_A", "Department_A"),
            ("Person_Company", "Company_A", (string?)null),
            ("Person_Unassigned", (string?)null, (string?)null)
        })
            context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Person")).Add(new()
            {
                ["EID"] = id, ["PersonNo"] = id, ["ENAME"] = id,
                ["CompanyId"] = companyId, ["DepartmentId"] = departmentId, ["PositionId"] = null,
                ["EmploymentStatus"] = "FullTime", ["IsEnabled"] = false,
                ["CreatedBy"] = "User_Admin", ["UpdatedBy"] = "User_Admin",
                ["CreatedAt"] = DateTime.UtcNow, ["UpdatedAt"] = DateTime.UtcNow
            });
        await context.SaveChangesAsync();
        var registry = new StandardCrudResourceRegistry([CompanyCrudDescriptorFactory.Create(), DepartmentCrudDescriptorFactory.Create(), PersonCrudDescriptorFactory.Create()]);
        var reader = new RelationTreeReader(context, new RelationTreeSql(context, registry, []));
        var definition = ReadRelationDefinition();
        Assert.Equal("company:Company_A", Assert.Single(await reader.RootsAsync(definition, default)).NodeKey);
        var children = (await reader.ChildrenAsync(definition, "company:Company_A", default))!;
        Assert.Contains(children, node => node.NodeKey == "person:Person_Company");
        Assert.True(children.Single(node => node.NodeType == "department").HasChildren);
        var person = Assert.Single((await reader.ChildrenAsync(definition, "department:Department_A", default))!);
        Assert.Equal("person:Person_Department", person.NodeKey);
        Assert.False(person.HasChildren);
        Assert.False(person.IsEnabled);
        Assert.Equal("Company_A", person.Context["companyId"]);
        Assert.Equal("Department_A", person.Context["departmentId"]);
        Assert.Equal(new[] { "company:Company_A", "department:Department_A", person.NodeKey },
            (await reader.PathAsync(definition, person.NodeKey, default))!.Select(node => node.NodeKey));
        var search = await reader.SearchAsync(definition, "Person_Department", 1, 20, default);
        Assert.Equal(person.NodeKey, Assert.Single(search.Items).Node.NodeKey);
        Assert.Equal(1, search.Total);
        Assert.Equal(2, (await reader.SearchAsync(definition, "Person_", 1, 20, default)).Total);
    }
    [Fact]
    public void RelationTree_RejectsUnreadableFieldsAndNonForeignKeyParents()
    {
        using var context = CreateContext("Server=(localdb)\\MSSQLLocalDB;Database=unused;Integrated Security=true");
        var registry = new StandardCrudResourceRegistry([CompanyCrudDescriptorFactory.Create(), DepartmentCrudDescriptorFactory.Create(), PersonCrudDescriptorFactory.Create()]);
        var compiler = new RelationTreeSql(context, registry, []);
        var definition = ReadRelationDefinition();
        var department = definition.Source.Nodes.Single(node => node.Key == "department");
        foreach (var invalid in new[] {
            department with { LabelField = "CreatedBy" },
            department with { Parents = [new() { NodeType = "company", Field = "ECODE" }] },
            department with { Model = "Person" },
            department with { ContextFields = new Dictionary<string, string> { ["bad"] = "UnknownField" } }
        })
            Assert.Throws<InvalidOperationException>(() => compiler.Build(definition with
                { Source = new() { Nodes = [definition.Source.Nodes[0], invalid] } }));
    }

    private static RelationTreeDefinition ReadRelationDefinition()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "modules/Organization/newp.plugin.json"))) repository = repository.Parent;
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository!.FullName, "modules/Organization/metadata/views/OrganizationStructure.view.json")));
        return new("Organization", "organization.structure", RelationTreeMetadata.Read(document.RootElement.GetProperty("view")));
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 Organization 树四类读取与批量路径")]
    public async Task OrganizationTreeReadsRootsChildrenSearchAndCurrentPath()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        await using (var setup = CreateContext(fixture.ConnectionString))
        {
            await setup.Database.EnsureCreatedAsync();
            AddCompany(setup, "Company_1");
            AddDepartment(setup, "Department_A", "Company_1", null);
            AddDepartment(setup, "Department_B", "Company_1", "Department_A", false);
            await setup.SaveChangesAsync();
        }
        await using var context = CreateContext(fixture.ConnectionString);
        var definition = ReadRelationDefinition();
        var registry = new StandardCrudResourceRegistry([CompanyCrudDescriptorFactory.Create(), DepartmentCrudDescriptorFactory.Create(), PersonCrudDescriptorFactory.Create()]);
        var tree = new RelationTreeReader(context, new RelationTreeSql(context, registry, [new CompanyCrudQueryCustomizer(), new DepartmentCrudQueryCustomizer()]));
        var root = Assert.Single(await tree.RootsAsync(definition, default));
        Assert.Equal("company:Company_1", root.NodeKey);
        Assert.True(root.HasChildren);
        Assert.Equal("department:Department_A", Assert.Single((await tree.ChildrenAsync(definition, root.NodeKey, default))!).NodeKey);
        var path = (await tree.PathAsync(definition, "department:Department_B", default))!;
        Assert.Equal(new[] { "company:Company_1", "department:Department_A", "department:Department_B" }, path.Select(node => node.NodeKey));
        Assert.False(path[^1].IsEnabled);
        Assert.Empty((await tree.ChildrenAsync(definition, "department:Department_B", default))!);
        Assert.Null(await tree.ChildrenAsync(definition, "department:missing", default));
        var search = await tree.SearchAsync(definition, "Department", 2, 1, default);
        Assert.Equal(2, search.Total);
        Assert.Equal("department:Department_B", Assert.Single(search.Items).Node.NodeKey);
        Assert.Equal(3, search.Items[0].Path.Count);
        var departments = context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Department"));
        var moved = await departments.SingleAsync(row => EF.Property<string>(row, "EID") == "Department_B");
        moved["ParentDepartmentId"] = null;
        await context.SaveChangesAsync();
        Assert.Equal(new[] { "company:Company_1", "department:Department_B" }, (await tree.PathAsync(definition, "department:Department_B", default))!.Select(node => node.NodeKey));
        departments.Remove(moved); await context.SaveChangesAsync();
        Assert.Null(await tree.PathAsync(definition, "department:Department_B", default));
        Assert.Empty((await tree.SearchAsync(definition, "absent", 1, 20, default)).Items);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tree.SearchAsync(definition, "Department", 1, 20, cancelled.Token));
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server 上的部门层级固定查询次数")]
    [Trait("Category", "SqlServerSmoke")]
    public async Task DepartmentHierarchyUsesFixedQueryCountAtDepthSixtyFour()
    {
        var fixture = await SqlFixture.CreateAsync();
        await using (fixture)
        {
            await using (var setup = CreateContext(fixture.ConnectionString))
            {
                await setup.Database.EnsureCreatedAsync();
                AddCompany(setup, "Company_1");
                string? parentId = null;
                for (var index = 0; index < 64; index++)
                {
                    var id = $"Department_{index:D3}";
                    AddDepartment(setup, id, "Company_1", parentId);
                    parentId = id;
                }

                AddDepartment(setup, "Department_Peer", "Company_1", null);

                await setup.SaveChangesAsync();
            }

            await using var context = CreateContext(fixture.ConnectionString);
            var connection = Assert.IsType<SqlConnection>(context.Database.GetDbConnection());
            var validator = await CreateValidatorAsync(context);
            connection.StatisticsEnabled = true;
            var error = await validator.ValidateUpdateAsync(
                DepartmentCrudDescriptorFactory.Create(),
                "Department_000",
                new Dictionary<string, object?> { ["ParentDepartmentId"] = "Department_063" });

            Assert.Equal("Organization.Department.ParentCycle", error?.Code);
            Assert.Equal(3L, connection.RetrieveStatistics()["SelectCount"]);

            connection.ResetStatistics();
            var page = await new EfDepartmentDirectory(context).LookupParentCandidatesAsync(
                "Company_1",
                "Department_000",
                new OrganizationDirectoryLookupRequest(null, 1, 20));

            Assert.Equal("Department_Peer", Assert.Single(page.Items).Id);
            Assert.Equal(2L, connection.RetrieveStatistics()["SelectCount"]);
        }
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "事务内目标链在真实 SQL Server 以单个递归查询返回标量结果")]
    [Trait("Category", "SqlServerSmoke")]
    public async Task EnabledTargetChainUsesOneDatabaseRoundTripAtDepthSixtyFour()
    {
        await using var fixture = await SqlFixture.CreateAsync();
        await using var context = CreateContext(fixture.ConnectionString);
        await context.Database.EnsureCreatedAsync();
        AddCompany(context, "Company_1");
        string? parentId = null;
        for (var index = 0; index < 64; index++)
        {
            var id = $"Department_{index:D3}";
            AddDepartment(context, id, "Company_1", parentId);
            parentId = id;
        }
        await context.SaveChangesAsync();
        var connection = Assert.IsType<SqlConnection>(context.Database.GetDbConnection());
        connection.StatisticsEnabled = true;

        var transactions = new OrganizationFactsTestServices.TransactionState();
        var facts = new OrganizationEffectiveFacts(transactions,
            new PublishedOrganizationFacts(new RecordingOrganizationQuery()),
            new SqlOrganizationFacts(context, new SqlServerDepartmentHierarchyQuery(context)));
        Assert.False(await facts.IsDepartmentChainEnabledAsync("Company_1", parentId!, default));
        Assert.Equal(0L, connection.RetrieveStatistics()["SelectCount"]);

        transactions.IsActive = true;
        var enabled = await facts.IsDepartmentChainEnabledAsync("Company_1", parentId!, default);

        Assert.True(enabled);
        Assert.Equal(1L, connection.RetrieveStatistics()["SelectCount"]);
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server 上超过 1024 个部门的层级操作")]
    [Trait("Category", "SqlServerSmoke")]
    public async Task DepartmentHierarchyAboveOneThousandTwentyFourKeepsReparentAndPagedLookupAvailable()
    {
        var fixture = await SqlFixture.CreateAsync();
        await using (fixture)
        {
            await using (var setup = CreateContext(fixture.ConnectionString))
            {
                await setup.Database.EnsureCreatedAsync();
                AddCompany(setup, "Company_1");
                for (var index = 0; index <= 1024; index++)
                {
                    AddDepartment(
                        setup,
                        $"Department_{index:D4}",
                        "Company_1",
                        parentDepartmentId: null,
                        enabled: index != 0);
                }

                await setup.SaveChangesAsync();
            }

            await using var context = CreateContext(fixture.ConnectionString);
            var error = await (await CreateValidatorAsync(context)).ValidateUpdateAsync(
                DepartmentCrudDescriptorFactory.Create(),
                "Department_0000",
                new Dictionary<string, object?> { ["ParentDepartmentId"] = "Department_1024" });
            var page = await new EfDepartmentDirectory(context).LookupParentCandidatesAsync(
                "Company_1",
                "Department_0000",
                new OrganizationDirectoryLookupRequest(
                    "Department_10",
                    2,
                    5,
                    ["Department_0000"]));

            Assert.Null(error);
            Assert.Equal(5, page.Items.Count);
            Assert.True(page.HasMore);
            Assert.Equal("Department_0000", Assert.Single(page.CurrentItems).Id);
            Assert.False(page.CurrentItems[0].IsEnabled);
        }
    }

    [EnvironmentGatedFact(RunEnvironmentVariable, "真实 SQL Server 上损坏部门环的失败关闭")]
    [Trait("Category", "SqlServerSmoke")]
    public async Task CorruptedDepartmentCycleFailsClosedForReparentAndParentLookup()
    {
        var fixture = await SqlFixture.CreateAsync();
        await using (fixture)
        {
            await using (var setup = CreateContext(fixture.ConnectionString))
            {
                await setup.Database.EnsureCreatedAsync();
                AddCompany(setup, "Company_1");
                AddDepartment(setup, "Department_Current", "Company_1", null);
                AddDepartment(setup, "Department_A", "Company_1", null);
                AddDepartment(setup, "Department_B", "Company_1", null);
                await setup.SaveChangesAsync();

                var departments = setup.Set<Dictionary<string, object?>>(setup.ResolveModelKey("Department"));
                var departmentA = await departments.SingleAsync(
                    row => EF.Property<string>(row, "EID") == "Department_A");
                var departmentB = await departments.SingleAsync(
                    row => EF.Property<string>(row, "EID") == "Department_B");
                departmentA["ParentDepartmentId"] = "Department_B";
                departmentB["ParentDepartmentId"] = "Department_A";
                await setup.SaveChangesAsync();
            }

            await using var context = CreateContext(fixture.ConnectionString);
            var error = await (await CreateValidatorAsync(context)).ValidateUpdateAsync(
                DepartmentCrudDescriptorFactory.Create(),
                "Department_Current",
                new Dictionary<string, object?> { ["ParentDepartmentId"] = "Department_A" });
            var exception = await Assert.ThrowsAsync<DepartmentHierarchyCorruptedException>(() =>
                new EfDepartmentDirectory(context).LookupParentCandidatesAsync(
                    "Company_1",
                    "Department_A",
                    new OrganizationDirectoryLookupRequest(null, 1, 20)));

            Assert.Equal(409, error?.StatusCode);
            Assert.Equal(DepartmentHierarchyErrors.Corrupted, error?.Code);
            Assert.Equal(DepartmentHierarchyErrors.Corrupted, exception.Code);
        }
    }

    private static async Task<DepartmentCrudValidator> CreateValidatorAsync(EfPlatformDbContext context)
    {
        var companies = await context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Company")).AsNoTracking()
            .Select(row => new ICP.Platform.Modules.Organization.Models.Company { Id = EF.Property<string>(row, "EID"), IsEnabled = EF.Property<bool>(row, "IsEnabled") }).ToListAsync();
        var departments = await context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Department")).AsNoTracking()
            .Select(row => new ICP.Platform.Modules.Organization.Models.Department { Id = EF.Property<string>(row, "EID"), CompanyId = EF.Property<string>(row, "CompanyId"),
                ParentDepartmentId = EF.Property<string?>(row, "ParentDepartmentId"), IsEnabled = EF.Property<bool>(row, "IsEnabled") }).ToListAsync();
        return new DepartmentCrudValidator(context, new SqlServerDepartmentHierarchyQuery(context),
            OrganizationFactsTestServices.Published(new RecordingOrganizationQuery(companies, departments)));
    }
    private static EfPlatformDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlServer(connectionString)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>();

        var definitions = new OrganizationModelMetadataProvider().GetDefinitions()
            .Select(static definition => definition with { OwnerModuleId = "Organization", SchemaName = "dbo" })
            .ToArray();
        return new EfPlatformDbContext(options.Options, definitions);
    }

    private static void AddCompany(EfPlatformDbContext context, string eid) =>
        context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Company")).Add(new Dictionary<string, object?>
        {
            ["EID"] = eid,
            ["ECODE"] = eid,
            ["ENAME"] = eid,
            ["IsEnabled"] = true,
            ["SortOrder"] = "0100",
            ["CreatedBy"] = "User_Admin",
            ["CreatedAt"] = DateTime.UtcNow,
            ["UpdatedBy"] = "User_Admin",
            ["UpdatedAt"] = DateTime.UtcNow
        });

    private static void AddDepartment(
        EfPlatformDbContext context,
        string eid,
        string companyId,
        string? parentDepartmentId,
        bool enabled = true) =>
        context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Department")).Add(new Dictionary<string, object?>
        {
            ["EID"] = eid,
            ["CompanyId"] = companyId,
            ["ParentDepartmentId"] = parentDepartmentId,
            ["ECODE"] = eid,
            ["ENAME"] = eid,
            ["IsEnabled"] = enabled,
            ["SortOrder"] = "0100",
            ["CreatedBy"] = "User_Admin",
            ["CreatedAt"] = DateTime.UtcNow,
            ["UpdatedBy"] = "User_Admin",
            ["UpdatedAt"] = DateTime.UtcNow
        });

    private sealed class SqlFixture(
        string adminConnectionString,
        string catalog,
        string connectionString) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public static async Task<SqlFixture> CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable(AdminConnectionEnvironmentVariable)
                ?? "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true";
            var admin = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" }.ConnectionString;
            var catalog = $"NewPDepartmentHierarchyQueries_{Guid.NewGuid():N}";
            await using var connection = new SqlConnection(admin);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{catalog}]";
            await command.ExecuteNonQueryAsync();
            return new SqlFixture(
                admin,
                catalog,
                new SqlConnectionStringBuilder(admin) { InitialCatalog = catalog }.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"IF DB_ID(N'{catalog}') IS NOT NULL BEGIN ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{catalog}]; END";
            await command.ExecuteNonQueryAsync();
        }
    }
}
