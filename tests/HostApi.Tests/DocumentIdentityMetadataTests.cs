using ICP.Platform.Modules.Document;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DocumentIdentityMetadataTests
{
    [Fact]
    public void Authoring_LoadsOwnedInitialIdentityAndKeepsBusinessCrudOnMaster()
    {
        var definitions = new DocumentModelMetadataProvider().GetDefinitions();
        Assert.Equal(new[] { "Document", "DocumentIteration", "DocumentVersion" }, definitions.Select(model => model.LogicalKey).Order());
        Assert.All(definitions, model => Assert.Equal("Document", model.OwnerModuleId));
        var descriptor = DocumentCrudDescriptor.Create();
        Assert.Equal("Document", descriptor.ModelName);
        Assert.True(descriptor.RequiresCreationRecord);
        Assert.True(descriptor.RequiresUpdateRecord);
        Assert.DoesNotContain("LatestVersionId", descriptor.FieldSet.CreateFields);
        Assert.DoesNotContain("LatestVersionId", descriptor.FieldSet.UpdateFields);
    }
}
