using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Part;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class PartObjectCopyExecutionContractTests
{
    [Fact]
    public void ActualPartCopyDeclarationKeepsSeparateReadCopyAndParentRolesAtTheTwoOriginalStages()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules"));
        var result = new CanonicalModulePlanCompiler().Compile("part-object-copy", candidates);
        Assert.True(result.IsSuccess, string.Join(";", result.Diagnostics.Select(item => item.Message)));
        var declaration = Assert.Single(result.Plan!.ActionDataAccessCatalog, item => item.Execution == "workspace.objectCopy");
        Assert.Equal("Workspace", declaration.ModuleId);
        Assert.Equal("workspace.backend", declaration.Provider);
        Assert.Equal("workspace.folder.list", declaration.ViewKey);
        Assert.Equal("pageActions", declaration.Surface);
        Assert.Equal("pasteObjectCopy", declaration.ActionKey);
        Assert.All(new[] { "prepare", "submit" }, stage =>
        {
            var roles = declaration.Requirements!.Where(requirement => requirement.Stage == stage && (requirement.Branch is null or "part")).ToArray();
            Assert.Equal(new[] { "visibility", "source", "parent" }, roles.Select(requirement => requirement.Role));
            Assert.Equal(new[] { "read" }, Assert.Single(roles[0].AnyOf));
            Assert.Equal(new[] { "partObjectCopy" }, Assert.Single(roles[1].AnyOf));
            Assert.Equal(new[] { "read", "edit" }, Assert.Single(roles[2].AnyOf));
            Assert.Equal("workspace.position", roles[2].ReadResolverKey);
            Assert.All(roles, requirement =>
            {
                Assert.Equal("object", requirement.Shape);
                Assert.Equal(1, requirement.MinimumCount);
                Assert.Equal(1, requirement.MaximumCount);
                Assert.DoesNotContain("document.document", requirement.Resources);
            });
        });
        Assert.Empty(PartCrudDescriptor.Create().LegacyObjectOperationBindings);
        var part = Assert.Single(result.Plan.Modules, module => module.ModuleId == "Part");
        var source = Assert.Single(part.DataExecutions, item => item.Key == "part.part.copy.source");
        Assert.Equal("internal", source.Boundary);
        Assert.Equal("part.backend", source.Provider);
        Assert.Equal("ICP.Platform.Modules.Part.PartCopyBehavior.ExecuteAsync", Assert.Single(source.FixedBindings).Caller);
        Assert.Equal(new[] { "visibility", "copy" }, source.Requirements.Select(role => role.Role));
        Assert.Equal(new[] { "read" }, Assert.Single(source.Requirements[0].AnyOf));
        Assert.Equal(new[] { "partObjectCopy" }, Assert.Single(source.Requirements[1].AnyOf));
        var final = Assert.Single(part.DataExecutions, item => item.Key == "part.part.copy.finalCandidate");
        Assert.Equal(("internal", "standardCrud.create"), (final.Boundary, final.Provider));
        Assert.Equal("ICP.Platform.Modules.Workspace.WorkspaceContents.CopyObjectAsync", Assert.Single(final.FixedBindings).Caller);
        Assert.DoesNotContain(result.Plan.ActionDataAccessCatalog, item => item.Execution == source.Key || item.Execution == final.Key);
        Assert.DoesNotContain(result.Plan.ActionDataAccessCatalog, item => item.Execution == "workspace.objectCopy"
            && item.ActionKey == "resolveObjectCopy");
    }
}