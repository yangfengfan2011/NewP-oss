using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
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
        Assert.Equal(["EID", "ECODE", "ENAME"], descriptor.FieldSet.ListFields);
        Assert.Equal(
            ["EID", "ECODE", "ENAME", "CreatedBy", "CreatedAt", "UpdatedBy", "UpdatedAt"],
            descriptor.FieldSet.DetailFields);
        Assert.Equal(["ECODE", "ENAME"], descriptor.FieldSet.CreateFields);
        Assert.Equal(["ENAME"], descriptor.FieldSet.UpdateFields);
        Assert.Equal(StandardCrudDeletePolicy.Hard, descriptor.DeletePolicy);

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
}
