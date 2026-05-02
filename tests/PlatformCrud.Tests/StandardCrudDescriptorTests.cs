using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Runtime;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudDescriptorTests
{
    [Fact]
    public void Validate_WhenDetailFieldsMissId_ThrowsStableMessage()
    {
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
                DetailFields: ["ENAME"],
                CreateFields: ["ENAME"],
                UpdateFields: ["ENAME"]),
            DeletePolicy = StandardCrudDeletePolicy.None
        };

        var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 的 DetailFields 必须包含 IdField。", ex.Message);
    }
}
