using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Document;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class DocumentObjectCopyExecutionContractTests
{
    [Fact]
    public void FormalDocumentCopySharesTheActualButtonButKeepsEveryOriginalReadAndCopyPoint()
    {
        var compiled = new CanonicalModulePlanCompiler().Compile("document-object-copy",
            new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")));
        Assert.True(compiled.IsSuccess, string.Join(";", compiled.Diagnostics.Select(item => item.Message)));
        var plan = compiled.Plan!;
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "workspace.objectCopy");
        Assert.Equal(("workspace.folder.list", "pageActions", "pasteObjectCopy"), (declaration.ViewKey, declaration.Surface, declaration.ActionKey));
        Assert.Equal(new[] { "part.part", "document.document", "workspace.folder" }, declaration.Resources);
        var workspace = Assert.Single(plan.Modules, module => module.ModuleId == "Workspace");
        Assert.Equal(new[] { "part", "document", "documentBatch" }, Assert.Single(workspace.DataExecutions, item => item.Key == declaration.Execution).Branches);
        foreach (var stage in new[] { "prepare", "submit" })
        {
            foreach (var branch in new[] { "document", "documentBatch" })
            {
                var requirements = declaration.Requirements!.Where(item => item.Stage == stage && (item.Branch is null || item.Branch == branch)).ToArray();
                var visibility = Assert.Single(requirements, item => item.Role == "visibility");
                Assert.Equal(new[] { "read" }, Assert.Single(visibility.AnyOf));
                Assert.Equal(branch == "documentBatch" ? "workspace.clipboard.endpoint" : null, visibility.ReadResolverKey);
                var source = Assert.Single(requirements, item => item.Role == "source");
                Assert.Equal(new[] { "document.document" }, source.Resources);
                Assert.Equal(new[] { "documentObjectCopy" }, Assert.Single(source.AnyOf));
                Assert.Null(source.ReadResolverKey);
                Assert.Equal(new[] { "read", "edit" }, Assert.Single(Assert.Single(requirements, item => item.Role == "parent").AnyOf));
            }
        }
        var owner = Assert.Single(plan.Modules, module => module.ModuleId == "Document");
        var copy = Assert.Single(DataExecutionProviders.Resolve(owner.DataExecutions.Where(item => item.Key == "document.files.copySource").ToArray(), owner.DataExecutionProviders));
        Assert.Equal(new[] { "read", "documentObjectCopy" }, Assert.Single(Assert.Single(copy.Requirements).AnyOf));
        Assert.Equal("document.files.currentRead", Assert.Single(copy.Requirements).ReadResolverKey);
        var metadata = Assert.Single(owner.DataExecutions, item => item.Key == "document.files.copyMetadata");
        Assert.Equal(new[] { "read" }, Assert.Single(Assert.Single(metadata.Requirements).AnyOf));
        Assert.Equal("ICP.Platform.Modules.Document.DocumentFiles.ReadCopySourceAsync", Assert.Single(metadata.FixedBindings).Caller);
        Assert.Equal(new[] { "ICP.Platform.Modules.Document.DocumentFiles.PrepareCopyFilesAsync", "ICP.Platform.Modules.Document.DocumentFiles.CopyAsync" }, copy.FixedBindings.Select(item => item.Caller));
        Assert.DoesNotContain(plan.ActionDataAccessCatalog, item => item.Execution == metadata.Key || item.Execution == copy.Key);
    }
}
