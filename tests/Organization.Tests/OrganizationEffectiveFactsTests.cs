using ICP.Platform.Modules.Organization.Caching;
using ICP.Platform.Modules.Organization.Directories;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Organization.Contracts;
using ICP.Platform.Persistence.Abstractions;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class OrganizationEffectiveFactsTests
{
    [Fact]
    public async Task PublishedFacts_KeepUnavailableAndCancellationFailuresIncludingEmptyBatch()
    {
        var failure = new InvalidOperationException("published organization unavailable");
        var facts = OrganizationFactsTestServices.Published(new UnavailableQuery(failure));
        Func<CancellationToken, Task>[] reads =
        [
            token => facts.FindCompanyAsync("company", token),
            token => facts.FindCompaniesAsync([], token),
            token => facts.FindCompanyByCodeAsync("company", token),
            token => facts.IsCompanyEnabledAsync("company", token),
            token => facts.IsDepartmentChainEnabledAsync("company", "department", token)
        ];
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        foreach (var read in reads)
        {
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => read(default)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read(cancellation.Token));
        }
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("cycle")]
    public async Task PublishedTargetChain_RejectsInvalidAncestorWithoutReadingSql(string kind)
    {
        var root = new Department { Id = "root", CompanyId = kind == "foreign" ? "other" : "company",
            IsEnabled = kind != "disabled", ParentDepartmentId = kind == "cycle" ? "leaf" : null };
        var leaf = new Department { Id = "leaf", CompanyId = "company", IsEnabled = true, ParentDepartmentId = "root" };
        var query = new RecordingOrganizationQuery(
            [new Company { Id = "company", IsEnabled = true }], kind == "missing" ? [leaf] : [root, leaf]);

        Assert.False(await OrganizationFactsTestServices.Published(query)
            .IsDepartmentChainEnabledAsync("company", "leaf", default));
        Assert.Equal(1, query.Reads);
    }

    private sealed class UnavailableQuery(Exception failure) : IOrganizationQuery
    {
        public ValueTask<IOrganizationReadView> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw failure;
        }
    }
}

internal static class OrganizationFactsTestServices
{
    // 规则单测显式装配已发布来源；若误切到 SQL 就立即失败，不保留生产可选依赖。
    internal static OrganizationEffectiveFacts Published(IOrganizationQuery query) =>
        new(new TransactionState(), new PublishedOrganizationFacts(query), new UnexpectedSqlFacts());

    internal sealed class TransactionState : IPlatformTransactionExecutor
    {
        public bool IsActive { get; set; }
        public ValueTask<TResult> ExecuteAsync<TResult>(Func<CancellationToken, ValueTask<TResult>> registerChanges,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnexpectedSqlFacts : IOrganizationFactsSource
    {
        public Task<IReadOnlyDictionary<string, object?>?> FindCompanyAsync(string id, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Published facts must not fall back to SQL.");
        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FindCompaniesAsync(
            IReadOnlyCollection<string> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, object?>?> FindCompanyByCodeAsync(string code, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> IsCompanyEnabledAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsDepartmentChainEnabledAsync(string companyId, string departmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
