using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Modules.Identity.Persistence.Ef;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class OrganizationOwnershipTests
{
    [Fact]
    public void OrganizationModule_RegistersTrustedOwnershipDirectory()
    {
        var services = new ServiceCollection();
        new OrganizationModule().ConfigureServices(services);
        Assert.Contains(services, registration => registration.ServiceType == typeof(IAccountOwnershipDirectory));
    }

    [Fact]
    public async Task TrustedDirectory_LeavesUninitializedAdministratorUnassignedAndNeverInventsUnknownAccounts()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().Options;
        var definitions = OrganizationModelDefinitions.All.Concat(IdentityModelDefinitions.All).ToArray();
        var services = new ServiceCollection();
        new OrganizationModule().ConfigureServices(services);
        services.AddScoped(_ => new EfPlatformDbContext(options, definitions));
        services.AddScoped<IIdentityAccountDirectory>(provider =>
            new IdentityAccountDirectory(new EfIdentityRepository(provider.GetRequiredService<EfPlatformDbContext>())));
        services.AddSingleton<IDataScopeReadableIds, ReadableIds>();
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EfPlatformDbContext>();
            db.Set<Dictionary<string, object?>>("IdentityAccount").Add(new()
            {
                ["EID"] = "administrator", ["ECODE"] = "admin", ["NormalizedECODE"] = "ADMIN", ["ENAME"] = "管理员",
                ["Status"] = "Enabled", ["IsLocalAdministrator"] = true, ["IsPersonnelBound"] = false,
                ["PersonnelDisableHold"] = false, ["Revision"] = 1L
            });
            await db.SaveChangesAsync();
        }
        await provider.GetRequiredService<Caching.OrganizationCache>().InitializeAsync();
        await using var readScope = provider.CreateAsyncScope();
        var directory = readScope.ServiceProvider.GetRequiredService<IAccountOwnershipDirectory>();
        var accounts = await directory.FindManyAsync(["administrator", "unknown"]);
        Assert.Null(Assert.Single(accounts).Value.DepartmentId);
        Assert.Equal("administrator", accounts["administrator"].AccountId);
        await using (var failedCommit = await provider.GetRequiredService<Caching.OrganizationCache>().BeginAsync(default))
            failedCommit.MarkUnavailable();
        await Assert.ThrowsAsync<ICP.Platform.Persistence.Transactions.PlatformCacheUnavailableException>(
            () => directory.FindManyAsync(["administrator"]).AsTask());
    }

    private sealed class ReadableIds : IDataScopeReadableIds
    {
        public Task<IReadOnlySet<string>> GetReadableIdsAsync(string resourceKey, IReadOnlyCollection<string> objectIds,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(objectIds.ToHashSet());
    }
}
