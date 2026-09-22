using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Authorization.Services;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public sealed class DataScopeIdentityRegistrationTests
{
    [Fact]
    public async Task Resolution_IsOncePerReferencedProvider_AndPropagatesFaults()
    {
        var used = new Provider("custom");
        var unused = new Provider("unused") { Fail = true };
        var identities = new DataScopeIdentities([used, unused]);
        var resolved = await identities.ResolveAsync("trusted-account", ["custom", "custom"], ["custom.current"], default);
        Assert.Equal(1, used.Calls);
        Assert.Equal(0, unused.Calls);
        Assert.True(resolved.Matches("custom", "trusted-account", null));
        Assert.False(resolved.Matches("custom", "other", null));
        Assert.Equal("trusted-account", resolved.ContextValue("custom.current"));
        used.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => identities.ResolveAsync("trusted-account", ["custom"], [], default));
    }

    [Fact]
    public void RegistrationAndOptions_RejectAmbiguityAndUnknownReferences()
    {
        Assert.Throws<InvalidOperationException>(() => new DataScopeIdentities([new Provider("same"), new Provider("same")]));
        var identities = new DataScopeIdentities([new Provider("custom")]);
        Assert.Throws<RolePermissionValidationException>(() => identities.Validate("missing", "x", null));
        Assert.Throws<RolePermissionValidationException>(() => identities.Validate("custom", null, null));
        Assert.Throws<RolePermissionValidationException>(() => identities.Validate("custom", "x", new(true)));
    }

    private sealed class Provider(string key) : IDataScopeIdentityProvider
    {
        public IReadOnlyList<DataScopeSubjectType> SubjectTypes => [new(key, key, true)];
        public IReadOnlyList<DataScopeContextValue> ContextValues => [new(key + ".current", key, "String", "test")];
        internal int Calls { get; private set; }
        internal bool Fail { get; set; }
        public Task<IDataScopeIdentityFacts> ResolveAsync(string accountId, CancellationToken token)
        {
            Calls++;
            if (Fail) throw new InvalidOperationException("facts unavailable");
            return Task.FromResult<IDataScopeIdentityFacts>(new Facts(accountId));
        }
        public Task<DataScopeSubjectPage> SearchAsync(string type, string? query, int page, IReadOnlyList<string> currentIds, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class Facts(string account) : IDataScopeIdentityFacts
    {
        public bool Matches(string type, string? id, DataScopeMatchOptions options) => id == account;
        public string? ContextValue(string key) => account;
    }
}
