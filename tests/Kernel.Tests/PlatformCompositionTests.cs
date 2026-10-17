using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class PlatformCompositionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("{\"platformCompositionModuleId\":7}")]
    [InlineData("{\"platformCompositionModuleId\":\" \"}")]
    public void DistributionRole_MissingOrMalformedCannotBypassRequiredCompilation(string? json)
    {
        var root = Path.Combine(Path.GetTempPath(), "newp-platform-role-" + Guid.NewGuid().ToString("N"));
        try
        {
            var modules = Path.Combine(root, "modules");
            Directory.CreateDirectory(modules);
            if (json is not null) File.WriteAllText(Path.Combine(root, "module-composition.json"), json);
            if (json is null)
                Assert.Throws<FileNotFoundException>(() => TrustedBuiltInModuleSource.CreateForHostDistribution(root, modules));
            else
                Assert.Throws<System.Text.Json.JsonException>(() => TrustedBuiltInModuleSource.CreateForHostDistribution(root, modules));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void DistributionRole_DerivesRequiredMembersAndParticipatesInPlanIdentity()
    {
        var candidates = new[] { Capsule("Foundation", ["Group"]), Capsule("Group", ["Storage"]), Capsule("Storage"), Capsule("Example") };
        var compiler = new CanonicalModulePlanCompiler();
        var full = compiler.Compile("foundation-role", candidates, "Foundation").Plan!;
        var nested = compiler.Compile("foundation-role", candidates, "Group").Plan!;
        Assert.Equal(["Foundation", "Group", "Storage"], full.RequiredModuleIds);
        Assert.Equal(["Group", "Storage"], nested.RequiredModuleIds);
        Assert.Equal("Foundation", full.PlatformCompositionModuleId);
        Assert.NotEqual(full.PlanHash, nested.PlanHash);
        Assert.DoesNotContain("Example", full.RequiredModuleIds);
        Assert.Equal(full.CanonicalBytes, compiler.Compile("foundation-role", candidates.Reverse(), "Foundation").Plan!.CanonicalBytes);
    }

    [Theory]
    [InlineData("Foundation")]
    [InlineData("Group")]
    [InlineData("Storage")]
    public void DirectDisablement_RejectsEveryRequiredLevel(string disabled)
    {
        var candidates = new[] { Capsule("Foundation", ["Group"]), Capsule("Group", ["Storage"]), Capsule("Storage") }
            .Select(candidate => candidate with { Enabled = candidate.ModuleId != disabled }).ToArray();
        var result = new CanonicalModulePlanCompiler().Compile("disabled-level", candidates, "Foundation");
        Assert.Null(result.Plan);
        Assert.Equal(disabled, Assert.Single(result.Diagnostics).ModuleId);
        Assert.False(Assert.Single(candidates, candidate => candidate.ModuleId == disabled).Enabled);
    }

    [Fact]
    public void DisabledAncestor_RejectsRequiredCompositionWithoutRewritingDescendants()
    {
        var candidates = new[] { Capsule("Product", ["Foundation"], false), Capsule("Foundation", ["Storage"]), Capsule("Storage") };
        var result = new CanonicalModulePlanCompiler().Compile("masked-foundation", candidates, "Foundation");
        Assert.Null(result.Plan);
        Assert.Equal("MODULE_REQUIRED_ANCESTOR_DISABLED", Assert.Single(result.Diagnostics).Code);
        Assert.True(candidates[1].Enabled);
        Assert.True(candidates[2].Enabled);
    }

    [Fact]
    public void MissingPlatformOrReferencedMember_RejectsCompleteCandidate()
    {
        var compiler = new CanonicalModulePlanCompiler();
        var absent = compiler.Compile("missing-role", [Capsule("Storage")], "Foundation");
        var uninstalled = compiler.Compile("missing-member", [Capsule("Foundation", ["Storage"])], "Foundation");
        Assert.Null(absent.Plan);
        Assert.Equal("MODULE_PLATFORM_COMPOSITION_MISSING", Assert.Single(absent.Diagnostics).Code);
        Assert.Null(uninstalled.Plan);
        Assert.Equal("MODULE_COMPOSITION_MEMBER_MISSING", Assert.Single(uninstalled.Diagnostics).Code);
        Assert.Equal("MODULE_PLATFORM_COMPOSITION_INVALID", Assert.Single(compiler.Compile("empty-role", [Capsule("Foundation")], "Foundation").Diagnostics).Code);
    }

    [Fact]
    public void DisabledRequiredDescendant_RejectsWholeCandidateWithoutChangingSelection()
    {
        var candidates = new[] { Capsule("Foundation", ["Group"]), Capsule("Group", ["Storage"]), Capsule("Storage", enabled: false) };
        var result = new CanonicalModulePlanCompiler().Compile("disabled-foundation", candidates, "Foundation");
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "MODULE_REQUIRED_DISABLED" && diagnostic.ModuleId == "Storage");
        Assert.False(candidates[2].Enabled);
    }

    [Fact]
    public void RealPlatformAuthoring_ContainsRequiredFoundationAndKeepsBusinessDependencies()
    {
        var candidates = new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules"));
        var platform = Assert.Single(candidates, candidate => candidate.ModuleId == "Platform");
        Assert.Equal(["Audit", "Authentication", "Authorization", "ConfigCenter", "Files", "Identity", "MetadataConfigCenter", "Organization", "PluginOps"],
            platform.CompositionMembers.Order(StringComparer.Ordinal));
        Assert.Null(platform.BackendEntry);
        Assert.Empty(platform.ModelKeys);
        Assert.Equal(["Audit", "Document", "Part"], Assert.Single(candidates, candidate => candidate.ModuleId == "Workspace").Dependencies.Order(StringComparer.Ordinal));
        var source = TrustedBuiltInModuleSource.CreateForHostDistribution(AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "modules"));
        var result = new CanonicalModulePlanCompiler().Compile("real-foundation", candidates, source.PlatformCompositionModuleId);
        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(["Audit", "Authentication", "Authorization", "ConfigCenter", "Files", "Identity", "MetadataConfigCenter", "Organization", "Platform", "PluginOps"], result.Plan!.RequiredModuleIds);
        Assert.True(ModuleDependencyPolicy.Allows(result.Plan, "Workspace", "Identity"));
        Assert.True(ModuleDependencyPolicy.Allows(result.Plan, "Workspace", "Part"));
        Assert.True(ModuleDependencyPolicy.Allows(result.Plan, "Workspace", "Audit"));
        Assert.False(ModuleDependencyPolicy.Allows(result.Plan, "Part", "Document"));
        Assert.False(ModuleDependencyPolicy.Allows(result.Plan, "Workspace", "Missing"));
    }

    private static ModuleCapsuleCandidate Capsule(string id, string[]? members = null, bool enabled = true) =>
        new(id, "1.0.0", id, null, "fixtures/" + id, null, null, null, null,
            "behavior-bindings", [], [], [], [], [], new("fixture", "fixtures/" + id + "/plugin.json", "/"))
        { CompositionMembers = members ?? [], Enabled = enabled };
}
