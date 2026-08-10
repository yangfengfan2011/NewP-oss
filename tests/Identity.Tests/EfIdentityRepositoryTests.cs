using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Models;
using ICP.Platform.Modules.Identity.Persistence.Descriptors;
using ICP.Platform.Modules.Identity.Persistence.Ef;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Projection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace ICP.Platform.Identity.Tests;

public sealed class EfIdentityRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsAggregateAndConditionallyClaimsRevision()
    {
        var options = CreateOptions();
        await using var context = new EfPlatformDbContext(options, IdentityModelDefinitions.All);
        var repository = new EfIdentityRepository(context);
        var createdAt = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
        var user = new User
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
        repository.AddUser(user);
        repository.AddCredential(new LocalCredential
        {
            EID = "LocalCredential_1",
            UserEID = user.EID,
            PasswordHash = "hash-only",
            IsTemporary = true,
            CreatedBy = "actor",
            CreatedAt = createdAt,
            UpdatedBy = "actor",
            UpdatedAt = createdAt
        });
        await context.SaveChangesAsync();

        var loaded = Assert.IsType<User>(await repository.FindUserByNormalizedCodeAsync("ALICE"));
        loaded.ENAME = "Changed";
        loaded.Revision = 2;

        Assert.True(await repository.TryReplaceUserAsync(loaded, 1));
        await context.SaveChangesAsync();
        Assert.False(await repository.TryReplaceUserAsync(loaded, 1));
        Assert.Equal("Changed", (await repository.FindUserAsync(user.EID))!.ENAME);
        Assert.Equal("hash-only", (await repository.FindCredentialAsync(user.EID))!.PasswordHash);
    }

    [Fact]
    public async Task Repository_RemovesCredentialButKeepsUserTombstoneAndSecurityEvent()
    {
        var options = CreateOptions();
        await using var context = new EfPlatformDbContext(options, IdentityModelDefinitions.All);
        var repository = new EfIdentityRepository(context);
        var now = new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc);
        repository.AddUser(new User
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
            UserEID = "User_1",
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
            TargetUserEID = "User_1",
            EventType = "AccountDeleted",
            Reason = "retired",
            OccurredAt = now,
            TraceId = "trace",
            ActorKind = IdentitySecurityActorKind.User,
            ActorUserEID = "User_1",
            CreatedBy = "User_1",
            CreatedAt = now,
            UpdatedBy = "User_1",
            UpdatedAt = now
        });
        await context.SaveChangesAsync();

        await repository.RemoveCredentialAsync("User_1");
        await context.SaveChangesAsync();

        Assert.Null(await repository.FindCredentialAsync("User_1"));
        Assert.NotNull(await repository.FindUserAsync("User_1"));
        var securityEvent = Assert.Single(await repository.SearchSecurityEventsAsync("User_1"));
        Assert.Equal("AccountDeleted", securityEvent.EventType);
        Assert.Equal("retired", securityEvent.Reason);
        Assert.Equal(IdentitySecurityActorKind.User, securityEvent.ActorKind);
        Assert.DoesNotContain("never-a-secret", securityEvent.Reason, StringComparison.Ordinal);
    }

    private static DbContextOptions<EfPlatformDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .Options;
}
