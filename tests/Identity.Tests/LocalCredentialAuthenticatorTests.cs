using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Identity.Application;
using ICP.Platform.Modules.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace ICP.Platform.Identity.Tests;

public sealed class LocalCredentialAuthenticatorTests
{
    [Fact]
    public async Task AuthenticateAsync_UsesNormalizedECODEAndReturnsOnlyNonSecretAccountFacts()
    {
        const string password = "real password";
        var repository = new InMemoryIdentityRepository();
        var user = User("user-1", IdentityAccountStatus.Enabled);
        repository.Users.Add(user);
        repository.Credentials.Add(new LocalCredential
        {
            EID = "credential-1",
            UserEID = user.EID,
            PasswordHash = new PasswordHasher<User>().HashPassword(user, password),
            IsTemporary = true
        });
        var authenticator = new LocalCredentialAuthenticator(repository, new PasswordHasher<User>());

        var result = await authenticator.AuthenticateAsync("  admin  ", password);

        Assert.Equal(LocalCredentialAuthenticationStatus.Succeeded, result.Status);
        Assert.Equal("user-1", result.Account?.UserEid);
        Assert.True(result.IsTemporary);
        Assert.DoesNotContain(password, System.Text.Json.JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(IdentityAccountStatus.Disabled, "real password")]
    [InlineData(IdentityAccountStatus.Deleted, "real password")]
    [InlineData(IdentityAccountStatus.Enabled, "wrong password")]
    public async Task AuthenticateAsync_DoesNotRevealWhetherAccountOrCredentialFailed(
        IdentityAccountStatus status,
        string suppliedPassword)
    {
        var repository = new InMemoryIdentityRepository();
        var user = User("user-1", status);
        repository.Users.Add(user);
        repository.Credentials.Add(new LocalCredential
        {
            EID = "credential-1",
            UserEID = user.EID,
            PasswordHash = new PasswordHasher<User>().HashPassword(user, "real password")
        });
        var authenticator = new LocalCredentialAuthenticator(repository, new PasswordHasher<User>());

        var result = await authenticator.AuthenticateAsync("admin", suppliedPassword);

        Assert.Equal(LocalCredentialAuthenticationStatus.Invalid, result.Status);
        Assert.Null(result.Account);
    }

    private static User User(string eid, IdentityAccountStatus status) => new()
    {
        EID = eid,
        ECODE = "admin",
        NormalizedECODE = "ADMIN",
        ENAME = "Administrator",
        Status = status,
        Revision = 1
    };
}
