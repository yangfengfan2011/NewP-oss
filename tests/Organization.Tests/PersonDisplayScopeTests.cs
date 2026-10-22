using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Organization.Crud;
using ICP.Platform.Modules.Organization.DTOs;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class PersonDisplayScopeTests
{
    [Fact]
    public async Task BindingSummary_HidesUnreadableAccountDetailsAndPreservesPersonBindingFacts()
    {
        var query = new RecordingOrganizationQuery(persons:
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "Person_1", ["ENAME"] = "张三", ["IsEnabled"] = true,
                ["AccountEID"] = "User_1", ["AccountBindingReason"] = "入职"
            }
        ]);
        var directory = new AccountDirectory();
        var readable = new ReadableIds();
        var provider = new PersonUserBindingSummaryProvider(query, directory, readable);
        var descriptor = OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create());
        var detail = new Dictionary<string, object?> { ["EID"] = "Person_1" };

        var hidden = Assert.IsType<PersonUserBindingSummaryDto>((await provider.BuildAsync(descriptor, detail)).Data);
        Assert.Equal("Person_1", hidden.PersonEid);
        Assert.Equal("User_1", hidden.AccountEid);
        Assert.Equal("入职", hidden.Reason);
        Assert.False(hidden.IsAccountReadable);
        Assert.True(hidden.IsAccountAvailable);
        Assert.Null(hidden.Account);
        Assert.Equal("identity.account", readable.ResourceKey);

        readable.Allowed = true;
        var visible = Assert.IsType<PersonUserBindingSummaryDto>((await provider.BuildAsync(descriptor, detail)).Data);
        Assert.True(visible.IsAccountReadable);
        Assert.Equal("private-login", visible.Account!.ECODE);
        Assert.Equal("私有账号", visible.Account.ENAME);
    }

    [Fact]
    public async Task BindingSummary_PropagatesAuthorizationFailure()
    {
        var query = new RecordingOrganizationQuery(persons:
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "Person_1", ["ENAME"] = "张三", ["IsEnabled"] = true,
                ["AccountEID"] = "User_1"
            }
        ]);
        var readable = new ReadableIds { Failure = new DataScopeReadExecutionException("identity.account", new IOException("offline")) };
        var provider = new PersonUserBindingSummaryProvider(query, new AccountDirectory(), readable);
        await Assert.ThrowsAsync<DataScopeReadExecutionException>(() => provider.BuildAsync(
            OrganizationCrudTestResources.Resolve(PersonCrudDescriptorFactory.Create()),
            new Dictionary<string, object?> { ["EID"] = "Person_1" }));
    }

    private sealed class ReadableIds : IDataScopeReadableIds
    {
        public bool Allowed { get; set; }
        public string? ResourceKey { get; private set; }
        public Exception? Failure { get; init; }

        /// <inheritdoc/>
        public Task<IReadOnlySet<string>> GetReadableIdsAsync(string resourceKey, IReadOnlyCollection<string> objectIds,
            CancellationToken cancellationToken = default)
        {
            ResourceKey = resourceKey;
            return Failure is not null
                ? Task.FromException<IReadOnlySet<string>>(Failure)
                : Task.FromResult<IReadOnlySet<string>>(Allowed ? objectIds.ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal));
        }
    }

    private sealed class AccountDirectory : IIdentityAccountDirectory
    {
        /// <inheritdoc/>
        public ValueTask<IdentityAccountSnapshot?> FindAsync(string accountEid, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IdentityAccountSnapshot?>(new IdentityAccountSnapshot
            {
                AccountEid = accountEid, ECODE = "private-login", ENAME = "私有账号", Status = IdentityAccountStatus.Enabled, Revision = 7
            });

        /// <inheritdoc/>
        public ValueTask<IReadOnlyDictionary<string, IdentityAccountSnapshot>> FindManyAsync(
            IReadOnlyCollection<string> accountEids, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(
            IdentityAccountSearch search, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
