using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class ModuleCompositionTests
{
    [Fact]
    public void RealInforCenterAuthoring_PreservesMemberIdentityAndRuntimeBindings()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules"));
        var result = new CanonicalModulePlanCompiler().Compile("inforcenter-authoring", candidates);
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        var plan = result.Plan!;
        var composition = Assert.Single(plan.Modules, module => module.ModuleId == "InforCenter");
        Assert.Empty(composition.ModelKeys);
        Assert.Null(composition.Persistence);
        Assert.Null(composition.BackendEntry);
        Assert.Null(composition.FrontendEntry);

        using var catalog = JsonDocument.Parse(new ActiveModuleRuntimeCatalogBuilder().Build(plan));
        var entries = catalog.RootElement.GetProperty("modules").EnumerateArray().ToArray();
        var parent = Assert.Single(entries, entry => entry.GetProperty("moduleId").GetString() == "InforCenter");
        Assert.Equal(["Document", "Part", "Workspace"], parent.GetProperty("compositionMembers").EnumerateArray().Select(member => member.GetString()));
        foreach (var id in new[] { "Workspace", "Part", "Document" })
        {
            var member = Assert.Single(plan.Modules, module => module.ModuleId == id);
            Assert.Equal("1.0.0", member.Version);
            Assert.Contains(id + "." + (id == "Workspace" ? "WorkspaceFolder" : id), member.ModelKeys);
            Assert.NotNull(member.Persistence);
            Assert.Single(member.Persistence.MigrationBundles);
            var entry = Assert.Single(entries, entry => entry.GetProperty("moduleId").GetString() == id);
            Assert.Equal("InforCenter", entry.GetProperty("parentModuleId").GetString());
            Assert.Equal(id.ToLowerInvariant() + ".frontend", entry.GetProperty("frontend").GetProperty("behaviorKey").GetString());
        }
        Assert.Equal(["Document", "Part"],
            Assert.Single(plan.Modules, module => module.ModuleId == "Workspace").Dependencies);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DuplicateMember_RejectsWholeCandidateEvenWhenParentIsDisabled(bool enabled)
    {
        var result = new CanonicalModulePlanCompiler().Compile("duplicate-member",
            [Capsule("InforCenter", ["Part", "Part"], enabled), Capsule("Part")]);
        Assert.Null(result.Plan);
        Assert.Equal("MODULE_COMPOSITION_MEMBER_DUPLICATE", Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData("Missing", true, "MODULE_COMPOSITION_MEMBER_MISSING")]
    [InlineData("Missing", false, "MODULE_COMPOSITION_MEMBER_MISSING")]
    [InlineData("InforCenter", true, "MODULE_COMPOSITION_SELF_MEMBER")]
    [InlineData("InforCenter", false, "MODULE_COMPOSITION_SELF_MEMBER")]
    public void InvalidMember_RejectsWholeSelectedGraph(string member, bool enabled, string code)
    {
        var result = new CanonicalModulePlanCompiler().Compile("invalid-member", [Capsule("InforCenter", [member], enabled)]);
        Assert.Null(result.Plan);
        Assert.Equal(code, Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MultipleParents_RejectsWholeSelectedGraph(bool enabled)
    {
        var result = new CanonicalModulePlanCompiler().Compile("multiple-parents",
            [Capsule("InforCenter", ["Part"], enabled), Capsule("Document", ["Part"]), Capsule("Part")]);
        Assert.Null(result.Plan);
        Assert.Equal("MODULE_COMPOSITION_MULTIPLE_PARENTS", Assert.Single(result.Diagnostics).Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MultiLevelCycle_RejectsWholeSelectedGraph(bool enabled)
    {
        var result = new CanonicalModulePlanCompiler().Compile("composition-cycle",
            [Capsule("InforCenter", ["Workspace"], enabled), Capsule("Workspace", ["Part"]), Capsule("Part", ["InforCenter"])]);
        Assert.Null(result.Plan);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MODULE_COMPOSITION_CYCLE", diagnostic.Code);
        Assert.Equal("InforCenter -> Workspace -> Part -> InforCenter", diagnostic.CanonicalKey);
    }

    [Fact]
    public void RealMembers_RegroupIntoMultipleLevelsWithoutChangingIdentityOrPersistence()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules"));
        var original = new CanonicalModulePlanCompiler().Compile("regroup", candidates).Plan!;
        var nested = candidates.Select(candidate => candidate.ModuleId switch
        {
            "InforCenter" => candidate with { CompositionMembers = ["Workspace"] },
            "Workspace" => candidate with { CompositionMembers = ["Part", "Document"] },
            _ => candidate
        }).ToArray();
        var compiler = new CanonicalModulePlanCompiler();
        var regrouped = compiler.Compile("regroup", nested);
        var reordered = compiler.Compile("regroup", nested.Reverse().Select(candidate => candidate with { CompositionMembers = candidate.CompositionMembers.Reverse().ToArray() }));
        Assert.True(regrouped.IsSuccess, string.Join(Environment.NewLine, regrouped.Diagnostics));
        Assert.True(reordered.IsSuccess, string.Join(Environment.NewLine, reordered.Diagnostics));
        Assert.Equal(regrouped.Plan!.CanonicalBytes, reordered.Plan!.CanonicalBytes);
        Assert.Equal("InforCenter", Assert.Single(regrouped.Plan.CompositionGraph, node => node.ModuleId == "Workspace").ParentModuleId);
        foreach (var id in new[] { "Workspace", "Part", "Document" })
        {
            var before = Assert.Single(original.Modules, module => module.ModuleId == id);
            var after = Assert.Single(regrouped.Plan.Modules, module => module.ModuleId == id);
            Assert.Equal(before.ModuleId, after.ModuleId);
            Assert.Equal(before.Version, after.Version);
            Assert.Equal(before.ModelKeys, after.ModelKeys);
            Assert.Equal(before.Dependencies, after.Dependencies);
            Assert.Equal(before.BackendBehaviorKey, after.BackendBehaviorKey);
            Assert.Equal(before.FrontendBehaviorKey, after.FrontendBehaviorKey);
            Assert.Equal(before.Persistence!.ModelPaths, after.Persistence!.ModelPaths);
            Assert.Equal(before.Persistence.MigrationBundles, after.Persistence.MigrationBundles);
            if (id != "Workspace") Assert.Equal("Workspace", Assert.Single(regrouped.Plan.CompositionGraph, node => node.ModuleId == id).ParentModuleId);
        }
    }

    [Fact]
    public void DisabledBusinessMember_DoesNotReplaceWorkspacesExplicitDependency()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules"));
        var result = new CanonicalModulePlanCompiler().Compile("workspace-disabled-part",
            candidates.Select(candidate => candidate.ModuleId == "Part" ? candidate with { Enabled = false } : candidate));
        Assert.Null(result.Plan);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MODULE_DEPENDENCY_MISSING", diagnostic.Code);
        Assert.Equal("Workspace", diagnostic.ModuleId);
        Assert.Equal("Part", diagnostic.CanonicalKey);
    }

    [Fact]
    public void MemberCaseVariants_RejectDuplicateDeclaration()
    {
        var result = new CanonicalModulePlanCompiler().Compile("duplicate-case", [Capsule("InforCenter", ["Part", "part"]), Capsule("Part")]);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "MODULE_COMPOSITION_MEMBER_DUPLICATE");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"members\":false}")]
    [InlineData("{\"members\":[\"Part\",7]}")]
    [InlineData("{\"members\":[\"\"]}")]
    public void MalformedComposition_IsNotSilentlyDiscarded(string composition)
    {
        var root = Path.Combine(Path.GetTempPath(), "newp-composition-shape-" + Guid.NewGuid().ToString("N"));
        try
        {
            var moduleRoot = Path.Combine(root, "InforCenter");
            Directory.CreateDirectory(moduleRoot);
            File.WriteAllText(Path.Combine(moduleRoot, "plugin.json"),
                "{\"moduleId\":\"InforCenter\",\"version\":\"1.0.0\",\"displayName\":\"InforCenter\",\"composition\":" + composition + "}");
            Assert.Throws<JsonException>(() => new ModuleCapsuleCandidateReader().ReadBuiltIn(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static ModuleCapsuleCandidate Capsule(string id, string[]? members = null, bool enabled = true) =>
        new(id, "1.0.0", id, null, "fixtures/" + id, null, null, null, null,
            "behavior-bindings", [], [], [], [], [], new("fixture", "fixtures/" + id + "/newp.plugin.json", "/"))
        { CompositionMembers = members ?? [], Enabled = enabled };
}
