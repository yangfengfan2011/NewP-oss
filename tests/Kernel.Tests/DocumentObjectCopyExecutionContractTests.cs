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
        // owner业务检查留在原读取/候选时点，旧固定权限目录不再表达这些内部阶段。
        var owner = Assert.Single(plan.Modules, module => module.ModuleId == "Document");
        string[] retired = ["document.document.copy.source", "document.files.copyMetadata", "document.files.copySource",
            "document.document.copy.finalCandidate", "document.document.internal.create"];
        Assert.DoesNotContain(owner.DataExecutions, item => retired.Contains(item.Key));
        Assert.DoesNotContain(owner.DataExecutionProviders, item => item.Key == "document.files.copySource");
        Assert.DoesNotContain(plan.ActionDataAccessCatalog, item => retired.Contains(item.Execution));
        Assert.Contains(plan.ActionDataAccessCatalog, item => item.Execution == "document.document.read" && item.ViewKey == "document.document.detail");
    }
}
