using ICP.Platform.Kernel.ModulePlanning;
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
        var declaration = Assert.Single(plan.ActionDataAccessCatalog, item => item.BusinessBinding == "workspace.folder.pasteObjectCopy");
        Assert.Equal(("workspace.folder.list", "pageActions", "pasteObjectCopy"), (declaration.ViewKey, declaration.Surface, declaration.ActionKey));
        Assert.Empty(declaration.Execution);
        Assert.Null(declaration.Requirements);
        Assert.Equal(new[] { "part.part", "document.document" }, declaration.Resources);
        var target = Assert.Single(declaration.GetObjectRequirements(), item => item.Object == "Document");
        Assert.Equal("document.document", target.ResourceKey);
        Assert.Equal(new[] { "documentObjectCopy" }, target.Actions);
        var workspace = Assert.Single(plan.Modules, module => module.ModuleId == "Workspace");
        Assert.DoesNotContain(workspace.DataExecutions, item => item.Key == "workspace.objectCopy");
        // Document文件复制本体尚未迁移，原owner来源与文件证明继续由同一业务实现消费。
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
