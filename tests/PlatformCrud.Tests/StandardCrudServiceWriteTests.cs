using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Crud.Tests.Support;
using ICP.Platform.Modeling;
using ICP.Platform.Persistence.Auditing;
using ICP.Platform.Presentation;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudServiceWriteTests
{
    [Fact]
    public async Task CreateAsync_WhenObjectBehaviorIsRegistered_UsesOnlyUnifiedOperationEntry()
    {
        var repository = new RecordingMutationStandardCrudRepository([]);
        var operations = new RecordingObjectOperationExecutor();
        var service = Service(repository, objectOperations: operations);

        var result = await service.CreateAsync(Descriptor(), new Dictionary<string, object?>
        {
            ["PersonNo"] = "P001",
            ["ENAME"] = "张三"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(1, operations.Executions);
        Assert.Equal("P001", operations.LastValues!["PersonNo"]);
        Assert.Equal(0, repository.CreateCallCount);
    }

    [Fact]
    public async Task CreateAsync_NormalizesTemporalInputsBeforeRepositoryCreate()
    {
        var repository = new RecordingMutationStandardCrudRepository([]);
        var normalizer = new TemporalInputNormalizer(
            new FixedPresentationContextAccessor(new RequestPresentationContext("zh-CN", "Asia/Taipei")),
            "Asia/Shanghai",
            TimeProvider.System);
        var service = new StandardCrudService(
            repository,
            EmptyQueryExecutor(),
            [],
            [],
            [],
            [],
            [],
            temporalInputNormalizer: normalizer);

        var result = await service.CreateAsync(TemporalDescriptor(), new Dictionary<string, object?>
        {
            ["BirthDate"] = "2000-02-29",
            ["EmploymentStartedAt"] = "2030-01-02T08:04:05"
        });

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2000, 2, 29), repository.LastCreatedValues!["BirthDate"]);
        Assert.Equal(
            new DateTime(2030, 1, 2, 0, 4, 5, DateTimeKind.Utc),
            repository.LastCreatedValues["EmploymentStartedAt"]);
    }

    [Fact]
    public async Task CreateAsync_WhenDatabaseConstraintWinsRace_ReturnsStablePersistenceConflict()
    {
        var service = Service(new RecordingMutationStandardCrudRepository([], failCreateWithKnownConstraintConflict: true));

        var result = await service.CreateAsync(Descriptor(), new Dictionary<string, object?>
        {
            ["PersonNo"] = "P001",
            ["ENAME"] = "张三"
        });

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error?.StatusCode);
        Assert.Equal("StandardCrud.PersistenceConflict", result.Error?.Code);
        Assert.Equal("standardCrud.persistenceConflict", result.Error?.ReasonKey);
        Assert.DoesNotContain("provider-detail", result.Error?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateAsync_WhenForeignKeyConstraintWinsDeleteFirstRace_ReturnsStablePersistenceConflict()
    {
        var repository = new RecordingMutationStandardCrudRepository(
            [new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["PersonNo"] = "P001",
                ["ENAME"] = "张三",
                ["IsEnabled"] = true,
                ["Revision"] = 1L
            }],
            failUpdateWithKnownConstraintConflict: true);
        var service = Service(repository);

        var result = await service.UpdateAsync(
            Descriptor(concurrencyRevisionField: "Revision"),
            "person-1",
            new Dictionary<string, object?> { ["ENAME"] = "李四", ["Revision"] = 1L });

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error?.StatusCode);
        Assert.Equal("StandardCrud.PersistenceConflict", result.Error?.Code);
        Assert.DoesNotContain("provider-detail", result.Error?.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_WhenRequiredRecorderIsAbsent_FailsBeforeBusinessWrite()
    {
        var repository = new RecordingMutationStandardCrudRepository([]);
        var service = Service(repository);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Descriptor() with { RequiresCreationRecord = true },
            new Dictionary<string, object?> { ["PersonNo"] = "P001", ["ENAME"] = "张三" }));
        Assert.Null(repository.LastCreatedValues);
    }

    [Fact]
    public async Task UpdateAsync_WhenRequiredRecorderIsAbsent_FailsBeforeBusinessWrite()
    {
        var repository = new RecordingMutationStandardCrudRepository([]);
        var service = Service(repository);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(Descriptor() with { RequiresUpdateRecord = true },
            "person", new Dictionary<string, object?> { ["ENAME"] = "新姓名" }));
        Assert.Null(repository.LastUpdatedValues);
    }

    [Fact]
    public async Task CreateAsync_WhenPersistenceFailureIsUnknown_PropagatesFailure()
    {
        var service = Service(new RecordingMutationStandardCrudRepository([], failCreate: true));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => service.CreateAsync(
            Descriptor(),
            new Dictionary<string, object?>
            {
                ["PersonNo"] = "P001",
                ["ENAME"] = "张三"
            }));

        Assert.Equal("provider-detail", exception.Message);
    }

    [Fact]
    public async Task UpdateAsync_WhenPersistenceFailureIsUnknown_PropagatesFailure()
    {
        var service = Service(new RecordingMutationStandardCrudRepository(
            [new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["PersonNo"] = "P001",
                ["ENAME"] = "张三",
                ["IsEnabled"] = true
            }],
            failUpdate: true));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => service.UpdateAsync(
            Descriptor(),
            "person-1",
            new Dictionary<string, object?> { ["ENAME"] = "李四" }));

        Assert.Equal("provider-detail", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_IgnoresUnexpectedAndLegacyDisplayFields()
    {
        var repository = new RecordingMutationStandardCrudRepository([]);
        var service = Service(repository, [new FixedPersonDisplayValueResolver()]);

        var result = await service.CreateAsync(DescriptorWithMutationDisplayValueFields(), new Dictionary<string, object?>
        {
            ["PersonNo"] = "P001",
            ["ENAME"] = "张三",
            ["CompanyId"] = "company-1",
            ["DepartmentName"] = "客户端伪造部门",
            ["PositionName"] = "客户端伪造岗位",
            ["Unexpected"] = "ignored"
        });

        Assert.True(result.Succeeded);
        Assert.False(result.Detail!.Fields.ContainsKey("Unexpected"));
        Assert.Equal("张三", result.Detail.Fields["ENAME"]);
        Assert.Equal("company-1", result.Detail.Fields["CompanyId"]);
        Assert.Equal("示例公司", result.Detail.Fields["CompanyId_DisplayValue"]);
        Assert.Equal(1, repository.CreateCallCount);
        Assert.DoesNotContain("Unexpected", repository.LastCreatedValues!.Keys);
        Assert.DoesNotContain("DepartmentName", repository.LastCreatedValues.Keys);
        Assert.DoesNotContain("PositionName", repository.LastCreatedValues.Keys);
    }

    [Fact]
    public async Task CreateAsync_WhenCanonicalDisplayCompanionIsSubmitted_ReturnsBadRequestBeforeRepositoryCreate()
    {
        var repository = new RecordingMutationStandardCrudRepository([]);
        var service = Service(repository, [new FixedPersonDisplayValueResolver()]);

        var result = await service.CreateAsync(
            DescriptorWithMutationDisplayValueFields(),
            new Dictionary<string, object?>
            {
                ["PersonNo"] = "P001",
                ["ENAME"] = "张三",
                ["CompanyId"] = "company-1",
                ["CompanyId_DisplayValue"] = "客户端伪造公司"
            });

        Assert.False(
            result.Succeeded,
            $"canonical display companion 当前被静默忽略并返回成功；仓储创建调用数：{repository.CreateCallCount}。");
        Assert.Equal(0, repository.CreateCallCount);
        AssertDisplayValueFieldReadOnlyError(result);
    }

    [Fact]
    public async Task CreateAsync_WhenServerOwnedAuditFieldIsSubmitted_ReturnsBadRequestBeforeRepositoryCreate()
    {
        var repository = new RecordingMutationStandardCrudRepository([]);
        var service = Service(repository);
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "ENAME"],
                DetailFields: ["EID", "ENAME", "CreatedBy"],
                CreateFields: ["ENAME"],
                UpdateFields: ["ENAME"]),
            FieldPolicies =
            [
                new StandardCrudFieldPolicy
                {
                    Field = "CreatedBy",
                    StorageRequired = true,
                    ValueOwner = StandardCrudFieldValueOwner.Server
                }
            ]
        };

        var result = await service.CreateAsync(
            descriptor,
            new Dictionary<string, object?>
            {
                ["ENAME"] = "张三",
                ["CreatedBy"] = "client-forged"
            });

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Equal("StandardCrud.ServerOwnedFieldReadOnly", result.Error.Code);
        Assert.Equal("standardCrud.serverOwnedFieldReadOnly", result.Error.ReasonKey);
        Assert.Equal(0, repository.CreateCallCount);
        var fieldError = Assert.Single(result.Error.FieldErrors);
        Assert.Equal("CreatedBy", fieldError.Field);
    }

    [Fact]
    public async Task CreateAsync_WhenRequiredAuditOwnerIsUnavailable_ReturnsStablePlatformErrorWithoutRepositoryCreate()
    {
        var repository = new RecordingMutationStandardCrudRepository([]);
        var service = Service(repository);
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "ENAME"],
                DetailFields: ["EID", "ENAME", "CreatedBy"],
                CreateFields: ["ENAME"],
                UpdateFields: ["ENAME"]),
            FieldPolicies =
            [
                new StandardCrudFieldPolicy
                {
                    Field = "CreatedBy",
                    StorageRequired = true,
                    ValueOwner = StandardCrudFieldValueOwner.Server
                }
            ]
        };

        var result = await service.CreateAsync(
            descriptor,
            new Dictionary<string, object?> { ["ENAME"] = "张三" });

        Assert.False(result.Succeeded);
        Assert.Equal(500, result.Error!.StatusCode);
        Assert.Equal("StandardCrud.ServerOwnedValueUnavailable", result.Error.Code);
        Assert.Equal("standardCrud.serverOwnedValueUnavailable", result.Error.ReasonKey);
        Assert.Empty(result.Error.FieldErrors);
        Assert.Equal(0, repository.CreateCallCount);
    }

    [Fact]
    public async Task CreateAsync_WhenRequiredAuditOwnerReturnsNoValue_ReturnsStablePlatformErrorWithoutRepositoryCreate()
    {
        var repository = new RecordingMutationStandardCrudRepository([]);
        var service = Service(repository, auditStampProvider: new NullAuditStampProvider());
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "ENAME"],
                DetailFields: ["EID", "ENAME", "CreatedBy"],
                CreateFields: ["ENAME"],
                UpdateFields: ["ENAME"]),
            FieldPolicies = [ServerOwnedAuditPolicy("CreatedBy")]
        };

        var result = await service.CreateAsync(
            descriptor,
            new Dictionary<string, object?> { ["ENAME"] = "张三" });

        Assert.False(result.Succeeded);
        Assert.Equal("StandardCrud.ServerOwnedValueUnavailable", result.Error!.Code);
        Assert.Equal(0, repository.CreateCallCount);
    }

    [Fact]
    public async Task CreateAsync_WhenRequiredAuditOwnerCannotBuildNonEmptyValue_ReturnsStablePlatformErrorWithoutRepositoryCreate()
    {
        var repository = new RecordingMutationStandardCrudRepository([]);
        var service = Service(repository, auditStampProvider: new MissingActorAuditStampProvider());
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "ENAME"],
                DetailFields: ["EID", "ENAME", "CreatedBy"],
                CreateFields: ["ENAME"],
                UpdateFields: ["ENAME"]),
            FieldPolicies = [ServerOwnedAuditPolicy("CreatedBy")]
        };

        var result = await service.CreateAsync(
            descriptor,
            new Dictionary<string, object?> { ["ENAME"] = "张三" });

        Assert.False(result.Succeeded);
        Assert.Equal("StandardCrud.ServerOwnedValueUnavailable", result.Error!.Code);
        Assert.Equal(0, repository.CreateCallCount);
    }

    [Fact]
    public async Task CreateAsync_WhenRequiredAuditOwnerIsAvailable_AppliesTrustedValuesBeforeRepositoryCreate()
    {
        var now = new DateTime(2026, 8, 28, 10, 30, 0, DateTimeKind.Utc);
        var repository = new RecordingMutationStandardCrudRepository([]);
        var service = Service(
            repository,
            auditStampProvider: new FixedAuditStampProvider("trusted-subject", now));
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "ENAME"],
                DetailFields: ["EID", "ENAME", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"],
                CreateFields: ["ENAME"],
                UpdateFields: ["ENAME"]),
            FieldPolicies =
            [
                ServerOwnedAuditPolicy("CreatedBy"),
                ServerOwnedAuditPolicy("CreatedAt"),
                ServerOwnedAuditPolicy("UpdatedBy"),
                ServerOwnedAuditPolicy("UpdatedAt")
            ]
        };

        var result = await service.CreateAsync(
            descriptor,
            new Dictionary<string, object?> { ["ENAME"] = "张三" });

        Assert.True(result.Succeeded);
        Assert.Equal("trusted-subject", repository.LastCreatedValues!["CreatedBy"]);
        Assert.Equal(now, repository.LastCreatedValues["CreatedAt"]);
        Assert.Equal("trusted-subject", repository.LastCreatedValues["UpdatedBy"]);
        Assert.Equal(now, repository.LastCreatedValues["UpdatedAt"]);
    }

    [Fact]
    public async Task CreateAsync_WhenUndeclaredDisplayLikeFieldIsSubmitted_IgnoresIt()
    {
        var repository = new RecordingMutationStandardCrudRepository([]);
        var service = Service(repository, [new FixedPersonDisplayValueResolver()]);

        var result = await service.CreateAsync(
            DescriptorWithMutationDisplayValueFields(),
            new Dictionary<string, object?>
            {
                ["PersonNo"] = "P001",
                ["ENAME"] = "张三",
                ["CompanyId"] = "company-1",
                ["GhostId_DisplayValue"] = "未声明的伪 companion"
            });

        Assert.True(result.Succeeded);
        Assert.Equal(1, repository.CreateCallCount);
        Assert.DoesNotContain("GhostId_DisplayValue", repository.LastCreatedValues!.Keys);
        Assert.DoesNotContain("GhostId_DisplayValue", result.Detail!.Fields.Keys);
    }

    [Fact]
    public async Task UpdateAsync_WhenCanonicalDisplayCompanionIsSubmitted_ReturnsBadRequestBeforeRepositoryUpdate()
    {
        var descriptor = DescriptorWithMutationDisplayValueFields();
        var repository = new RecordingMutationStandardCrudRepository(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["PersonNo"] = "P001",
                ["ENAME"] = "原姓名",
                ["CompanyId"] = "company-1",
                ["DepartmentId"] = null,
                ["PositionId"] = null,
                ["IsEnabled"] = true
            }
        ]);
        var service = Service(repository, [new FixedPersonDisplayValueResolver()]);

        var result = await service.UpdateAsync(
            descriptor,
            "person-1",
            new Dictionary<string, object?>
            {
                ["ENAME"] = "客户端伪造姓名",
                ["CompanyId_DisplayValue"] = "客户端伪造公司"
            });

        Assert.False(
            result.Succeeded,
            $"canonical display companion 当前被静默忽略并返回成功；仓储更新调用数：{repository.UpdateCallCount}。");
        Assert.Equal(0, repository.UpdateCallCount);
        AssertDisplayValueFieldReadOnlyError(result);
        var current = await repository.GetByIdAsync(descriptor, "person-1");
        Assert.Equal("原姓名", current!["ENAME"]);
        Assert.Equal("company-1", current["CompanyId"]);
    }

    [Fact]
    public async Task UpdateAsync_WhenServerOwnedAuditFieldIsSubmitted_ReturnsBadRequestBeforeRepositoryUpdate()
    {
        var repository = new RecordingMutationStandardCrudRepository(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["ENAME"] = "原姓名",
                ["UpdatedBy"] = "trusted-subject"
            }
        ]);
        var service = Service(repository);
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "ENAME"],
                DetailFields: ["EID", "ENAME", "UpdatedBy"],
                CreateFields: ["ENAME"],
                UpdateFields: ["ENAME"]),
            FieldPolicies =
            [
                new StandardCrudFieldPolicy
                {
                    Field = "UpdatedBy",
                    StorageRequired = true,
                    ValueOwner = StandardCrudFieldValueOwner.Server
                }
            ]
        };

        var result = await service.UpdateAsync(
            descriptor,
            "person-1",
            new Dictionary<string, object?>
            {
                ["ENAME"] = "新姓名",
                ["UpdatedBy"] = "client-forged"
            });

        Assert.False(result.Succeeded);
        Assert.Equal("StandardCrud.ServerOwnedFieldReadOnly", result.Error!.Code);
        Assert.Equal(0, repository.UpdateCallCount);
        var current = await repository.GetByIdAsync(descriptor, "person-1");
        Assert.Equal("原姓名", current!["ENAME"]);
        Assert.Equal("trusted-subject", current["UpdatedBy"]);
    }

    [Fact]
    public async Task UpdateAsync_WhenRequiredAuditOwnerIsAvailable_AppliesTrustedUpdateValuesBeforeRepositoryUpdate()
    {
        var createdAt = new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc);
        var updatedAt = createdAt.AddDays(1);
        var repository = new RecordingMutationStandardCrudRepository(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["ENAME"] = "原姓名",
                ["CreatedBy"] = "creator",
                ["CreatedAt"] = createdAt,
                ["UpdatedBy"] = "creator",
                ["UpdatedAt"] = createdAt
            }
        ]);
        var service = Service(
            repository,
            auditStampProvider: new FixedAuditStampProvider("trusted-editor", updatedAt));
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "ENAME"],
                DetailFields: ["EID", "ENAME", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"],
                CreateFields: ["ENAME"],
                UpdateFields: ["ENAME"]),
            FieldPolicies =
            [
                ServerOwnedAuditPolicy("CreatedBy"),
                ServerOwnedAuditPolicy("CreatedAt"),
                ServerOwnedAuditPolicy("UpdatedBy"),
                ServerOwnedAuditPolicy("UpdatedAt")
            ]
        };

        var result = await service.UpdateAsync(
            descriptor,
            "person-1",
            new Dictionary<string, object?> { ["ENAME"] = "新姓名" });

        Assert.True(result.Succeeded);
        Assert.Equal("trusted-editor", repository.LastUpdatedValues!["UpdatedBy"]);
        Assert.Equal(updatedAt, repository.LastUpdatedValues["UpdatedAt"]);
        Assert.DoesNotContain("CreatedBy", repository.LastUpdatedValues.Keys);
        Assert.DoesNotContain("CreatedAt", repository.LastUpdatedValues.Keys);
    }

    [Fact]
    public async Task UpdateAsync_WhenRequiredAuditOwnerIsUnavailable_ReturnsStablePlatformErrorWithoutRepositoryUpdate()
    {
        var repository = new RecordingMutationStandardCrudRepository(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["ENAME"] = "原姓名",
                ["UpdatedBy"] = "creator",
                ["UpdatedAt"] = new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc)
            }
        ]);
        var service = Service(repository);
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "ENAME"],
                DetailFields: ["EID", "ENAME", "UpdatedBy", "UpdatedAt"],
                CreateFields: ["ENAME"],
                UpdateFields: ["ENAME"]),
            FieldPolicies =
            [
                ServerOwnedAuditPolicy("UpdatedBy"),
                ServerOwnedAuditPolicy("UpdatedAt")
            ]
        };

        var result = await service.UpdateAsync(
            descriptor,
            "person-1",
            new Dictionary<string, object?> { ["ENAME"] = "新姓名" });

        Assert.False(result.Succeeded);
        Assert.Equal("StandardCrud.ServerOwnedValueUnavailable", result.Error!.Code);
        Assert.Equal(0, repository.UpdateCallCount);
        var current = await repository.GetByIdAsync(descriptor, "person-1");
        Assert.Equal("原姓名", current!["ENAME"]);
    }

    [Fact]
    public async Task UpdateAsync_IgnoresUnexpectedAndLegacyDisplayFields()
    {
        var descriptor = DescriptorWithMutationDisplayValueFields();
        var repository = new RecordingMutationStandardCrudRepository(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["PersonNo"] = "P001",
                ["ENAME"] = "原姓名",
                ["CompanyId"] = "company-1",
                ["DepartmentId"] = null,
                ["PositionId"] = null,
                ["IsEnabled"] = true
            }
        ]);
        var service = Service(repository, [new FixedPersonDisplayValueResolver()]);

        var result = await service.UpdateAsync(
            descriptor,
            "person-1",
            new Dictionary<string, object?>
            {
                ["ENAME"] = "新姓名",
                ["CompanyId"] = "company-2",
                ["DepartmentName"] = "客户端伪造部门",
                ["PositionName"] = "客户端伪造岗位",
                ["Unexpected"] = "ignored"
            });

        Assert.True(result.Succeeded);
        Assert.Equal("company-2", result.Detail!.Fields["CompanyId"]);
        Assert.Equal("示例公司", result.Detail.Fields["CompanyId_DisplayValue"]);
        Assert.Equal(1, repository.UpdateCallCount);
        Assert.DoesNotContain("Unexpected", repository.LastUpdatedValues!.Keys);
        Assert.DoesNotContain("DepartmentName", repository.LastUpdatedValues.Keys);
        Assert.DoesNotContain("PositionName", repository.LastUpdatedValues.Keys);
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
    public async Task CreateAsync_WhenServerFieldIsOmitted_AppliesServerDefault()
    {
        var repository = new RecordingMutationStandardCrudRepository([]);
        var service = Service(repository);

        var result = await service.CreateAsync(
            ShortTextDescriptor(),
            new Dictionary<string, object?> { ["ENAME"] = "平台主管" });

        Assert.True(result.Succeeded);
        Assert.Equal(true, repository.LastCreatedValues!["IsEnabled"]);
    }

    [Fact]
    public async Task CreateAsync_WhenServerOnCreateFieldIsSubmitted_ReturnsStableFieldError()
    {
        var service = Service();

        var result = await service.CreateAsync(
            ShortTextDescriptor(),
            new Dictionary<string, object?> { ["ENAME"] = "平台主管", ["IsEnabled"] = false });

        Assert.False(result.Succeeded);
        Assert.Equal("StandardCrud.ServerOwnedFieldReadOnly", result.Error!.Code);
        Assert.Equal("IsEnabled", Assert.Single(result.Error.FieldErrors).Field);
    }

    [Fact]
    public async Task CallerOnCreateField_IsAcceptedOnCreateAndRejectedOnUpdateBeforeFiltering()
    {
        var descriptor = CallerOnCreateDescriptor();
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "item-1", ["ECODE"] = "C001", ["ENAME"] = "初始名称" }
        ]);
        var service = Service(repository);

        var created = await service.CreateAsync(
            descriptor,
            new Dictionary<string, object?> { ["ECODE"] = "C002", ["ENAME"] = "新建名称" });
        var updated = await service.UpdateAsync(
            descriptor,
            "item-1",
            new Dictionary<string, object?> { ["ECODE"] = "C999", ["ENAME"] = "更新名称" });

        Assert.True(created.Succeeded);
        Assert.Equal("C002", created.Detail!.Fields["ECODE"]);
        Assert.False(updated.Succeeded);
        Assert.Equal(400, updated.Error!.StatusCode);
        Assert.Equal("StandardCrud.ImmutableFieldReadOnly", updated.Error.Code);
        Assert.Equal("ECODE", Assert.Single(updated.Error.FieldErrors).Field);
        Assert.Equal("C001", (await repository.GetByIdAsync(descriptor, "item-1"))!["ECODE"]);
    }

    [Fact]
    public async Task UpdateAsync_WhenFieldIsServerOwnedOnlyOnCreate_UsesLastWriteWinsWithoutRevision()
    {
        var descriptor = ShortTextDescriptor();
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "position-1", ["ENAME"] = "初始岗位", ["IsEnabled"] = true }
        ]);
        var service = Service(repository);

        var first = await service.UpdateAsync(
            descriptor,
            "position-1",
            new Dictionary<string, object?> { ["ENAME"] = "第一次更新", ["IsEnabled"] = false });
        var second = await service.UpdateAsync(
            descriptor,
            "position-1",
            new Dictionary<string, object?> { ["ENAME"] = "第二次更新", ["IsEnabled"] = true });

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Null(descriptor.ConcurrencyRevisionField);
        Assert.Equal("第二次更新", second.Detail!.Fields["ENAME"]);
        Assert.Equal(true, second.Detail.Fields["IsEnabled"]);
    }

    [Theory]
    [InlineData("ENAME", " 带空格", "StandardCrud.ShortTextNotNormalized")]
    [InlineData("Description", "", "StandardCrud.ShortTextNotNormalized")]
    [InlineData("Description", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", "StandardCrud.ShortTextTooLong")]
    public async Task UpdateAsync_WhenPositionShortTextIsNotCanonical_ReturnsStableFieldError(
        string field,
        string value,
        string expectedCode)
    {
        var descriptor = ShortTextDescriptor();
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "position-1", ["ENAME"] = "初始岗位", ["Description"] = null, ["IsEnabled"] = true }
        ]);
        var service = Service(repository);

        var result = await service.UpdateAsync(
            descriptor,
            "position-1",
            new Dictionary<string, object?> { [field] = value });

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.Error!.Code);
        var fieldError = Assert.Single(result.Error.FieldErrors);
        Assert.Equal(field, fieldError.Field);
        Assert.Equal(expectedCode, fieldError.Code);
    }

    [Fact]
    public async Task UpdateAsync_WhenPositionNameIsNull_ReturnsStableRequiredFieldError()
    {
        var descriptor = ShortTextDescriptor();
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "position-1", ["ENAME"] = "初始岗位", ["Description"] = null, ["IsEnabled"] = true }
        ]);
        var service = Service(repository);

        var result = await service.UpdateAsync(
            descriptor,
            "position-1",
            new Dictionary<string, object?> { ["ENAME"] = null });

        Assert.False(result.Succeeded);
        Assert.Equal("Organization.Position.NameRequired", result.Error!.Code);
        Assert.Equal("ENAME", Assert.Single(result.Error.FieldErrors).Field);
    }

    [Theory]
    [InlineData(" 带空格", "StandardCrud.ShortTextNotNormalized")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", "StandardCrud.ShortTextTooLong")]
    public async Task CreateAsync_WhenShortTextViolatesSharedPolicy_ReturnsStableFieldError(
        string name,
        string expectedCode)
    {
        var repository = new RecordingMutationStandardCrudRepository([]);
        var service = Service(repository);

        var result = await service.CreateAsync(
            ShortTextDescriptor(),
            new Dictionary<string, object?> { ["ENAME"] = name });

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.Error!.Code);
        var fieldError = Assert.Single(result.Error.FieldErrors);
        Assert.Equal("ENAME", fieldError.Field);
        Assert.Equal(expectedCode, fieldError.Code);
        Assert.Equal(0, repository.CreateCallCount);
    }

    [Fact]
    public async Task CreateAsync_WhenFinalCandidateMissesCallerOwnedStorageRequiredValue_ReturnsConflictWithoutWriting()
    {
        var repository = new RecordingMutationStandardCrudRepository([]);
        var service = Service(repository);
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "ENAME"],
                DetailFields: ["EID", "ENAME"],
                CreateFields: ["ENAME"],
                UpdateFields: ["ENAME"]),
            FieldPolicies =
            [
                new StandardCrudFieldPolicy
                {
                    Field = "ENAME",
                    StorageRequired = true,
                    ValueOwner = StandardCrudFieldValueOwner.Caller
                }
            ]
        };

        var result = await service.CreateAsync(descriptor, new Dictionary<string, object?>());

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("StandardCrud.StorageInvariantViolation", result.Error.Code);
        Assert.Null(repository.LastCreatedValues);
    }

    [Fact]
    public async Task UpdateAsync_WhenMergedCandidateKeepsEmptyStorageRequiredValue_ReturnsConflictWithoutWriting()
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

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("StandardCrud.StorageInvariantViolation", result.Error.Code);
        Assert.Equal("standardCrud.storageInvariantViolation", result.Error.ReasonKey);
        var current = await repository.GetByIdAsync(DescriptorWithRequiredFields(), "person-1");
        Assert.True((bool)current!["IsEnabled"]!);
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
    public async Task RevisionOwnedByServerOnCreate_DefaultsRejectsCreateOverrideAndSupportsConcurrencyUpdate()
    {
        var descriptor = Descriptor(concurrencyRevisionField: "Revision");
        var repository = new RecordingMutationStandardCrudRepository([]);
        var service = Service(repository);

        var created = await service.CreateAsync(
            descriptor,
            new Dictionary<string, object?> { ["PersonNo"] = "P001", ["ENAME"] = "初始姓名" });
        var rejected = await service.CreateAsync(
            descriptor,
            new Dictionary<string, object?> { ["PersonNo"] = "P002", ["ENAME"] = "伪造版本", ["Revision"] = 9L });
        var updated = await service.UpdateAsync(
            descriptor,
            created.Detail!.Fields["EID"]!.ToString()!,
            new Dictionary<string, object?> { ["ENAME"] = "更新姓名", ["Revision"] = 0L });

        Assert.True(created.Succeeded);
        Assert.Equal(0L, created.Detail.Fields["Revision"]);
        Assert.Equal(0L, repository.LastCreatedValues!["Revision"]);
        Assert.False(rejected.Succeeded);
        Assert.Equal("StandardCrud.ServerOwnedFieldReadOnly", rejected.Error!.Code);
        Assert.Equal("Revision", Assert.Single(rejected.Error.FieldErrors).Field);
        Assert.Equal(1, repository.CreateCallCount);
        Assert.True(updated.Succeeded);
        Assert.Equal(1L, updated.Detail!.Fields["Revision"]);
        Assert.Equal(1, repository.UpdateCallCount);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnableAsync_ChangesOnlyDeclaredStateField(bool initial)
    {
        var repository = new InMemoryStandardCrudRepository([
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "原名称", ["IsEnabled"] = initial }
        ]);
        var result = await Service(repository).EnableAsync(Descriptor(), "person-1");
        Assert.True(result.Succeeded);
        Assert.True((bool)result.Detail!.Fields["IsEnabled"]!);
        Assert.Equal("原名称", result.Detail.Fields["ENAME"]);
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
    public async Task DisableAsync_WithCanonicalDisplayValueFields_StillUpdatesEnabledField()
    {
        var descriptor = DescriptorWithMutationDisplayValueFields();
        var repository = new RecordingMutationStandardCrudRepository(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["PersonNo"] = "P001",
                ["ENAME"] = "张三",
                ["CompanyId"] = "company-1",
                ["DepartmentId"] = null,
                ["PositionId"] = null,
                ["IsEnabled"] = true
            }
        ]);
        var service = Service(repository, [new FixedPersonDisplayValueResolver()]);

        var result = await service.DisableAsync(descriptor, "person-1");

        Assert.True(result.Succeeded);
        Assert.False((bool)result.Detail!.Fields["IsEnabled"]!);
        Assert.Equal(1, repository.UpdateCallCount);
        var update = Assert.Single(repository.LastUpdatedValues!);
        Assert.Equal("IsEnabled", update.Key);
        Assert.Equal(false, update.Value);
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
            [executor]);

        var result = await service.DeleteAsync(Descriptor(StandardCrudDeletePolicy.Hard), "person-1");

        Assert.True(result.Succeeded);
        Assert.Equal(0, executor.CallCount);
        var repeated = await service.DeleteAsync(Descriptor(StandardCrudDeletePolicy.Hard), "person-1");
        Assert.False(repeated.Succeeded);
        Assert.Equal(404, repeated.Error?.StatusCode);
    }

    private static StandardCrudService Service(
        IStandardCrudRepository? repository = null,
        IEnumerable<IStandardCrudDisplayValueResolver>? displayValueResolvers = null,
        IEntityAuditStampProvider? auditStampProvider = null,
        IObjectOperationExecutor? objectOperations = null) =>
        new(
            repository ?? new InMemoryStandardCrudRepository([]),
            EmptyQueryExecutor(),
            [],
            displayValueResolvers ?? [],
            [],
            [],
            [],
            [],
            auditStampProvider: auditStampProvider,
            objectOperations: objectOperations);

    private static StandardCrudFieldPolicy ServerOwnedAuditPolicy(string field) => new()
    {
        Field = field,
        StorageRequired = true,
        ValueOwner = StandardCrudFieldValueOwner.Server
    };

    private static RecordingPlatformQueryExecutor EmptyQueryExecutor() =>
        new(new PlatformQueryResult([], 0, 1, 20, null, null, [], []));

    private static StandardCrudDescriptor TemporalDescriptor() => new()
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "BirthDate", "EmploymentStartedAt"],
            DetailFields: ["EID", "BirthDate", "EmploymentStartedAt"],
            CreateFields: ["BirthDate", "EmploymentStartedAt"],
            UpdateFields: ["BirthDate", "EmploymentStartedAt"]),
        FieldPolicies =
        [
            new StandardCrudFieldPolicy { Field = "BirthDate", ValueType = FieldValueType.CalendarDate },
            new StandardCrudFieldPolicy { Field = "EmploymentStartedAt", ValueType = FieldValueType.Instant }
        ]
    };

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
        ConcurrencyRevisionField = concurrencyRevisionField,
        FieldPolicies = concurrencyRevisionField is null
            ? []
            : [
                new StandardCrudFieldPolicy
                {
                    Field = concurrencyRevisionField,
                    ValueType = FieldValueType.Integer,
                    StorageRequired = true,
                    ValueOwner = StandardCrudFieldValueOwner.ServerOnCreate,
                    CreateDefaultValue = 0L
                }
            ]
    };

    private static StandardCrudDescriptor ShortTextDescriptor() => new()
    {
        ResourceKey = "organization.position",
        ModuleId = "Organization",
        ModelName = "Position",
        Route = "api/organization/positions",
        IdField = "EID",
        DisplayField = "ENAME",
        EnabledField = "IsEnabled",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "ENAME", "Description", "IsEnabled"],
            DetailFields: ["EID", "ENAME", "Description", "IsEnabled"],
            CreateFields: ["ENAME", "Description"],
            UpdateFields: ["ENAME", "Description", "IsEnabled"]),
        RequiredFields =
        [
            new StandardCrudRequiredField
            {
                Field = "ENAME",
                Scope = StandardCrudRequiredFieldScope.CreateAndUpdate,
                ErrorCode = "Organization.Position.NameRequired",
                Message = "岗位名称不能为空。",
                ReasonKey = "organization.position.nameRequired"
            }
        ],
        FieldPolicies =
        [
            new StandardCrudFieldPolicy
            {
                Field = "ENAME",
                StorageRequired = true,
                CreateInputRequired = true,
                UpdateNonEmpty = true,
                ValueOwner = StandardCrudFieldValueOwner.Caller,
                Trim = true,
                MaxLength = 256
            },
            new StandardCrudFieldPolicy
            {
                Field = "Description",
                ValueOwner = StandardCrudFieldValueOwner.Caller,
                Trim = true,
                EmptyAsNull = true,
                MaxLength = 256
            },
            new StandardCrudFieldPolicy
            {
                Field = "IsEnabled",
                StorageRequired = true,
                ValueOwner = StandardCrudFieldValueOwner.ServerOnCreate,
                CreateDefaultValue = true
            }
        ]
    };

    private static StandardCrudDescriptor CallerOnCreateDescriptor() => new()
    {
        ResourceKey = "example.immutable",
        ModuleId = "Example",
        ModelName = "ImmutableItem",
        Route = "api/example/immutable-items",
        IdField = "EID",
        DisplayField = "ENAME",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "ECODE", "ENAME"],
            DetailFields: ["EID", "ECODE", "ENAME"],
            CreateFields: ["ECODE", "ENAME"],
            UpdateFields: ["ENAME"]),
        RequiredFields =
        [
            new StandardCrudRequiredField
            {
                Field = "ECODE",
                Scope = StandardCrudRequiredFieldScope.CreateOnly,
                ErrorCode = "Example.Immutable.CodeRequired",
                Message = "编码不能为空。",
                ReasonKey = "example.immutable.codeRequired"
            }
        ],
        FieldPolicies =
        [
            new StandardCrudFieldPolicy
            {
                Field = "ECODE",
                StorageRequired = true,
                CreateInputRequired = true,
                ValueOwner = StandardCrudFieldValueOwner.CallerOnCreate
            },
            new StandardCrudFieldPolicy
            {
                Field = "ENAME",
                StorageRequired = true,
                ValueOwner = StandardCrudFieldValueOwner.Caller
            }
        ]
    };

    private static StandardCrudDescriptor DescriptorWithMutationDisplayValueFields() => new()
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        EnabledField = "IsEnabled",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId", "IsEnabled"],
            DetailFields: ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId", "IsEnabled"],
            CreateFields: ["PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId"],
            UpdateFields: ["ENAME", "CompanyId", "DepartmentId", "PositionId", "IsEnabled"]),
        DisplayValueFields =
        [
            new PlatformDisplayValueField("CompanyId_DisplayValue", "CompanyId", "Company", "EID", "ENAME"),
            new PlatformDisplayValueField("DepartmentName", "DepartmentId", "Department", "EID", "ENAME"),
            new PlatformDisplayValueField("PositionName", "PositionId", "Position", "EID", "ENAME")
        ]
    };

    private static void AssertDisplayValueFieldReadOnlyError(StandardCrudMutationResult result)
    {
        var error = Assert.IsType<StandardCrudError>(result.Error);
        Assert.Equal(400, error.StatusCode);
        Assert.Equal("StandardCrud.DisplayValueFieldReadOnly", error.Code);
        Assert.Equal("standardCrud.displayValueFieldReadOnly", error.ReasonKey);
        Assert.Equal(
            "标准 CRUD 显示字段 CompanyId_DisplayValue 只允许输出，不能提交写入。",
            error.Message);
        var fieldError = Assert.Single(error.FieldErrors);
        Assert.Equal("CompanyId_DisplayValue", fieldError.Field);
        Assert.Equal("StandardCrud.DisplayValueFieldReadOnly", fieldError.Code);
        Assert.Equal(
            "标准 CRUD 显示字段 CompanyId_DisplayValue 只允许输出，不能提交写入。",
            fieldError.Message);
    }

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
        ],
        FieldPolicies =
        [
            new StandardCrudFieldPolicy
            {
                Field = "PersonNo",
                StorageRequired = true,
                CreateInputRequired = true,
                ValueOwner = StandardCrudFieldValueOwner.Caller
            },
            new StandardCrudFieldPolicy
            {
                Field = "ENAME",
                StorageRequired = true,
                CreateInputRequired = true,
                UpdateNonEmpty = true,
                ValueOwner = StandardCrudFieldValueOwner.Caller
            },
            new StandardCrudFieldPolicy
            {
                Field = "IsEnabled",
                StorageRequired = true,
                CreateInputRequired = true,
                UpdateNonEmpty = true,
                ValueOwner = StandardCrudFieldValueOwner.Caller
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

    private sealed class RecordingMutationStandardCrudRepository : IStandardCrudRepository
    {
        private readonly InMemoryStandardCrudRepository _inner;
        private readonly bool _failCreate;
        private readonly bool _failUpdate;
        private readonly bool _failCreateWithKnownConstraintConflict;
        private readonly bool _failUpdateWithKnownConstraintConflict;

        public RecordingMutationStandardCrudRepository(
            IEnumerable<Dictionary<string, object?>> rows,
            bool failCreate = false,
            bool failUpdate = false,
            bool failCreateWithKnownConstraintConflict = false,
            bool failUpdateWithKnownConstraintConflict = false)
        {
            _inner = new InMemoryStandardCrudRepository(rows);
            _failCreate = failCreate;
            _failUpdate = failUpdate;
            _failCreateWithKnownConstraintConflict = failCreateWithKnownConstraintConflict;
            _failUpdateWithKnownConstraintConflict = failUpdateWithKnownConstraintConflict;
        }

        public int CreateCallCount { get; private set; }
        public int UpdateCallCount { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastCreatedValues { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastUpdatedValues { get; private set; }

        public Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            _inner.GetByIdAsync(descriptor, id, cancellationToken);

        public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudListQuery query,
            CancellationToken cancellationToken = default) =>
            _inner.OptionsAsync(descriptor, query, cancellationToken);

        public Task<bool> ExistsAsync(
            StandardCrudDescriptor descriptor,
            StandardCrudUniqueConstraint constraint,
            IReadOnlyDictionary<string, object?> equalityValues,
            string? excludedId = null,
            CancellationToken cancellationToken = default) =>
            _inner.ExistsAsync(descriptor, constraint, equalityValues, excludedId, cancellationToken);

        public Task<IReadOnlyDictionary<string, object?>> CreateAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            CreateCallCount++;
            LastCreatedValues = new Dictionary<string, object?>(values, StringComparer.Ordinal);
            if (_failCreate)
            {
                throw new DbUpdateException("provider-detail");
            }
            if (_failCreateWithKnownConstraintConflict)
            {
                throw new StandardCrudPersistenceConflictException("provider-detail");
            }
            return _inner.CreateAsync(descriptor, values, cancellationToken);
        }

        public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
            StandardCrudDescriptor descriptor,
            string id,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            UpdateCallCount++;
            LastUpdatedValues = new Dictionary<string, object?>(values, StringComparer.Ordinal);
            if (_failUpdate)
            {
                throw new DbUpdateException("provider-detail");
            }
            if (_failUpdateWithKnownConstraintConflict)
            {
                throw new StandardCrudPersistenceConflictException("provider-detail");
            }
            return _inner.UpdateAsync(descriptor, id, values, cancellationToken);
        }

        public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
            StandardCrudDescriptor descriptor,
            string id,
            IReadOnlyDictionary<string, object?> values,
            long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            UpdateCallCount++;
            LastUpdatedValues = new Dictionary<string, object?>(values, StringComparer.Ordinal);
            if (_failUpdate)
            {
                throw new DbUpdateException("provider-detail");
            }
            if (_failUpdateWithKnownConstraintConflict)
            {
                throw new StandardCrudPersistenceConflictException("provider-detail");
            }
            return _inner.UpdateAsync(descriptor, id, values, expectedRevision, cancellationToken);
        }

        public Task<bool> DeleteAsync(
            StandardCrudDescriptor descriptor,
            string id,
            CancellationToken cancellationToken = default) =>
            _inner.DeleteAsync(descriptor, id, cancellationToken);
    }

    private sealed class FixedPersonDisplayValueResolver : IStandardCrudDisplayValueResolver
    {
        public string ResourceKey => "organization.person";

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
            StandardCrudDescriptor descriptor,
            IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
            IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(
                rows.Select(_ => (IReadOnlyDictionary<string, object?>)requestedDisplayFields.ToDictionary(
                    field => field.Field,
                    field => (object?)(field.Field switch
                        {
                            "CompanyId_DisplayValue" => "示例公司",
                            "DepartmentName" => "示例部门",
                            "PositionName" => "示例岗位",
                            _ => string.Empty
                        }),
                    StringComparer.Ordinal)).ToList());
    }

    private sealed class FixedAuditStampProvider(string actorId, DateTime occurredAtUtc) : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent() => new(actorId, occurredAtUtc);
    }

    private sealed class RecordingObjectOperationExecutor : IObjectOperationExecutor
    {
        public int Executions { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastValues { get; private set; }

        public bool CanExecute(string objectKey, ObjectOperationAction action) =>
            objectKey == "organization.person" && action == ObjectOperationAction.Create;

        public Task<StandardCrudMutationResult> ExecuteAsync(
            string objectKey,
            ObjectOperationAction action,
            IReadOnlyDictionary<string, object?> values,
            CancellationToken cancellationToken = default)
        {
            Executions++;
            LastValues = values;
            return Task.FromResult(StandardCrudMutationResult.Success(new StandardCrudDetail
            {
                Fields = new Dictionary<string, object?>
                {
                    ["EID"] = "Person_1",
                    ["PersonNo"] = values["PersonNo"],
                    ["ENAME"] = values["ENAME"]
                }
            }));
        }
    }

    private sealed class FixedPresentationContextAccessor(RequestPresentationContext? context)
        : IRequestPresentationContextAccessor
    {
        public RequestPresentationContext? Current { get; } = context;
    }

    private sealed class NullAuditStampProvider : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent() => null!;
    }

    private sealed class MissingActorAuditStampProvider : IEntityAuditStampProvider
    {
        public EntityAuditStamp GetCurrent() => new(" ", DateTime.UtcNow);
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
