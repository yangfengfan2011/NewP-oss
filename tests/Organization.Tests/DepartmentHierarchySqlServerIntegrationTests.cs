using System.Data.Common;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Persistence.Ef;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class DepartmentHierarchySqlServerIntegrationTests
{
    private const string RunEnvironmentVariable = "NEWP_RUN_ORGANIZATION_SQLSERVER_INTEGRATION";
    private const string AdminConnectionEnvironmentVariable = "NEWP_ORGANIZATION_SQLSERVER_ADMIN_CONNECTION";

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

                await setup.SaveChangesAsync();
            }

            var interceptor = new DepartmentQueryCounter();
            await using var context = CreateContext(fixture.ConnectionString, interceptor);
            var error = await new DepartmentCrudValidator(context).ValidateUpdateAsync(
                DepartmentCrudDescriptorFactory.Create(),
                "Department_000",
                new Dictionary<string, object?> { ["ParentDepartmentId"] = "Department_063" });

            Assert.Equal("Organization.Department.ParentCycle", error?.Code);
            Assert.Equal(3, interceptor.ReaderCount);

            interceptor.Reset();
            var page = await new EfDepartmentDirectory(context).LookupParentCandidatesAsync(
                "Company_1",
                "Department_000",
                new OrganizationDirectoryLookupRequest(null, 1, 20));

            Assert.Empty(page.Items);
            Assert.Equal(2, interceptor.ReaderCount);
        }
    }

    private static EfPlatformDbContext CreateContext(
        string connectionString,
        DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlServer(connectionString)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>();
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

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
        string? parentDepartmentId) =>
        context.Set<Dictionary<string, object?>>(context.ResolveModelKey("Department")).Add(new Dictionary<string, object?>
        {
            ["EID"] = eid,
            ["CompanyId"] = companyId,
            ["ParentDepartmentId"] = parentDepartmentId,
            ["ECODE"] = eid,
            ["ENAME"] = eid,
            ["IsEnabled"] = true,
            ["SortOrder"] = "0100",
            ["CreatedBy"] = "User_Admin",
            ["CreatedAt"] = DateTime.UtcNow,
            ["UpdatedBy"] = "User_Admin",
            ["UpdatedAt"] = DateTime.UtcNow
        });

    private sealed class DepartmentQueryCounter : DbCommandInterceptor
    {
        public int ReaderCount { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[Department]", StringComparison.Ordinal))
            {
                ReaderCount++;
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public void Reset() => ReaderCount = 0;
    }

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
