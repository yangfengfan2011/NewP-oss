using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Crud.Tests.Support;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudServiceWriteTests
{
    [Fact]
    public async Task CreateAsync_OnlyPersistsCreateFieldsAndReturnsDetail()
    {
        var service = Service();

        var result = await service.CreateAsync(Descriptor(), new Dictionary<string, object?>
        {
            ["PersonNo"] = "P001",
            ["ENAME"] = "张三",
            ["Unexpected"] = "ignored"
        });

        Assert.True(result.Succeeded);
        Assert.False(result.Detail!.Fields.ContainsKey("Unexpected"));
        Assert.Equal("张三", result.Detail.Fields["ENAME"]);
    }

    [Fact]
    public async Task CreateAsync_WhenUniqueConstraintConflicts_ReturnsConflict()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = true }
        ]);
        var service = Service(repository);

        var result = await service.CreateAsync(
            DescriptorWithUniqueConstraint(),
            new Dictionary<string, object?> { ["PersonNo"] = "p001", ["ENAME"] = "李四" });

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("Organization.Person.PersonNoDuplicated", result.Error.Code);
    }

    [Fact]
    public async Task DisableAsync_SetsEnabledFieldFalse()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = true }
        ]);
        var service = Service(repository);

        var result = await service.DisableAsync(Descriptor(), "person-1");

        Assert.True(result.Succeeded);
        Assert.False((bool)result.Detail!.Fields["IsEnabled"]!);
    }

    [Fact]
    public async Task UpdateAsync_WhenUpdatedAtIsWritable_RefreshesMutationTimestamp()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["PersonNo"] = "P001",
                ["ENAME"] = "张三",
                ["IsEnabled"] = true,
                ["UpdatedAt"] = null
            }
        ]);
        var service = Service(repository);

        var result = await service.UpdateAsync(
            Descriptor(includeUpdatedAt: true),
            "person-1",
            new Dictionary<string, object?> { ["ENAME"] = "张三-更新" });

        Assert.True(result.Succeeded);
        Assert.IsType<DateTime>(result.Detail!.Fields["UpdatedAt"]);
    }

    [Fact]
    public async Task DisableAsync_WhenUpdatedAtIsWritable_RefreshesMutationTimestamp()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["PersonNo"] = "P001",
                ["ENAME"] = "张三",
                ["IsEnabled"] = true,
                ["UpdatedAt"] = null
            }
        ]);
        var service = Service(repository);

        var result = await service.DisableAsync(Descriptor(includeUpdatedAt: true), "person-1");

        Assert.True(result.Succeeded);
        Assert.IsType<DateTime>(result.Detail!.Fields["UpdatedAt"]);
    }

    [Fact]
    public async Task UpdateAsync_WhenUniqueFieldNotSubmitted_StillChecksExistingUniqueValue()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = true },
            new Dictionary<string, object?> { ["EID"] = "person-2", ["PersonNo"] = "P002", ["ENAME"] = "李四", ["IsEnabled"] = true }
        ]);
        var service = Service(repository);

        var result = await service.UpdateAsync(
            DescriptorWithUniqueConstraint(),
            "person-1",
            new Dictionary<string, object?> { ["ENAME"] = "张三-更新" });

        Assert.True(result.Succeeded);
        Assert.Equal("P001", result.Detail!.Fields["PersonNo"]);
    }

    [Fact]
    public async Task UpdateAsync_WhenUniqueFieldNotSubmitted_PassesExistingValueToUniqueCheck()
    {
        var repository = new RecordingExistsStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = true }
        ]);
        var service = Service(repository);

        var result = await service.UpdateAsync(
            DescriptorWithUniqueConstraint(),
            "person-1",
            new Dictionary<string, object?> { ["ENAME"] = "张三-更新" });

        Assert.True(result.Succeeded);
        Assert.Equal("person-1", repository.LastExcludedId);
        Assert.NotNull(repository.LastExistsValues);
        Assert.Equal("P001", repository.LastExistsValues!["PersonNo"]);
    }

    [Fact]
    public async Task UpdateAsync_WhenUniqueValueChangesToExistingValue_ReturnsConflict()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = true },
            new Dictionary<string, object?> { ["EID"] = "person-2", ["PersonNo"] = "P002", ["ENAME"] = "李四", ["IsEnabled"] = true }
        ]);
        var service = Service(repository);

        var result = await service.UpdateAsync(
            DescriptorWithUniqueConstraint(includePersonNoInUpdate: true),
            "person-1",
            new Dictionary<string, object?> { ["PersonNo"] = "p002", ["ENAME"] = "张三-更新" });

        Assert.False(result.Succeeded);
        Assert.Equal("Organization.Person.PersonNoDuplicated", result.Error!.Code);
    }

    [Fact]
    public async Task DisableAsync_WhenEnabledFieldIsNotUpdatable_FailsClosed()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = true }
        ]);
        var service = Service(repository);

        var result = await service.DisableAsync(Descriptor(includeEnabledInUpdateFields: false), "person-1");

        Assert.False(result.Succeeded);
        Assert.Equal("StandardCrud.EnabledFieldNotWritable", result.Error!.Code);
    }

    [Fact]
    public async Task DeleteAsync_WhenConditionalWithoutGuard_FailsClosed()
    {
        var service = Service();

        var result = await service.DeleteAsync(Descriptor(StandardCrudDeletePolicy.Conditional), "person-1");

        Assert.False(result.Succeeded);
        Assert.Equal("StandardCrud.DeleteGuardMissing", result.Error!.Code);
    }

    [Fact]
    public async Task DeleteAsync_WhenAuditablePolicyHasNoAuditKernel_FailsClosed()
    {
        var service = Service();

        var result = await service.DeleteAsync(Descriptor(StandardCrudDeletePolicy.Auditable), "person-1");

        Assert.False(result.Succeeded);
        Assert.Equal("StandardCrud.AuditableDeleteNotSupported", result.Error!.Code);
    }

    [Fact]
    public async Task DeleteAsync_WhenGuardBlocks_ReturnsConflict()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三" }
        ]);
        var service = new StandardCrudService(repository, [], [], [], [], [], [new BlockingDeleteGuard()]);

        var result = await service.DeleteAsync(Descriptor(StandardCrudDeletePolicy.Conditional), "person-1");

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("Organization.Person.DeleteBlockedByUserLink", result.Error.Code);
    }

    private static StandardCrudService Service(InMemoryStandardCrudRepository? repository = null) =>
        new(repository ?? new InMemoryStandardCrudRepository([]), [], [], [], [], [], []);

    private static StandardCrudDescriptor Descriptor(
        StandardCrudDeletePolicy policy = StandardCrudDeletePolicy.None,
        bool includeEnabledInUpdateFields = true,
        bool includeUpdatedAt = false) => new()
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        EnabledField = "IsEnabled",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME", "IsEnabled"],
            DetailFields: includeUpdatedAt
                ? ["EID", "PersonNo", "ENAME", "IsEnabled", "UpdatedAt"]
                : ["EID", "PersonNo", "ENAME", "IsEnabled"],
            CreateFields: ["PersonNo", "ENAME"],
            UpdateFields: includeUpdatedAt
                ? ["ENAME", "IsEnabled", "UpdatedAt"]
                : includeEnabledInUpdateFields ? ["ENAME", "IsEnabled"] : ["ENAME"]),
        DeletePolicy = policy
    };

    private static StandardCrudDescriptor DescriptorWithUniqueConstraint(bool includePersonNoInUpdate = false) => new()
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        EnabledField = "IsEnabled",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME", "IsEnabled"],
            DetailFields: ["EID", "PersonNo", "ENAME", "IsEnabled"],
            CreateFields: ["PersonNo", "ENAME", "IsEnabled"],
            UpdateFields: includePersonNoInUpdate ? ["PersonNo", "ENAME", "IsEnabled"] : ["ENAME", "IsEnabled"]),
        UniqueConstraints =
        [
            new StandardCrudUniqueConstraint
            {
                Key = "personNo",
                Fields = ["PersonNo"],
                ErrorCode = "Organization.Person.PersonNoDuplicated",
                Message = "人员工号必须唯一。",
                ReasonKey = "organization.person.personNoDuplicated"
            }
        ]
    };

    private sealed class BlockingDeleteGuard : IStandardCrudDeleteGuard
    {
        public string ResourceKey => "organization.person";

        public Task<StandardCrudError?> CanDeleteAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StandardCrudError?>(new StandardCrudError
            {
                StatusCode = 409,
                Code = "Organization.Person.DeleteBlockedByUserLink",
                Message = "当前人员已绑定账号，不能删除。",
                ReasonKey = "organization.person.deleteBlockedByUserLink"
            });
        }
    }

    private sealed class RecordingExistsStandardCrudRepository : InMemoryStandardCrudRepository
    {
        public RecordingExistsStandardCrudRepository(IEnumerable<Dictionary<string, object?>> rows)
            : base(rows)
        {
        }

        public IReadOnlyDictionary<string, object?>? LastExistsValues { get; private set; }
        public string? LastExcludedId { get; private set; }

        public override Task<bool> ExistsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudUniqueConstraint constraint,
            IReadOnlyDictionary<string, object?> equalityValues,
            string? excludedId = null,
            CancellationToken cancellationToken = default)
        {
            LastExistsValues = new Dictionary<string, object?>(equalityValues, StringComparer.Ordinal);
            LastExcludedId = excludedId;
            return base.ExistsAsync(descriptor, constraint, equalityValues, excludedId, cancellationToken);
        }
    }
}
