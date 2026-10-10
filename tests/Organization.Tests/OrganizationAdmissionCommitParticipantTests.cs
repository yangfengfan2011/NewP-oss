using ICP.Platform.Authentication.Contracts;
using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Services;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class OrganizationAdmissionCommitParticipantTests
{
    [Fact]
    public async Task Prepare_UsesChangedPersonAndSnapshotRelationsForContinuousMove()
    {
        await using var provider = await CreateProviderAsync();
        var cache = new OrganizationCache(provider.GetRequiredService<IServiceScopeFactory>());
        await cache.InitializeAsync();
        var before = (await cache.ReadAsync()).Persons["person"];
        var after = before.ToDictionary();
        after["DepartmentId"] = "target-leaf";
        after["UpdatedBy"] = "actor";
        var revoker = new RecordingBatchRevoker();
        var participant = new OrganizationAdmissionCommitParticipant(cache, revoker);

        await using var scope = await participant.BeginAsync(default);
        await scope.PrepareAsync([new("Person", "person", before, after)], default);

        Assert.Empty(revoker.Accounts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prepare_TreatsUnassignedAndDeletedPersonAsIneligibleOverlay(bool delete)
    {
        await using var provider = await CreateProviderAsync();
        var cache = new OrganizationCache(provider.GetRequiredService<IServiceScopeFactory>());
        await cache.InitializeAsync();
        var before = (await cache.ReadAsync()).Persons["person"];
        IReadOnlyDictionary<string, object?>? after = null;
        if (!delete)
        {
            var unassigned = before.ToDictionary();
            unassigned["CompanyId"] = null;
            unassigned["DepartmentId"] = null;
            unassigned["UpdatedBy"] = "actor";
            after = unassigned;
        }
        var revoker = new RecordingBatchRevoker();
        var participant = new OrganizationAdmissionCommitParticipant(cache, revoker);

        await using var scope = await participant.BeginAsync(default);
        await scope.PrepareAsync([new("Person", "person", before, after)], default);

        Assert.Equal(["account"], revoker.Accounts);
        Assert.Equal("organization-admission-interrupted", revoker.Reason);
    }

    private static async Task<ServiceProvider> CreateProviderAsync()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;
        var definitions = OrganizationModelDefinitions.All
            .Select(definition => definition with { OwnerModuleId = "Organization" })
            .ToArray();
        var services = new ServiceCollection();
        services.AddScoped(_ => new EfPlatformDbContext(options, definitions));
        var provider = services.BuildServiceProvider();
        await using var db = new EfPlatformDbContext(options, definitions);
        db.Set<Dictionary<string, object?>>("Company").Add(Company("company"));
        foreach (var (id, parent) in new[]
        {
            ("root", (string?)null),
            ("source-parent", "root"),
            ("source-leaf", "source-parent"),
            ("target-parent", "root"),
            ("target-leaf", "target-parent")
        })
        {
            db.Set<Dictionary<string, object?>>("Department").Add(Department(id, parent));
        }
        db.Set<Dictionary<string, object?>>("Person").Add(Person());
        await db.SaveChangesAsync();
        return provider;
    }

    private static Dictionary<string, object?> Company(string id) => new()
    {
        ["EID"] = id,
        ["ECODE"] = id,
        ["ENAME"] = id,
        ["SortOrder"] = "01",
        ["IsEnabled"] = true
    };

    private static Dictionary<string, object?> Department(string id, string? parent) => new()
    {
        ["EID"] = id,
        ["ECODE"] = id,
        ["ENAME"] = id,
        ["CompanyId"] = "company",
        ["ParentDepartmentId"] = parent,
        ["SortOrder"] = "01",
        ["IsEnabled"] = true
    };

    private static Dictionary<string, object?> Person() => new()
    {
        ["EID"] = "person",
        ["PersonNo"] = "person",
        ["ENAME"] = "person",
        ["EmploymentStatus"] = "Active",
        ["CompanyId"] = "company",
        ["DepartmentId"] = "source-leaf",
        ["AccountEID"] = "account",
        ["IsEnabled"] = true,
        ["UpdatedBy"] = "actor"
    };

    private sealed class RecordingBatchRevoker : IAuthenticationSessionBatchRevoker
    {
        public IReadOnlyCollection<string> Accounts { get; private set; } = [];
        public string? Reason { get; private set; }

        public ValueTask<int> RevokeAccountsAsync(
            IReadOnlyCollection<string> accountEids,
            string reason,
            string actorEid,
            CancellationToken cancellationToken = default)
        {
            Accounts = accountEids.ToArray();
            Reason = reason;
            return ValueTask.FromResult(accountEids.Count);
        }
    }
}
