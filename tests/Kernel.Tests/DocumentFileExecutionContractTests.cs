using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class DocumentFileExecutionContractTests
{
    [Fact]
    public void FormalDocumentDownload_UsesOwnButtonAndPreservesIndependentRead()
    {
        var result = new CanonicalModulePlanCompiler().Compile("document-files", Candidates());
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var plan = result.Plan!;
        var owner = Assert.Single(plan.Modules, item => item.ModuleId == "Document");
        var download = Assert.Single(plan.ActionDataAccessCatalog, item => item.BusinessBinding == "document.document.download");
        Assert.Equal("Document", download.ModuleId);
        Assert.Equal("Document", download.Object);
        Assert.Equal(new[] { "document.document" }, download.Resources);
        Assert.Equal(new[] { "download" }, download.Actions);
        Assert.Empty(download.Execution); Assert.Null(download.Provider); Assert.Null(download.Requirements);
        Assert.Equal(("document.document.detail", "featureActions", "download"), (download.ViewKey, download.Surface, download.ActionKey));
        var feature = Assert.Single(plan.FeatureCatalog, point => point.BehaviorKey == "document.document.download");
        Assert.Equal(new[] { feature.Position }, download.FeaturePositions);
        var read = Assert.Single(plan.ActionDataAccessCatalog, item => item.Execution == "document.document.read" && item.ViewKey == "document.document.detail");
        Assert.Equal("standardCrud.read", read.Provider);
        Assert.DoesNotContain(feature.Position, read.FeaturePositions);
        Assert.DoesNotContain(owner.DataExecutions, item => item.Key is "document.files.download" or "document.files.download.feedback");
        Assert.DoesNotContain(owner.DataExecutionProviders, item => item.Key == "document.files.download");
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
        var create = Assert.Single(plan.ActionDataAccessCatalog, item => item.BusinessBinding == "document.document.create");
        Assert.Equal(("document.document.list", "pageActions", "create", "Document"),
            (create.ViewKey, create.Surface, create.ActionKey, create.Object));
        Assert.Equal(new[] { "create" }, create.Actions);
        Assert.Null(create.Requirements);
        Assert.DoesNotContain(owner.DataExecutions, item => item.Key == "document.document.create");
        Assert.DoesNotContain(owner.DataExecutions, item => item.Key == "document.document.internal.create");
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
        Assert.False(descriptor.DataExecutions.ContainsKey("create"));
        Assert.Equal("document.document.list", descriptor.DataExecutions["list"]);
        Assert.Equal("document.document.read", descriptor.DataExecutions["read"]);
    }

    [Theory]
    [InlineData("unknown-action")]
    [InlineData("retired-execution")]
    public void DownloadButton_RejectsUnknownActionsAndMixedRetiredExecution(string problem)
    {
        var candidates = Candidates();
        var index = Array.FindIndex(candidates, module => module.ModuleId == "Document");
        var owner = candidates[index];
        candidates[index] = owner with
        {
            Features = owner.Features.Select(feature => feature with
            {
                DataAccess = feature.DataAccess.Select(declaration => declaration.BusinessBinding != "document.document.download" ? declaration
                    : problem == "unknown-action" ? declaration with { Actions = ["unregistered-action"] }
                    : declaration with { Execution = "document.files.download" }).ToArray()
            }).ToArray()
        };
        Assert.False(new CanonicalModulePlanCompiler().Compile("document-files-invalid", candidates).IsSuccess);
    }

    private static ModuleCapsuleCandidate[] Candidates() => new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();
}
