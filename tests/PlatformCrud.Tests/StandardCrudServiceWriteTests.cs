using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Crud.Tests.Support;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
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
    public async Task CreateAsync_WhenCreatedAtIsReadable_SetsCreationTimestamp()
    {
        var service = Service();

        var result = await service.CreateAsync(Descriptor(includeCreatedAt: true), new Dictionary<string, object?>
        {
            ["PersonNo"] = "P001",
            ["ENAME"] = "张三"
        });

        Assert.True(result.Succeeded);
        Assert.IsType<DateTime>(result.Detail!.Fields["CreatedAt"]);
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
    public async Task CreateAsync_WhenRequiredFieldIsMissing_ReturnsBadRequestBeforeUniqueCheck()
    {
        var repository = new RecordingExistsStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = true }
        ]);
        var service = Service(repository);

        var result = await service.CreateAsync(
            DescriptorWithRequiredAndUniqueConstraint(),
            new Dictionary<string, object?> { ["ENAME"] = "李四", ["IsEnabled"] = true });

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Equal("Organization.Person.PersonNoRequired", result.Error.Code);
        Assert.Null(repository.LastExistsValues);
    }

    [Fact]
    public async Task CreateAsync_WhenRequiredStringIsBlank_ReturnsBadRequest()
    {
        var service = Service();

        var result = await service.CreateAsync(
            DescriptorWithRequiredFields(),
            new Dictionary<string, object?> { ["PersonNo"] = "P001", ["ENAME"] = "   ", ["IsEnabled"] = true });

        Assert.False(result.Succeeded);
        Assert.Equal("Organization.Person.NameRequired", result.Error!.Code);
    }

    [Fact]
    public async Task CreateAsync_WhenRequiredBooleanIsFalse_Passes()
    {
        var service = Service();

        var result = await service.CreateAsync(
            DescriptorWithRequiredFields(),
            new Dictionary<string, object?> { ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = false });

        Assert.True(result.Succeeded);
        Assert.False((bool)result.Detail!.Fields["IsEnabled"]!);
    }

    [Fact]
    public async Task UpdateAsync_WhenRequiredFieldIsNotSubmitted_DoesNotBlockExistingEmptyValue()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "", ["IsEnabled"] = true }
        ]);
        var service = Service(repository);

        var result = await service.UpdateAsync(
            DescriptorWithRequiredFields(),
            "person-1",
            new Dictionary<string, object?> { ["IsEnabled"] = false });

        Assert.True(result.Succeeded);
        Assert.False((bool)result.Detail!.Fields["IsEnabled"]!);
    }

    [Fact]
    public async Task UpdateAsync_WhenRequiredFieldIsSubmittedAsNull_ReturnsBadRequest()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = true }
        ]);
        var service = Service(repository);

        var result = await service.UpdateAsync(
            DescriptorWithRequiredFields(),
            "person-1",
            new Dictionary<string, object?> { ["ENAME"] = null });

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Equal("Organization.Person.NameRequired", result.Error.Code);
    }

    [Fact]
    public async Task UpdateAsync_WhenDeclaredRevisionIsStale_ReturnsConflictWithoutOverwriting()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "服务器姓名", ["IsEnabled"] = true, ["Revision"] = 4L
            }
        ]);
        var service = Service(repository);

        var result = await service.UpdateAsync(
            Descriptor(concurrencyRevisionField: "Revision"),
            "person-1",
            new Dictionary<string, object?> { ["ENAME"] = "过期草稿", ["Revision"] = 3L });

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("StandardCrud.ConcurrencyConflict", result.Error.Code);
        var current = await repository.GetByIdAsync(Descriptor(concurrencyRevisionField: "Revision"), "person-1");
        Assert.Equal("服务器姓名", current!["ENAME"]);
        Assert.Equal(4L, current["Revision"]);
    }

    [Fact]
    public async Task UpdateAsync_WhenDeclaredRevisionMatches_UpdatesAndReturnsIncrementedRevision()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "服务器姓名", ["IsEnabled"] = true, ["Revision"] = 4L
            }
        ]);
        var service = Service(repository);

        var result = await service.UpdateAsync(
            Descriptor(concurrencyRevisionField: "Revision"),
            "person-1",
            new Dictionary<string, object?> { ["ENAME"] = "新姓名", ["Revision"] = 4m });

        Assert.True(result.Succeeded);
        Assert.Equal("新姓名", result.Detail!.Fields["ENAME"]);
        Assert.Equal(5L, result.Detail.Fields["Revision"]);
    }

    [Fact]
    public async Task UpdateAsync_WhenDeclaredRevisionIsMissing_FailsClosed()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "服务器姓名", ["IsEnabled"] = true, ["Revision"] = 4L
            }
        ]);
        var service = Service(repository);

        var result = await service.UpdateAsync(
            Descriptor(concurrencyRevisionField: "Revision"),
            "person-1",
            new Dictionary<string, object?> { ["ENAME"] = "无版本草稿" });

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("StandardCrud.ConcurrencyRevisionRequired", result.Error.Code);
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
    public async Task DeleteAsync_WhenConditionalExecutorIsMissing_FailsClosedWithoutRepositoryDelete()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三" }
        ]);
        var service = new StandardCrudService(
            repository,
            EmptyQueryExecutor(),
            [],
            [],
            [],
            [],
            [],
            [new AllowingDeleteGuard()]);

        var result = await service.DeleteAsync(Descriptor(StandardCrudDeletePolicy.Conditional), "person-1");

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("StandardCrud.ConditionalDeleteExecutorMissing", result.Error.Code);
        Assert.NotNull(await repository.GetByIdAsync(Descriptor(), "person-1"));
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
    public async Task DeleteAsync_WhenConditionalExecutorBlocks_ReturnsConflict()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三" }
        ]);
        var executor = new BlockingDeleteGuard();
        var service = new StandardCrudService(
            repository,
            EmptyQueryExecutor(),
            [],
            [],
            [],
            [],
            [],
            [executor],
            [executor]);

        var result = await service.DeleteAsync(Descriptor(StandardCrudDeletePolicy.Conditional), "person-1");

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("Organization.Person.DeleteBlockedByUserBinding", result.Error.Code);
    }

    [Fact]
    public async Task DeleteAsync_WhenPolicyIsHard_IgnoresRegisteredConditionalExecutor()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三" }
        ]);
        var executor = new RecordingConditionalDeleteExecutor();
        var service = new StandardCrudService(
            repository,
            EmptyQueryExecutor(),
            [],
            [],
            [],
            [],
            [],
            [],
            [executor]);

        var result = await service.DeleteAsync(Descriptor(StandardCrudDeletePolicy.Hard), "person-1");

        Assert.True(result.Succeeded);
        Assert.Equal(0, executor.CallCount);
        var repeated = await service.DeleteAsync(Descriptor(StandardCrudDeletePolicy.Hard), "person-1");
        Assert.False(repeated.Succeeded);
        Assert.Equal(404, repeated.Error?.StatusCode);
    }

    private static StandardCrudService Service(InMemoryStandardCrudRepository? repository = null) =>
        new(repository ?? new InMemoryStandardCrudRepository([]), EmptyQueryExecutor(), [], [], [], [], [], []);

    private static RecordingPlatformQueryExecutor EmptyQueryExecutor() =>
        new(new PlatformQueryResult([], 0, 1, 20, null, null, [], []));

    private static StandardCrudDescriptor Descriptor(
        StandardCrudDeletePolicy policy = StandardCrudDeletePolicy.None,
        bool includeEnabledInUpdateFields = true,
        bool includeUpdatedAt = false,
        bool includeCreatedAt = false,
        string? concurrencyRevisionField = null) => new()
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
            DetailFields: concurrencyRevisionField is not null
                ? ["EID", "PersonNo", "ENAME", "IsEnabled", concurrencyRevisionField]
                : includeCreatedAt
                ? ["EID", "PersonNo", "ENAME", "IsEnabled", "CreatedAt"]
                : includeUpdatedAt
                ? ["EID", "PersonNo", "ENAME", "IsEnabled", "UpdatedAt"]
                : ["EID", "PersonNo", "ENAME", "IsEnabled"],
            CreateFields: ["PersonNo", "ENAME"],
            UpdateFields: includeUpdatedAt
                ? ["ENAME", "IsEnabled", "UpdatedAt"]
                : includeEnabledInUpdateFields ? ["ENAME", "IsEnabled"] : ["ENAME"]),
        DeletePolicy = policy,
        ConcurrencyRevisionField = concurrencyRevisionField
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

    private static StandardCrudDescriptor DescriptorWithRequiredFields() => new()
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
            UpdateFields: ["ENAME", "IsEnabled"]),
        RequiredFields =
        [
            new StandardCrudRequiredField
            {
                Field = "PersonNo",
                Scope = StandardCrudRequiredFieldScope.CreateOnly,
                ErrorCode = "Organization.Person.PersonNoRequired",
                Message = "人员工号不能为空。",
                ReasonKey = "organization.person.personNoRequired"
            },
            new StandardCrudRequiredField
            {
                Field = "ENAME",
                Scope = StandardCrudRequiredFieldScope.CreateAndUpdate,
                ErrorCode = "Organization.Person.NameRequired",
                Message = "人员姓名不能为空。",
                ReasonKey = "organization.person.nameRequired"
            },
            new StandardCrudRequiredField
            {
                Field = "IsEnabled",
                Scope = StandardCrudRequiredFieldScope.CreateAndUpdate,
                ErrorCode = "Organization.Person.EnabledRequired",
                Message = "人员启用状态不能为空。",
                ReasonKey = "organization.person.enabledRequired"
            }
        ]
    };

    private static StandardCrudDescriptor DescriptorWithRequiredAndUniqueConstraint()
    {
        var descriptor = DescriptorWithRequiredFields();
        return new StandardCrudDescriptor
        {
            ResourceKey = descriptor.ResourceKey,
            ModuleId = descriptor.ModuleId,
            ModelName = descriptor.ModelName,
            Route = descriptor.Route,
            IdField = descriptor.IdField,
            DisplayField = descriptor.DisplayField,
            EnabledField = descriptor.EnabledField,
            FieldSet = descriptor.FieldSet,
            RequiredFields = descriptor.RequiredFields,
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
    }

    private sealed class BlockingDeleteGuard : IStandardCrudDeleteGuard, IStandardCrudConditionalDeleteExecutor
    {
        public string ResourceKey => "organization.person";

        public Task<StandardCrudError?> CanDeleteAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StandardCrudError?>(new StandardCrudError
            {
                StatusCode = 409,
                Code = "Organization.Person.DeleteBlockedByUserBinding",
                Message = "当前人员已绑定账号，不能删除。",
                ReasonKey = "organization.person.deleteBlockedByUserBinding"
            });
        }

        public Task<StandardCrudMutationResult> DeleteAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StandardCrudMutationResult.Failure(new StandardCrudError
            {
                StatusCode = 409,
                Code = "Organization.Person.DeleteBlockedByUserBinding",
                Message = "当前人员已绑定账号，不能删除。",
                ReasonKey = "organization.person.deleteBlockedByUserBinding"
            }));
    }

    private sealed class AllowingDeleteGuard : IStandardCrudDeleteGuard
    {
        public string ResourceKey => "organization.person";

        public Task<StandardCrudError?> CanDeleteAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<StandardCrudError?>(null);
    }

    private sealed class RecordingConditionalDeleteExecutor : IStandardCrudConditionalDeleteExecutor
    {
        public string ResourceKey => "organization.person";

        public int CallCount { get; private set; }

        public Task<StandardCrudMutationResult> DeleteAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(StandardCrudMutationResult.Success());
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

    private sealed class RecordingPlatformQueryExecutor : IPlatformQueryExecutor
    {
        private readonly PlatformQueryResult _result;

        public RecordingPlatformQueryExecutor(PlatformQueryResult result)
        {
            _result = result;
        }

        public Task<PlatformQueryResult> QueryAsync(
            PlatformQueryDescriptor descriptor,
            PlatformQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }
}
