using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Transactions;
using ICP.Platform.Persistence.Transactions;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Crud.Abstractions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using ICP.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;
using static ICP.Platform.HostApi.Tests.FeatureHttpTestRequests;

namespace ICP.Platform.HostApi.Tests;

public sealed class OrganizationSnapshotSqlServerHttpApiTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "人员别名绑定按实际持久化 EID 登记缓存")]
    public Task Binding_WithPersonEidAlias_RegistersPersistedIdentity() => DedicatedWriteCaseVariantAsync(true);

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "人员别名启停按实际持久化 EID 登记缓存")]
    public Task Lifecycle_WithPersonEidAlias_RegistersPersistedIdentity() => DedicatedWriteCaseVariantAsync(false);

    private async Task DedicatedWriteCaseVariantAsync(bool binding)
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var canonical = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "PERSON-ALIAS", ENAME = "人员别名", EmploymentStatus = "Active", IsEnabled = true }));
        var alias = canonical.ToLowerInvariant();
        Assert.False(string.Equals(canonical, alias, StringComparison.Ordinal));
        output.WriteLine($"canonical={canonical}; alias={alias}");
        string? accountId = null;
        HttpResponseMessage response;
        if (binding)
        {
            var account = (await CreateAsync(admin, http.Identity("create"), "/api/identity/accounts", new { ECODE = "canonical-target", ENAME = "规范标识账号" })).GetProperty("account");
            accountId = account.GetProperty("eid").GetString()!;
            response = await SendAsync(admin, http.Organization("persons", "linkUser", "detail.custom"), HttpMethod.Put,
                $"/api/organization/persons/{alias}/user-binding", new { accountEid = accountId, expectedRevision = account.GetProperty("revision").GetInt64(), reason = "实际标识" });
        }
        else response = await SendAsync(admin, http.Organization("persons", "disable", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/persons/{alias}/status/disable", new { isEnabled = false, reason = "实际标识" });
        using (response)
        {
            output.WriteLine($"PUT status={(int)response.StatusCode}; body={await response.Content.ReadAsStringAsync()}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var stored = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey("Person")).AsNoTracking()
            .SingleAsync(row => EF.Property<string>(row, "EID") == canonical);
        Assert.Equal(canonical, stored["EID"]);
        var snapshot = await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync();
        Assert.Equal(stored.OrderBy(p => p.Key), snapshot.Persons[canonical].OrderBy(p => p.Key));
        Assert.False(snapshot.Persons.ContainsKey(alias));
        if (binding)
        {
            Assert.Equal(accountId, stored["AccountEID"]);
            Assert.Equal(canonical, snapshot.PersonByAccount[accountId!]);
        }
        else Assert.Equal(false, stored["IsEnabled"]);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "公司条件删除使用实际命中的 EID 维护缓存")]
    public Task CompanyDelete_WithCaseVariantEid_RemovesCanonicalCacheRecord() => DeleteCaseVariantAsync("Company", "companies");

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "部门条件删除使用实际命中的 EID 维护缓存")]
    public Task DepartmentDelete_WithCaseVariantEid_RemovesCanonicalCacheRecord() => DeleteCaseVariantAsync("Department", "departments");

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "岗位条件删除使用实际命中的 EID 维护缓存")]
    public Task PositionDelete_WithCaseVariantEid_RemovesCanonicalCacheRecord() => DeleteCaseVariantAsync("Position", "positions");

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "人员条件删除使用实际命中的 EID 维护缓存")]
    public Task PersonDelete_WithCaseVariantEid_RemovesCanonicalCacheRecord() => DeleteCaseVariantAsync("Person", "persons");

    private async Task DeleteCaseVariantAsync(string model, string tab)
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        object data;
        if (model == "Department")
        {
            var company = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("companies", "create", "list.pageActions"),
                "/api/organization/companies", new { ECODE = "ALIAS-PARENT", ENAME = "部门所属公司", SortOrder = "01" }));
            data = new { ECODE = "ALIAS", ENAME = "无引用部门", CompanyId = company, SortOrder = "01" };
        }
        else data = model switch
        {
            "Company" => new { ECODE = "ALIAS", ENAME = "无引用公司", SortOrder = "01" },
            "Position" => new { ENAME = "无引用岗位" },
            "Person" => new { PersonNo = "ALIAS", ENAME = "未绑定人员", EmploymentStatus = "Active", IsEnabled = true },
            _ => throw new ArgumentOutOfRangeException(nameof(model))
        };
        var canonical = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization(tab, "create", "list.pageActions"),
            $"/api/organization/{tab}", data));
        var alias = canonical.ToLowerInvariant();
        Assert.False(string.Equals(canonical, alias, StringComparison.Ordinal));
        output.WriteLine($"canonical={canonical}; alias={alias}");
        output.WriteLine($"canonicalUtf8={Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(canonical))}; aliasUtf8={Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(alias))}");

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        await db.Database.OpenConnectionAsync();
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = "SELECT c.collation_name FROM sys.columns c JOIN sys.tables t ON c.object_id=t.object_id WHERE t.name=@model AND c.name=N'EID'";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@model";
            parameter.Value = model;
            command.Parameters.Add(parameter);
            output.WriteLine($"actualEidCollation={await command.ExecuteScalarAsync()}");
        }
        var rows = db.Set<Dictionary<string, object?>>(db.ResolveModelKey(model)).AsNoTracking();
        Assert.True(await rows.AnyAsync(row => EF.Property<string>(row, "EID") == alias));
        using (var deleted = await SendAsync(admin, http.Organization(tab, "delete", "list.rowActions"), HttpMethod.Delete,
            $"/api/organization/{tab}/{alias}"))
        {
            output.WriteLine($"DELETE status={(int)deleted.StatusCode}; body={await deleted.Content.ReadAsStringAsync()}");
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }
        using (var detail = await SendAsync(admin, http.Organization(tab), HttpMethod.Get, $"/api/organization/{tab}/{canonical}"))
        {
            output.WriteLine($"GET canonical status={(int)detail.StatusCode}; body={await detail.Content.ReadAsStringAsync()}");
            Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        }
        Assert.False(await rows.AnyAsync(row => EF.Property<string>(row, "EID") == canonical));
        var snapshot = await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync();
        var records = model switch
        {
            "Company" => snapshot.Companies, "Department" => snapshot.Departments,
            "Position" => snapshot.Positions, "Person" => snapshot.Persons,
            _ => throw new ArgumentOutOfRangeException(nameof(model))
        };
        output.WriteLine($"canonicalStillCached={records.ContainsKey(canonical)}");
        Assert.False(records.ContainsKey(canonical));
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "条件删除执行器单独调用仍进入同一组织提交管道")]
    public async Task ConditionalExecutors_WhenCalledDirectly_MaintainRegisteredCache()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var company = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("companies", "create", "list.pageActions"),
            "/api/organization/companies", new { ECODE = "DIRECT", ENAME = "直接删除公司", SortOrder = "01" }));
        var department = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("departments", "create", "list.pageActions"),
            "/api/organization/departments", new { ECODE = "DIRECT", ENAME = "直接删除部门", CompanyId = company, SortOrder = "01" }));
        var position = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("positions", "create", "list.pageActions"),
            "/api/organization/positions", new { ENAME = "直接删除岗位" }));
        var person = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "DIRECT", ENAME = "直接删除人员", EmploymentStatus = "Active", IsEnabled = true }));
        await using var scope = fixture.Services.CreateAsyncScope();
        var executors = scope.ServiceProvider.GetServices<IStandardCrudConditionalDeleteExecutor>().ToDictionary(e => e.ResourceKey);
        foreach (var (descriptor, id) in new[] { (PersonCrudDescriptorFactory.Create(), person), (PositionCrudDescriptorFactory.Create(), position),
            (DepartmentCrudDescriptorFactory.Create(), department), (CompanyCrudDescriptorFactory.Create(), company) })
            Assert.True((await executors[descriptor.ResourceKey].DeleteAsync(descriptor, id.ToLowerInvariant())).Succeeded);
        var snapshot = await fixture.Services.GetRequiredService<OrganizationCache>().ReadAsync();
        Assert.Empty(snapshot.Companies);
        Assert.Empty(snapshot.Departments);
        Assert.Empty(snapshot.Positions);
        Assert.Empty(snapshot.Persons);
        Assert.Empty(snapshot.DepartmentsByCompany);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "绑定提交后发布失败准确报告已保存")]
    public Task BindingApi_ReportsCommittedPublicationFailure() => DedicatedApis_ReportCommittedPublicationFailure(true);

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "生命周期提交后发布失败准确报告已保存")]
    public Task LifecycleApi_ReportsCommittedPublicationFailure() => DedicatedApis_ReportCommittedPublicationFailure(false);

    private static async Task DedicatedApis_ReportCommittedPublicationFailure(bool binding)
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync(services => services.AddSingleton<OrganizationCache, FailedPublicationCache>());
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var person = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "FAIL", ENAME = "提交后故障", EmploymentStatus = "Active", IsEnabled = true }));
        var cache = (FailedPublicationCache)fixture.Services.GetRequiredService<OrganizationCache>();
        HttpResponseMessage response;
        if (binding)
        {
            var account = (await CreateAsync(admin, http.Identity("create"), "/api/identity/accounts", new { ECODE = "failure", ENAME = "账号" })).GetProperty("account");
            cache.Fail = true;
            response = await SendAsync(admin, http.Organization("persons", "linkUser", "detail.custom"), HttpMethod.Put,
                $"/api/organization/persons/{person}/user-binding", new { accountEid = account.GetProperty("eid").GetString(), expectedRevision = account.GetProperty("revision").GetInt64(), reason = "发布失败" });
        }
        else
        {
            cache.Fail = true;
            response = await SendAsync(admin, http.Organization("persons", "disable", "list.rowActions"), HttpMethod.Put,
                $"/api/organization/persons/{person}/status/disable", new { isEnabled = false, reason = "发布失败" });
        }
        using (response)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Persistence.CommittedCacheUnavailable", problem.GetProperty("code").GetString());
            Assert.Contains("已保存", problem.GetProperty("message").GetString());
        }
        await Assert.ThrowsAsync<PlatformCacheUnavailableException>(() => cache.ReadAsync().AsTask());
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        var stored = await db.Set<Dictionary<string, object?>>("Organization/Person").AsNoTracking().SingleAsync();
        if (binding) Assert.NotNull(stored["AccountEID"]);
        else Assert.Equal(false, stored["IsEnabled"]);
        Assert.Equal(1, cache.FailedPublications);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "四表同一读事务与装载/并发局部发布协调")]
    public async Task InitialLoad_IsOneConsistentTransactionAndCannotOverwriteWaitingWriters()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var company = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("companies", "create", "list.pageActions"),
            "/api/organization/companies", new { ECODE = "LOAD", ENAME = "装载公司", SortOrder = "01" }));
        using (var disabled = await SendAsync(admin, http.Organization("companies", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/companies/{company}", new { IsEnabled = false }))
            Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        var person = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "LOAD", ENAME = "装载人员", EmploymentStatus = "Active", IsEnabled = false }));
        await using var sourceScope = fixture.Services.CreateAsyncScope();
        var connection = sourceScope.ServiceProvider.GetRequiredService<EfPlatformDbContext>().Database.GetConnectionString()!;
        var reads = new LoadTransactions();
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>().UseSqlServer(connection).AddInterceptors(reads)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options;
        var definitions = OrganizationModelDefinitions.All.Select(d => d with { OwnerModuleId = "Organization" }).ToArray();
        var services = new ServiceCollection();
        services.AddScoped(_ => new EfPlatformDbContext(options, definitions));
        await using var provider = services.BuildServiceProvider();
        var cache = new PausedLoadCache(provider.GetRequiredService<IServiceScopeFactory>());
        var loading = cache.InitializeAsync().AsTask();
        await cache.Loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(4, reads.Transactions.Count);
        Assert.All(reads.Isolations, level => Assert.Equal(IsolationLevel.Serializable, level));
        Assert.All(reads.Transactions, transaction => Assert.Same(reads.Transactions[0], transaction));
        await using var firstDb = new EfPlatformDbContext(options, definitions);
        await using var secondDb = new EfPlatformDbContext(options, definitions);
        var registrations = new[] { "Company", "Department", "Position", "Person" }
            .Select(model => new PlatformCommitRegistration($"Organization/{model}", cache)).ToArray();
        var first = new EfPlatformTransactionExecutor(firstDb, registrations);
        var second = new EfPlatformTransactionExecutor(secondDb, registrations);
        var entered = false;
        Task<bool> FirstWrite() => first.ExecuteAsync(["Company"], async token =>
        {
            entered = true;
            var row = await firstDb.Set<Dictionary<string, object?>>("Organization/Company").SingleAsync(token);
            row["ENAME"] = "已更新公司";
            return true;
        }).AsTask();
        Task<bool> SecondWrite() => second.ExecuteAsync(["Person"], async token =>
        {
            var row = await secondDb.Set<Dictionary<string, object?>>("Organization/Person").SingleAsync(token);
            row["ENAME"] = "已更新人员";
            return true;
        }).AsTask();
        // 未通过首次完整校验前，业务写入直接不可用，不把待执行业务挂在初始化队列上。
        await Assert.ThrowsAsync<PlatformCacheUnavailableException>(() => FirstWrite());
        await Assert.ThrowsAsync<PlatformCacheUnavailableException>(() => SecondWrite());
        Assert.False(entered);
        cache.Release.TrySetResult();
        await loading;
        await Task.WhenAll(FirstWrite(), SecondWrite());
        var result = await cache.ReadAsync();
        Assert.Equal("已更新公司", result.Companies[company]["ENAME"]);
        Assert.Equal("已更新人员", result.Persons[person]["ENAME"]);
        Assert.Equal(false, result.Companies[company]["IsEnabled"]);
        Assert.Equal(false, result.Persons[person]["IsEnabled"]);
        Assert.Equal("装载公司", cache.Initial!.Companies[company]["ENAME"]);
        Assert.Equal(1, cache.Loads);
        await AssertAllStoredFields(fixture, cache);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "永久绑定和人员生命周期通过专用 HTTP 事务维护组织快照")]
    public async Task BindingAndLifecycleApis_PublishActualRowsAndRetainPermanentBinding()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var cache = fixture.Services.GetRequiredService<OrganizationCache>();
        var person = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("persons", "create", "list.pageActions"),
            "/api/organization/persons", new { PersonNo = "BOUND", ENAME = "准备阶段人员", EmploymentStatus = "Active", IsEnabled = true }));
        var account = (await CreateAsync(admin, http.Identity("create"), "/api/identity/accounts", new { ECODE = "bound", ENAME = "账号" })).GetProperty("account");
        var accountId = account.GetProperty("eid").GetString()!;
        var beforeBinding = await cache.ReadAsync();
        fixture.Fault.Mode = "commit";
        using (var failed = await SendAsync(admin, http.Organization("persons", "linkUser", "detail.custom"), HttpMethod.Put,
            $"/api/organization/persons/{person}/user-binding", new { accountEid = accountId, expectedRevision = account.GetProperty("revision").GetInt64(), reason = "应回滚" }))
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        fixture.Fault.Mode = "";
        Assert.Same(beforeBinding, await cache.ReadAsync());
        Assert.Null((await cache.ReadAsync()).Persons[person]["AccountEID"]);
        using var binding = await SendAsync(admin, http.Organization("persons", "linkUser", "detail.custom"), HttpMethod.Put,
            $"/api/organization/persons/{person}/user-binding", new { accountEid = accountId, expectedRevision = account.GetProperty("revision").GetInt64(), reason = "正式绑定" });
        Assert.True(binding.StatusCode == HttpStatusCode.OK, await binding.Content.ReadAsStringAsync());
        var bound = await cache.ReadAsync();
        Assert.Equal(accountId, bound.Persons[person]["AccountEID"]);
        Assert.Equal(person, bound.PersonByAccount[accountId]);
        Assert.Equal("正式绑定", bound.Persons[person]["AccountBindingReason"]);
        Assert.Equal(fixture.ActorId, bound.Persons[person]["AccountBoundBy"]);
        Assert.Null(bound.Persons[person]["CompanyId"]);
        var company = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("companies", "create", "list.pageActions"),
            "/api/organization/companies", new { ECODE = "BOUND", ENAME = "绑定人员公司", SortOrder = "01" }));
        var department = BusinessAuditSqlServerHttpApiTests.Id(await CreateAsync(admin, http.Organization("departments", "create", "list.pageActions"),
            "/api/organization/departments", new { ECODE = "BOUND", ENAME = "绑定人员部门", CompanyId = company, SortOrder = "01" }));
        using (var assigned = await SendAsync(admin, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/persons/{person}", new { CompanyId = company, DepartmentId = department }))
            Assert.True(assigned.StatusCode == HttpStatusCode.OK, await assigned.Content.ReadAsStringAsync());
        var assignedSnapshot = await cache.ReadAsync();
        Assert.Equal([person], assignedSnapshot.PersonsByDepartment[department]);
        Assert.Equal(person, assignedSnapshot.PersonByAccount[accountId]);
        Assert.Equal("正式绑定", assignedSnapshot.Persons[person]["AccountBindingReason"]);
        fixture.Fault.Mode = "commit";
        using (var failed = await SendAsync(admin, http.Organization("persons", "disable", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/persons/{person}/status/disable", new { isEnabled = false, reason = "应回滚" }))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.Equal("ORGANIZATION_PERSON_LIFECYCLE_DEPENDENCY_UNAVAILABLE", (await failed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        fixture.Fault.Mode = "";
        Assert.Same(assignedSnapshot, await cache.ReadAsync());
        foreach (var enabled in new[] { false, true })
        {
            var action = enabled ? "enable" : "disable";
            using var response = await SendAsync(admin, http.Organization("persons", action, "list.rowActions"), HttpMethod.Put,
                $"/api/organization/persons/{person}/status/{action}", new { isEnabled = enabled, reason = "生命周期" });
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var snapshot = await cache.ReadAsync();
            Assert.Equal(enabled, snapshot.Persons[person]["IsEnabled"]);
            Assert.Equal(person, snapshot.PersonByAccount[accountId]);
            Assert.Equal(fixture.ActorId, snapshot.Persons[person]["UpdatedBy"]);
            Assert.Equal("正式绑定", snapshot.Persons[person]["AccountBindingReason"]);
        }
        using var denied = await SendAsync(admin, http.Organization("persons", "delete", "list.rowActions"), HttpMethod.Delete,
            $"/api/organization/persons/{person}");
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Equal(accountId, (await cache.ReadAsync()).Persons[person]["AccountEID"]);
        await AssertAllStoredFields(fixture, cache);
    }

    [EnvironmentGatedFact("NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION", "完整组织真实 HTTP 写入自动维护档案与关系")]
    public async Task OrganizationApis_MaintainCompleteRecordsAndRelationshipsForAllWrites()
    {
        await using var fixture = await BusinessAuditHostFixture.CreateAsync();
        using var admin = await fixture.LoginAdministratorAsync();
        var http = new FeatureHttpTestRequests(fixture);
        var cache = fixture.Services.GetRequiredService<OrganizationCache>();
        var company = await Create("companies", new { ECODE = "C", ENAME = "缓存公司", SortOrder = "01" });
        var department = await Create("departments", new { ECODE = "D", ENAME = "缓存部门", CompanyId = company, SortOrder = "01" });
        var position = await Create("positions", new { ENAME = "缓存岗位", Description = "完整备注" });
        var person = await Create("persons", new { PersonNo = "P", ENAME = "缓存人员", EmploymentStatus = "Active", IsEnabled = true,
            CompanyId = company, DepartmentId = department, PositionId = position, BirthDate = "2000-02-29" });
        var initial = await cache.ReadAsync();
        Assert.Equal("完整备注", initial.Positions[position]["Description"]);
        Assert.Equal(new DateOnly(2000, 2, 29), initial.Persons[person]["BirthDate"]);
        Assert.Equal([person], initial.PersonsByDepartment[department]);
        Assert.Equal([department], initial.DepartmentsByCompany[company]);

        var otherCompany = await Create("companies", new { ECODE = "C2", ENAME = "调入公司", SortOrder = "02" });
        var parent = await Create("departments", new { ECODE = "PARENT", ENAME = "上级部门", CompanyId = otherCompany, SortOrder = "01" });
        var otherDepartment = await Create("departments", new { ECODE = "D", ENAME = "调入部门", CompanyId = otherCompany, ParentDepartmentId = parent, SortOrder = "02" });
        var otherPosition = await Create("positions", new { ENAME = "调入岗位", Description = "另一岗位" });
        var beforeMove = await cache.ReadAsync();
        fixture.Fault.Commands.Clear();
        await Update("persons", person, new { CompanyId = otherCompany, DepartmentId = otherDepartment, PositionId = otherPosition, ENAME = "调动后人员", EmploymentEndedAt = "2027-01-01T00:00:00Z" });
        var moved = await cache.ReadAsync();
        Assert.Equal([person], moved.PersonsByCompany[otherCompany]);
        Assert.Equal([person], moved.PersonsByDepartment[otherDepartment]);
        Assert.Equal([person], moved.PersonsByPosition[otherPosition]);
        Assert.False(moved.PersonsByCompany.ContainsKey(company));
        Assert.False(moved.PersonsByDepartment.ContainsKey(department));
        Assert.False(moved.PersonsByPosition.ContainsKey(position));
        Assert.Equal(company, beforeMove.Persons[person]["CompanyId"]);
        Assert.Same(beforeMove.Departments, moved.Departments);
        Assert.Same(beforeMove.Companies, moved.Companies);
        Assert.Equal(new DateOnly(2000, 2, 29), moved.Persons[person]["BirthDate"]);
        Assert.Equal(new DateTime(2027, 1, 1), moved.Persons[person]["EmploymentEndedAt"]);
        Assert.Equal(fixture.ActorId, moved.Persons[person]["UpdatedBy"]);
        Assert.DoesNotContain(fixture.Fault.Commands, sql => sql.Contains("FROM [Person]") && !sql.Contains("WHERE"));

        // 数据库提交故障发生于候选准备之后，正式接口必须回滚记录并保留原缓存对象。
        fixture.Fault.Mode = "commit";
        using (var failed = await SendAsync(admin, http.Organization("persons", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/persons/{person}", new { DepartmentId = parent }))
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        fixture.Fault.Mode = "";
        Assert.Same(moved, await cache.ReadAsync());
        Assert.Equal(otherDepartment, (await ReadAsync(admin, http.Organization("persons"), $"/api/organization/persons/{person}"))
            .GetProperty("fields").GetProperty("DepartmentId").GetString());

        await Update("departments", otherDepartment, new { ParentDepartmentId = (string?)null, ENAME = "已换父" });
        Assert.False((await cache.ReadAsync()).ChildDepartments.ContainsKey(parent));
        Assert.Equal([otherDepartment], moved.ChildDepartments[parent]);
        await Update("departments", otherDepartment, new { ParentDepartmentId = parent });
        Assert.Equal([otherDepartment], (await cache.ReadAsync()).ChildDepartments[parent]);
        using (var disabled = await SendAsync(admin, http.Organization("companies", "edit", "list.rowActions"), HttpMethod.Put,
            $"/api/organization/companies/{company}", new { IsEnabled = false }))
        {
            Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
            Assert.False((await disabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fields").GetProperty("IsEnabled").GetBoolean());
        }
        var companyDisabled = await cache.ReadAsync();
        Assert.Equal(false, companyDisabled.Companies[company]["IsEnabled"]);
        Assert.Equal(true, companyDisabled.Departments[department]["IsEnabled"]);
        Assert.Equal([department], companyDisabled.DepartmentsByCompany[company]);
        await Update("departments", department, new { IsEnabled = false });
        foreach (var (tab, id) in new[] { ("companies", company), ("positions", position) })
        {
            await Update(tab, id, new { IsEnabled = false });
            await Update(tab, id, new { IsEnabled = true });
        }
        await Update("departments", department, new { IsEnabled = true });
        await Update("positions", otherPosition, new { IsEnabled = false, Description = "停用但保留引用" });
        Assert.Equal(false, (await cache.ReadAsync()).Positions[otherPosition]["IsEnabled"]);
        Assert.Equal([person], (await cache.ReadAsync()).PersonsByPosition[otherPosition]);
        await AssertAllStoredFields(fixture, cache);

        await Delete("persons", person);
        Assert.False((await cache.ReadAsync()).Persons.ContainsKey(person));
        Assert.False((await cache.ReadAsync()).PersonsByDepartment.ContainsKey(otherDepartment));
        await Delete("positions", otherPosition);
        await Delete("departments", otherDepartment);
        Assert.False((await cache.ReadAsync()).ChildDepartments.ContainsKey(parent));
        await Delete("departments", parent);
        await Delete("companies", otherCompany);
        await Delete("positions", position);
        Assert.False((await cache.ReadAsync()).Positions.ContainsKey(position));
        await Delete("departments", department);
        Assert.False((await cache.ReadAsync()).Departments.ContainsKey(department));
        await Delete("companies", company);
        Assert.Empty((await cache.ReadAsync()).Companies);
        Assert.True(initial.Persons.ContainsKey(person));

        async Task<string> Create(string tab, object data) => BusinessAuditSqlServerHttpApiTests.Id(
            await CreateAsync(admin, http.Organization(tab, "create", "list.pageActions"), $"/api/organization/{tab}", data));
        async Task Delete(string tab, string id)
        {
            using var response = await SendAsync(admin, http.Organization(tab, "delete", "list.rowActions"), HttpMethod.Delete, $"/api/organization/{tab}/{id}");
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }
        async Task Update(string tab, string id, object data)
        {
            using var response = await SendAsync(admin, http.Organization(tab, "edit", "list.rowActions"), HttpMethod.Put, $"/api/organization/{tab}/{id}", data);
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }
    }

    private static async Task AssertAllStoredFields(BusinessAuditHostFixture fixture, OrganizationCache cache)
    {
        var snapshot = await cache.ReadAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
        foreach (var (model, records) in new[] { ("Company", snapshot.Companies), ("Department", snapshot.Departments), ("Position", snapshot.Positions), ("Person", snapshot.Persons) })
        {
            var stored = await db.Set<Dictionary<string, object?>>(db.ResolveModelKey(model)).AsNoTracking().ToListAsync();
            Assert.Equal(stored.Count, records.Count);
            foreach (var row in stored)
                Assert.Equal(row.OrderBy(p => p.Key), records[(string)row["EID"]!].OrderBy(p => p.Key));
        }
    }

    private sealed class PausedLoadCache(IServiceScopeFactory scopes) : OrganizationCache(scopes)
    {
        public TaskCompletionSource Loaded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public OrganizationSnapshot? Initial;
        public int Loads;
        protected override async ValueTask<OrganizationSnapshot> LoadAsync(CancellationToken cancellationToken)
        {
            Loads++;
            Initial = await base.LoadAsync(cancellationToken);
            Loaded.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return Initial;
        }
    }

    private sealed class FailedPublicationCache(IServiceScopeFactory scopes) : OrganizationCache(scopes)
    {
        public bool Fail;
        public int FailedPublications;
        protected override void Publish(OrganizationSnapshot snapshot)
        {
            if (Fail) { FailedPublications++; throw new InvalidOperationException("publication failure"); }
            base.Publish(snapshot);
        }
        protected override async ValueTask<IReadOnlyList<PlatformRecordChange>> ReloadAsync(IReadOnlyList<PlatformRecordChange> changes, CancellationToken token)
        {
            // 该回归只检查首次业务提交的反馈；恢复独立测试，这里保持故障直到fixture停止。
            if (Fail) await Task.Delay(Timeout.Infinite, token);
            return await base.ReloadAsync(changes, token);
        }
    }

    private sealed class LoadTransactions : DbCommandInterceptor
    {
        public List<DbTransaction?> Transactions { get; } = [];
        public List<IsolationLevel> Isolations { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Transactions.Add(command.Transaction);
            Isolations.Add(command.Transaction?.IsolationLevel ?? IsolationLevel.Unspecified);
            return ValueTask.FromResult(result);
        }
    }
}
