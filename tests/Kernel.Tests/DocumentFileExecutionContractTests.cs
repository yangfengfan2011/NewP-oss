using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class DocumentFileExecutionContractTests
{
    [Fact]
    public void FormalDocumentDownload_UsesOwnerFloorAndExistingFeatureAction()
    {
        var result = new CanonicalModulePlanCompiler().Compile("document-files", Candidates());
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var plan = result.Plan!;
        var download = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "document.files.download");
        Assert.Equal("Document", download.ModuleId);
        Assert.Equal("Document", download.ProviderModuleId);
        Assert.Equal("document.files.download", download.Provider);
        Assert.Equal(("document.document.detail", "featureActions", "download"), (download.ViewKey, download.Surface, download.ActionKey));
        var feature = Assert.Single(plan.FeatureCatalog, point => point.BehaviorKey == "document.document.download");
        Assert.Equal(new[] { feature.Position }, download.FeaturePositions);
        var target = Assert.Single(download.Requirements!);
        Assert.Equal(("target", "read", "object"), (target.Role, target.Stage, target.Shape));
        Assert.Equal(new[] { "document.document" }, target.Resources);
        Assert.Equal(new[] { "read", "download" }, Assert.Single(target.AnyOf));
        Assert.Equal("document.files.currentRead", target.ReadResolverKey);
        var feedback = DataExecutionProviders.Resolve(plan.Modules.SelectMany(module => module.DataExecutions).Where(item => item.Key == "document.files.download.feedback").ToArray(),
            plan.Modules.SelectMany(module => module.DataExecutionProviders).ToArray()).Single();
        Assert.Equal(JsonSerializer.Serialize(download.Requirements), JsonSerializer.Serialize(feedback.Requirements));
        var manifest = new MetadataRuntimeManifestBuilder().Build(AppContext.BaseDirectory, plan.Modules.Select(module => module.ModuleId).ToArray(),
            plan.Modules.Select(module => module.ModuleId).ToArray(), plan.FeatureCatalog, plan.ActionDataAccessCatalog);
        using var json = JsonDocument.Parse(manifest);
        var module = json.RootElement.GetProperty("modules").EnumerateArray().Single(item => item.GetProperty("moduleId").GetString() == "Document");
        var projection = JsonSerializer.Serialize(module, new JsonSerializerOptions { WriteIndented = true });
        if (Environment.GetEnvironmentVariable("NEWP_DOCUMENT_FILES_FIXTURE_EXPORT_PATH") is { Length: > 0 } export)
            File.WriteAllText(export, projection);
        else
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
            Assert.NotNull(root);
            Assert.Equal(JsonSerializer.Serialize(module), JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(
                File.ReadAllText(Path.Combine(root.FullName, "tests", "Kernel.Tests", "Fixtures", "DocumentFileDataAccess.contract.json")))));
        }
    }

    [Fact]
    public void DocumentLifecycle_UsesCandidateCreateAndFixedReleaseWithoutAnInventedEntry()
    {
        var result = new CanonicalModulePlanCompiler().Compile("document-lifecycle", Candidates());
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var plan = result.Plan!;
        var owner = Assert.Single(plan.Modules, item => item.ModuleId == "Document");
        var create = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "document.document.create");
        Assert.Equal(("document.document.list", "pageActions", "create", "standardCrud.create"),
            (create.ViewKey, create.Surface, create.ActionKey, create.Provider));
        var candidate = Assert.Single(create.Requirements!);
        Assert.Equal(("candidate", "submit", "candidate"), (candidate.Role, candidate.Stage, candidate.Shape));
        Assert.Equal(new[] { "create" }, Assert.Single(candidate.AnyOf));
        var internalCreate = Assert.Single(owner.DataExecutions, item => item.Key == "document.document.internal.create");
        Assert.Equal("internal", internalCreate.Boundary);
        Assert.Equal(new[] { "ICP.Platform.Modules.Document.DocumentFiles.ValidateUploadAsync", "ICP.Platform.Modules.Document.DocumentFiles.CreateWithFilesAsync" }, internalCreate.FixedBindings.Select(item => item.Caller));
        var release = Assert.Single(owner.DataExecutions, item => item.Key == "document.files.release");
        Assert.Equal("internal", release.Boundary);
        Assert.Equal("ICP.Platform.Modules.Document.DocumentFileOwnerPolicy.VerifyBatchAsync", Assert.Single(release.FixedBindings).Caller);
        var effective = Assert.Single(DataExecutionProviders.Resolve([release], owner.DataExecutionProviders));
        var target = Assert.Single(effective.Requirements);
        Assert.Equal(("target", "submit", "object"), (target.Role, target.Stage, target.Shape));
        Assert.Equal(new[] { "read", "delete" }, Assert.Single(target.AnyOf));
        Assert.Equal("document.files.currentRead", target.ReadResolverKey);
        Assert.DoesNotContain(plan.ActionDataAccessCatalog, item => item.Execution is "document.document.internal.create" or "document.document.update" or "document.document.delete" or "document.files.release");
        var descriptor = ICP.Platform.Modules.Document.DocumentCrudDescriptor.Create();
        Assert.Empty(descriptor.LegacyDataExecutions);
        Assert.Equal(4, descriptor.LegacyObjectOperationBindings.Count);
    }

    [Theory]
    [InlineData("requirements")]
    [InlineData("legacy")]
    [InlineData("provider")]
    public void DownloadConsumer_CannotReplaceItsOwnerRequirements(string problem)
    {
        var candidates = Candidates();
        var index = Array.FindIndex(candidates, module => module.ModuleId == "Document");
        var owner = candidates[index];
        candidates[index] = owner with
        {
            DataExecutionProviders = problem == "provider" ? [] : owner.DataExecutionProviders,
            DataExecutions = owner.DataExecutions.Select(contract => contract.Key != "document.files.download" ? contract : problem switch
            {
                "requirements" => contract with { Requirements = [new("target", "read", "object", ["document.document"], [["read"]])] },
                "legacy" => contract with { LegacyActions = true },
                _ => contract
            }).ToArray()
        };
        Assert.False(new CanonicalModulePlanCompiler().Compile("document-files-invalid", candidates).IsSuccess);
    }

    private static ModuleCapsuleCandidate[] Candidates() => new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
}
