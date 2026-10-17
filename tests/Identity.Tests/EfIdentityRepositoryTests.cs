using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Modules.Identity.Persistence.Ef;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Query.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ICP.Platform.Identity.Tests;

public sealed class EfIdentityRepositoryTests
{
    [Fact]
    public async Task AccountDirectorySearch_FiltersOrdersAndLimitsCandidatesInSql()
    {
        var sql = new List<string>();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>().UseSqlite(connection)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .LogTo(sql.Add, LogLevel.Information).Options;
        await using var context = new EfPlatformDbContext(options, IdentityModelDefinitions.All);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfIdentityRepository(context);
        repository.AddAccount(Account("a", "alpha", IdentityAccountStatus.Enabled, true));
        repository.AddAccount(Account("b", "ALPHABET", IdentityAccountStatus.Disabled, true));
        repository.AddAccount(Account("c", "alphabetical", IdentityAccountStatus.Deleted, true));
        repository.AddAccount(Account("d", "ALPINE", IdentityAccountStatus.Enabled, false));
        await context.SaveChangesAsync();
        sql.Clear();
        var directory = new IdentityAccountDirectory(repository);
        var results = await directory.SearchAsync(new IdentityAccountSearch { Query = "  ALP  ", IsPersonnelBound = true, Limit = 1 });
        Assert.Equal("a", Assert.Single(results).AccountEid);
        var command = Assert.Single(sql, entry => entry.Contains("Executed DbCommand", StringComparison.Ordinal));
        Assert.Contains("WHERE", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", command, StringComparison.OrdinalIgnoreCase);
        var deleted = await directory.SearchAsync(new IdentityAccountSearch { Status = IdentityAccountStatus.Deleted });
        Assert.Equal("c", Assert.Single(deleted).AccountEid);
    }

    [Fact]
    public async Task ManagementSearch_ExecutesFilterCountStableSortAndPagingInSql()
    {
        var sql = new List<string>();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .LogTo(sql.Add, LogLevel.Information)
            .Options;
        await using var context = new EfPlatformDbContext(options, IdentityModelDefinitions.All);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfIdentityRepository(context);
        repository.AddAccount(Account("User_1", "A01", IdentityAccountStatus.Enabled, isPersonnelBound: true));
        repository.AddAccount(Account("User_2", "A02", IdentityAccountStatus.Disabled, isPersonnelBound: true));
        repository.AddAccount(Account("User_3", "A03", IdentityAccountStatus.Deleted, isPersonnelBound: true));
        repository.AddAccount(Account("User_4", "B01", IdentityAccountStatus.Enabled, isPersonnelBound: false));
        await context.SaveChangesAsync();
        sql.Clear();

        var result = await IdentityCrudQueryTestFactory.Create(
                repository,
                new EfPlatformQueryExecutor(context),
                new TestTemporalDisplayProjectionExecutor(), context)
            .SearchAsync(new IdentityManagementAccountSearch(
                Query: "A",
                Status: null,
                IsPersonnelBound: true,
                IsLocalAdministrator: false,
                Page: 2,
                PageSize: 1));

        Assert.Equal(2, result.TotalCount);
        Assert.Equal("A02", Assert.Single(result.Items).ECODE);
        Assert.Equal("System", result.Items[0].CreatedBy_DisplayValue);
        Assert.NotNull(result.Items[0].CreatedAt_DisplayValue);
        var executedSql = string.Join(Environment.NewLine, sql);
        Assert.Contains("COUNT", executedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", executedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", executedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", executedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", executedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Status", executedSql, StringComparison.Ordinal);
        Assert.Contains("IsPersonnelBound", executedSql, StringComparison.Ordinal);
        Assert.Contains("IsLocalAdministrator", executedSql, StringComparison.Ordinal);
        Assert.DoesNotContain("NormalizedECODE", executedSql, StringComparison.Ordinal);

        var queryService = IdentityCrudQueryTestFactory.Create(repository, new EfPlatformQueryExecutor(context), new TestTemporalDisplayProjectionExecutor(), context);
        var controller = new ICP.Platform.Modules.Identity.Controllers.IdentityAccountsController(queryService, null!);
        var response = await controller.GetSnapshot("User_2", CancellationToken.None);
        var payload = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(response.Result).Value;
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        Assert.Equal("system", json.RootElement.GetProperty("createdBy").GetString());
        Assert.Equal("System", json.RootElement.GetProperty("CreatedBy_DisplayValue").GetString());
        Assert.Equal(result.Items[0].CreatedAt_DisplayValue, json.RootElement.GetProperty("CreatedAt_DisplayValue").GetString());
    }

    [Fact]
    public async Task Repository_RoundTripsAggregateAndConditionallyClaimsRevision()
    {
        var options = CreateOptions();
        await using var context = new EfPlatformDbContext(options, IdentityModelDefinitions.All);
        var repository = new EfIdentityRepository(context);
        var createdAt = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
        var account = new IdentityAccount
        {
            EID = "User_1",
            ECODE = "alice",
            NormalizedECODE = "ALICE",
            ENAME = "Alice",
            Status = IdentityAccountStatus.Enabled,
            Revision = 1,
            CreatedBy = "actor",
            CreatedAt = createdAt,
            UpdatedBy = "actor",
            UpdatedAt = createdAt
        };
        repository.AddAccount(account);
        repository.AddCredential(new LocalCredential
        {
            EID = "LocalCredential_1",
            AccountEID = account.EID,
            PasswordHash = "hash-only",
            IsTemporary = true,
            CreatedBy = "actor",
            CreatedAt = createdAt,
            UpdatedBy = "actor",
            UpdatedAt = createdAt
        });
        await context.SaveChangesAsync();

        var loaded = Assert.IsType<IdentityAccount>(await repository.FindAccountByNormalizedCodeAsync("ALICE"));
        loaded.ENAME = "Changed";
        loaded.Revision = 2;

        Assert.True(await repository.TryReplaceAccountAsync(loaded, 1));
        await context.SaveChangesAsync();
        Assert.False(await repository.TryReplaceAccountAsync(loaded, 1));
        Assert.Equal("Changed", (await repository.FindAccountAsync(account.EID))!.ENAME);
        Assert.Equal("hash-only", (await repository.FindCredentialAsync(account.EID))!.PasswordHash);
    }

    [Fact]
    public async Task Repository_RemovesCredentialButKeepsUserTombstoneAndSecurityEvent()
    {
        var options = CreateOptions();
        await using var context = new EfPlatformDbContext(options, IdentityModelDefinitions.All);
        var repository = new EfIdentityRepository(context);
        var now = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
        repository.AddAccount(new IdentityAccount
        {
            EID = "User_1",
            ECODE = "alice",
            NormalizedECODE = "ALICE",
            ENAME = "Alice",
            Status = IdentityAccountStatus.Deleted,
            Revision = 2,
            CreatedBy = "actor",
            CreatedAt = now,
            UpdatedBy = "actor",
            UpdatedAt = now
        });
        repository.AddCredential(new LocalCredential
        {
            EID = "LocalCredential_1",
            AccountEID = "User_1",
            PasswordHash = "never-a-secret",
            IsTemporary = true,
            CreatedBy = "actor",
            CreatedAt = now,
            UpdatedBy = "actor",
            UpdatedAt = now
        });
        repository.AddSecurityEvent(new IdentitySecurityEvent
        {
            EID = "IdentitySecurityEvent_1",
            TargetAccountEID = "User_1",
            EventType = "AccountDeleted",
            Reason = "retired",
            OccurredAt = now,
            TraceId = "trace",
            ActorKind = IdentitySecurityActorKind.IdentityAccount,
            ActorAccountEID = "User_1",
            CreatedBy = "User_1",
            CreatedAt = now,
            UpdatedBy = "User_1",
            UpdatedAt = now
        });
        await context.SaveChangesAsync();

        await repository.RemoveCredentialAsync("User_1");
        await context.SaveChangesAsync();

        Assert.Null(await repository.FindCredentialAsync("User_1"));
        Assert.NotNull(await repository.FindAccountAsync("User_1"));
        var securityEvent = Assert.Single(await repository.SearchSecurityEventsAsync("User_1"));
        Assert.Equal("AccountDeleted", securityEvent.EventType);
        Assert.Equal("retired", securityEvent.Reason);
        Assert.Equal(IdentitySecurityActorKind.IdentityAccount, securityEvent.ActorKind);
        Assert.DoesNotContain("never-a-secret", securityEvent.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repository_FindAccountsByIdsAsync_UsesOneSqlCommandAndKeepsDeletedResultsForDirectoryToFilter()
    {
        var sql = new List<string>();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .LogTo(sql.Add, LogLevel.Information)
            .Options;
        await using var context = new EfPlatformDbContext(options, IdentityModelDefinitions.All);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfIdentityRepository(context);
        repository.AddAccount(Account("User_1", "alice", IdentityAccountStatus.Enabled, isPersonnelBound: false));
        repository.AddAccount(Account("User_2", "bob", IdentityAccountStatus.Deleted, isPersonnelBound: false));
        await context.SaveChangesAsync();
        sql.Clear();

        var results = await repository.FindAccountsByIdsAsync(["User_1", "User_2", "User_1", " ", "User_Missing"]);

        Assert.Equal(["User_1", "User_2"], results.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(IdentityAccountStatus.Enabled, results["User_1"].Status);
        Assert.Equal(IdentityAccountStatus.Deleted, results["User_2"].Status);
        Assert.Equal(1, sql.Count(static entry => entry.Contains("Executed DbCommand", StringComparison.Ordinal)));
    }

    private static DbContextOptions<EfPlatformDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;

    private static IdentityAccount Account(
        string eid,
        string code,
        IdentityAccountStatus status,
        bool isPersonnelBound) => new()
    {
        EID = eid,
        ECODE = code,
        NormalizedECODE = code,
        ENAME = code,
        Status = status,
        IsLocalAdministrator = false,
        IsPersonnelBound = isPersonnelBound,
        PersonnelDisableHold = false,
        Revision = 1,
        CreatedBy = "actor",
        CreatedAt = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc),
        UpdatedBy = "actor",
        UpdatedAt = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc)
    };
}
