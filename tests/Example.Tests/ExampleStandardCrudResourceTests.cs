using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Planning;
using ICP.Platform.Identity.Contracts;
using ICP.Platform.Modules.Example.Crud;
using ICP.Platform.Modules.Example.Persistence.Descriptors;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.Example.Tests;

public sealed class ExampleStandardCrudResourceTests
{
    [Fact]
    public void ExampleModule_RegistersPersistentRecordStandardCrudDescriptor()
    {
        var services = new ServiceCollection();
        new ExampleModule().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IStandardCrudResourceRegistry>();
        var descriptor = registry.FindByResourceKey("example.record");

        Assert.NotNull(descriptor);
        Assert.Equal("Example", descriptor!.ModuleId);
        Assert.Equal("ExampleRecord", descriptor.ModelName);
        Assert.Equal("api/example/records", descriptor.Route);
        Assert.Equal("EID", descriptor.IdField);
        Assert.Equal("ENAME", descriptor.DisplayField);
        Assert.Equal(["EID", "ECODE", "ENAME", "ReviewerUserEID"], descriptor.FieldSet.ListFields);
        Assert.Equal(
            ["EID", "ECODE", "ENAME", "ReviewerUserEID", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"],
            descriptor.FieldSet.DetailFields);
        Assert.Equal(["ECODE", "ENAME", "ReviewerUserEID"], descriptor.FieldSet.CreateFields);
        Assert.Equal(["ENAME", "ReviewerUserEID"], descriptor.FieldSet.UpdateFields);
        Assert.Equal(StandardCrudDeletePolicy.Hard, descriptor.DeletePolicy);
        var displayField = Assert.Single(descriptor.DisplayValueFields);
        Assert.Equal("ReviewerUserEID_DisplayValue", displayField.Field);
        Assert.Equal("ReviewerUserEID", displayField.SourceField);
        Assert.Equal("IdentityAccount", displayField.TargetModelName);
        Assert.Equal("EID", displayField.TargetIdField);
        Assert.Equal("ENAME", displayField.TargetDisplayField);

        Assert.NotNull(descriptor.Authorization);
        Assert.Equal("Example.Record", descriptor.Authorization!.PermissionStem);
        Assert.Equal(ExamplePermissions.RecordView, descriptor.Authorization.GetPermissionCode(StandardCrudOperation.List));
        Assert.Equal(ExamplePermissions.RecordCreate, descriptor.Authorization.GetPermissionCode(StandardCrudOperation.Create));
        Assert.Equal(ExamplePermissions.RecordUpdate, descriptor.Authorization.GetPermissionCode(StandardCrudOperation.Update));
        Assert.Equal(ExamplePermissions.RecordDelete, descriptor.Authorization.GetPermissionCode(StandardCrudOperation.Delete));

        var codeRequirement = Assert.Single(
            descriptor.RequiredFields,
            requirement => string.Equals(requirement.Field, "ECODE", StringComparison.Ordinal));
        Assert.Equal(StandardCrudRequiredFieldScope.CreateOnly, codeRequirement.Scope);

        var nameRequirement = Assert.Single(
            descriptor.RequiredFields,
            requirement => string.Equals(requirement.Field, "ENAME", StringComparison.Ordinal));
        Assert.Equal(StandardCrudRequiredFieldScope.CreateAndUpdate, nameRequirement.Scope);

        var codeUniqueConstraint = Assert.Single(descriptor.UniqueConstraints);
        Assert.Equal("code", codeUniqueConstraint.Key);
        Assert.Equal(["ECODE"], codeUniqueConstraint.Fields);
        Assert.Equal("Example.Record.CodeDuplicated", codeUniqueConstraint.ErrorCode);
        Assert.Equal("example.record.codeDuplicated", codeUniqueConstraint.ReasonKey);
    }

    [Fact]
    public async Task ExampleRecordDisplayValueResolver_ReturnsOnlyRequestedReviewerCompanionAndBatchesDistinctIds()
    {
        var directory = new FakeIdentityAccountDirectory(new Dictionary<string, IdentityAccountSnapshot>(StringComparer.Ordinal)
        {
            ["User_1"] = new()
            {
                AccountEid = "User_1",
                ECODE = "alice",
                ENAME = "Alice",
                Status = IdentityAccountStatus.Enabled,
                Revision = 3
            }
        });
        var resolver = new ExampleRecordDisplayValueResolver(directory);
        var descriptor = ExampleRecordCrudDescriptorFactory.Create();
        var displayField = Assert.Single(descriptor.DisplayValueFields);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
        [
            new Dictionary<string, object?> { ["ReviewerUserEID"] = "User_1" },
            new Dictionary<string, object?> { ["ReviewerUserEID"] = "User_1" },
            new Dictionary<string, object?> { ["ReviewerUserEID"] = "User_missing" },
            new Dictionary<string, object?> { ["ReviewerUserEID"] = null }
        ];

        var resolved = await resolver.ResolveListAsync(descriptor, rows, [displayField], CancellationToken.None);

        Assert.All(resolved, row =>
        {
            var entry = Assert.Single(row);
            Assert.Equal("ReviewerUserEID_DisplayValue", entry.Key);
        });
        Assert.Equal("Alice", resolved[0]["ReviewerUserEID_DisplayValue"]);
        Assert.Equal("Alice", resolved[1]["ReviewerUserEID_DisplayValue"]);
        Assert.Equal("User_missing", resolved[2]["ReviewerUserEID_DisplayValue"]);
        Assert.Null(resolved[3]["ReviewerUserEID_DisplayValue"]);
        Assert.Equal(1, directory.FindManyCallCount);
        Assert.Equal(["User_1", "User_missing"], Assert.Single(directory.RequestedBatches));
    }

    [Fact]
    public async Task ExampleRecordDisplayValueResolver_WhenDirectoryFails_DegradesToNullCompanionAndPreservesRaw()
    {
        var resolver = new ExampleRecordDisplayValueResolver(new ThrowingIdentityAccountDirectory(new InvalidOperationException("directory down")));
        var descriptor = ExampleRecordCrudDescriptorFactory.Create();
        var displayField = Assert.Single(descriptor.DisplayValueFields);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
        [
            new Dictionary<string, object?> { ["ReviewerUserEID"] = "User_1" }
        ];

        var resolved = await resolver.ResolveListAsync(descriptor, rows, [displayField], CancellationToken.None);

        Assert.Null(Assert.Single(resolved)["ReviewerUserEID_DisplayValue"]);
    }

    [Fact]
    public async Task ExampleRecordDisplayValueResolver_WhenCallerCancellationRequested_PropagatesCancellation()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var resolver = new ExampleRecordDisplayValueResolver(
            new ThrowingIdentityAccountDirectory(new OperationCanceledException(cancellationTokenSource.Token)));
        var descriptor = ExampleRecordCrudDescriptorFactory.Create();
        var displayField = Assert.Single(descriptor.DisplayValueFields);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
        [
            new Dictionary<string, object?> { ["ReviewerUserEID"] = "User_1" }
        ];

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            resolver.ResolveListAsync(descriptor, rows, [displayField], cancellationTokenSource.Token));
    }

    [Fact]
    public async Task ExampleRecordDisplayValueResolver_WhenDirectoryThrowsOperationCanceled_AlwaysPropagates()
    {
        var resolver = new ExampleRecordDisplayValueResolver(
            new ThrowingIdentityAccountDirectory(new OperationCanceledException("directory canceled")));
        var descriptor = ExampleRecordCrudDescriptorFactory.Create();
        var displayField = Assert.Single(descriptor.DisplayValueFields);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows =
        [
            new Dictionary<string, object?> { ["ReviewerUserEID"] = "User_1" }
        ];

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            resolver.ResolveListAsync(descriptor, rows, [displayField], CancellationToken.None));
    }

    [Fact]
    public void ExampleRecordCrudDescriptor_UsesReviewerDisplayFieldProjectedFromRealModelMetadata()
    {
        var exampleModels = new ExampleModelMetadataProvider().GetDefinitions();
        var exampleRecord = Assert.Single(exampleModels, model => model.Name == "ExampleRecord");
        var projected = Assert.Single(
            new ModelDisplayValueFieldProjector().Project(exampleRecord, exampleModels),
            field => field.SourceField == "ReviewerUserEID");

        var descriptor = ExampleRecordCrudDescriptorFactory.Create();
        var actual = Assert.Single(descriptor.DisplayValueFields);

        Assert.Equal(projected, actual);
    }

    private sealed class FakeIdentityAccountDirectory : IIdentityAccountDirectory
    {
        private readonly IReadOnlyDictionary<string, IdentityAccountSnapshot> _accounts;

        public FakeIdentityAccountDirectory(IReadOnlyDictionary<string, IdentityAccountSnapshot> accounts)
        {
            _accounts = accounts;
        }

        public int FindManyCallCount { get; private set; }

        public List<IReadOnlyCollection<string>> RequestedBatches { get; } = [];

        public ValueTask<IdentityAccountSnapshot?> FindAsync(string accountEid, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_accounts.TryGetValue(accountEid, out var account) ? account : null);

        public ValueTask<IReadOnlyDictionary<string, IdentityAccountSnapshot>> FindManyAsync(
            IReadOnlyCollection<string> accountEids,
            CancellationToken cancellationToken = default)
        {
            FindManyCallCount++;
            RequestedBatches.Add(accountEids.ToArray());
            return ValueTask.FromResult<IReadOnlyDictionary<string, IdentityAccountSnapshot>>(
                accountEids
                    .Where(_accounts.ContainsKey)
                    .Distinct(StringComparer.Ordinal)
                    .ToDictionary(accountEid => accountEid, accountEid => _accounts[accountEid], StringComparer.Ordinal));
        }

        public ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(
            IdentityAccountSearch search,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingIdentityAccountDirectory(Exception exception) : IIdentityAccountDirectory
    {
        public ValueTask<IdentityAccountSnapshot?> FindAsync(string accountEid, CancellationToken cancellationToken = default) =>
            throw exception;

        public ValueTask<IReadOnlyDictionary<string, IdentityAccountSnapshot>> FindManyAsync(
            IReadOnlyCollection<string> accountEids,
            CancellationToken cancellationToken = default) =>
            throw exception;

        public ValueTask<IReadOnlyList<IdentityAccountSnapshot>> SearchAsync(
            IdentityAccountSearch search,
            CancellationToken cancellationToken = default) =>
            throw exception;
    }
}
