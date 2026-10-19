using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class WorkspaceFolderReadContractTests
{
    [Fact]
    public void ActualContentViews_FreezeOnlyTheirOwnLinkReadSource()
    {
        var source = TrustedBuiltInModuleSource.CreateForHost(AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "modules"));
        var candidates = new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(source);
        var compiled = new CanonicalModulePlanCompiler().Compile("workspace-content-read-314", candidates);
        Assert.True(compiled.IsSuccess, JsonSerializer.Serialize(compiled.Diagnostics));
        var plan = compiled.Plan!;
        if (Environment.GetEnvironmentVariable("NEWP_WORKSPACE_READ_FEATURE_EVIDENCE") == "1")
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
            File.WriteAllText(Path.Combine(root!.FullName, ".codex-temp", "issue-305", "314", "folder-read-canonical-candidate.json"),
                JsonSerializer.Serialize(new { plan.FeatureCatalog, plan.ActionDataAccessCatalog }));
        }
        var bindings = LinkQueryReadMetadata.Project(plan, "Workspace", "WorkspaceFolderContent", "workspace.content");
        Assert.Equal(new[] { "workspace.folder.list", "workspace.references.list" }, bindings.Select(item => item.ViewKey).Order().ToArray());
        Assert.All(bindings, item =>
        {
            Assert.Equal("workspace.content.read", item.Execution);
            Assert.Equal("workspace.backend", item.Provider);
            var query = Assert.Single(item.Requirements!);
            Assert.Equal("query", query.Role); Assert.Equal("read", query.Stage); Assert.Equal("query", query.Shape);
            Assert.Equal(0, query.MinimumCount); Assert.Null(query.MaximumCount);
            Assert.Equal(new[] { "read" }, Assert.Single(query.AnyOf));
            Assert.Equal(new[] { "document.document", "part.part", "workspace.folder" }, item.Resources.Order().ToArray());
        });
        var tree = Assert.Single(LinkTreeMetadata.Project(plan));
        Assert.NotNull(tree.ReadBinding);
        Assert.Equal("workspace.structure.read", tree.ReadBinding.Execution);
        Assert.DoesNotContain(bindings, item => item.Id == tree.ReadBinding.Id);
        Assert.Empty(LinkQueryReadMetadata.Project(plan, "Workspace", "WorkspaceFolder", "workspace.content"));
        Assert.Empty(LinkQueryReadMetadata.Project(plan, "Workspace", "WorkspaceFolderContent", "workspace.content.other"));
    }
}
