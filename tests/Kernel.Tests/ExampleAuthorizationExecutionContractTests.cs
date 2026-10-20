using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class ExampleAuthorizationExecutionContractTests
{
    // 正式编译产物同时供API与PageEntry消费；显式导出只写临时目录，普通测试不写authoring。
    [Fact]
    public void FormalExampleAuthorization_KeepsQualificationAndSeparatesReadAndSaveBindings()
    {
        var result = new CanonicalModulePlanCompiler().Compile("example-authorization", Candidates());
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var requirements = result.Plan!.ActionDataAccessCatalog.Where(item => item.ModuleId == "Example").ToArray();
        var management = Assert.Single(requirements, item => item.ActionKey == "authorize" && item.Surface == "rowActions");
        var inspection = Assert.Single(requirements, item => item.ActionKey == "viewAuthorization" && item.Surface == "rowActions");
        Assert.Equal("example.record.authorize", management.BusinessBinding);
        Assert.Equal("example.record.viewAuthorization", inspection.BusinessBinding);
        Assert.Equal(new[] { "authorize" }, management.Actions);
        Assert.Equal(new[] { "read", "authorize" }, inspection.Actions);
        Assert.All(new[] { management, inspection }, source =>
        {
            Assert.Equal("ExampleRecord", source.Object); Assert.Null(source.Provider); Assert.Null(source.Requirements);
            Assert.All(source.FeaturePositions, position => Assert.Equal("example.record.authorize",
                Assert.Single(result.Plan.FeatureCatalog, point => point.Position == position).BehaviorKey));
        });
        Assert.DoesNotContain(result.Plan.Modules.SelectMany(module => module.DataExecutions), contract =>
            contract.Key is "example.record.authorization.manage" or "example.record.authorization.inspect");
        var root = Root();
        var json = JsonSerializer.Serialize(requirements, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        if (Environment.GetEnvironmentVariable("NEWP_DATA_EXECUTION_FIXTURE_EXPORT_PATH") is { Length: > 0 } exportPath)
            File.WriteAllText(exportPath, json);
        else if (Environment.GetEnvironmentVariable("NEWP_AUTHORIZATION_FIXTURE_EXPORT") == "1")
            File.WriteAllText(Path.Combine(root, ".codex-temp", "issue-305", "307", "ExampleDataAccess.contract.json"), json);
        else
            Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json)), JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(
                File.ReadAllText(Path.Combine(root, "tests", "Kernel.Tests", "Fixtures", "ExampleCrudDataAccess.contract.json")))));
    }

    [Theory]
    [InlineData("provider-owner")]
    [InlineData("backend")]
    [InlineData("unknown-provider")]
    [InlineData("lower-floor")]
    [InlineData("unbound")]
    [InlineData("resource")]
    public void FormalProviderConsumer_RejectsUnknownOwnerAndRequirementRewrites(string problem)
    {
        var candidates = Candidates();
        var owner = Array.FindIndex(candidates, item => item.ModuleId == "Authorization");
        var consumer = Array.FindIndex(candidates, item => item.ModuleId == "Part");
        var providers = candidates[owner].DataExecutionProviders.ToArray();
        var providerIndex = Array.FindIndex(providers, item => item.Key == "authorization.object.view");
        providers[providerIndex] = problem switch { "provider-owner" => providers[providerIndex] with { ModuleId = "Part" },
            "backend" => providers[providerIndex] with { BackendBehaviorKey = "unregistered.backend" }, _ => providers[providerIndex] };
        candidates[owner] = candidates[owner] with { DataExecutionProviders = problem == "unknown-provider" ? [] : providers };
        var contracts = candidates[consumer].DataExecutions.Select(contract => contract.Key != "part.part.authorization.view" ? contract : problem switch
        {
            "lower-floor" => contract with { Requirements = [new("view", "prepare", "object", ["part.part"], [[]])] },
            "resource" => contract with { TargetResource = "organization.person" },
            _ => contract
        }).ToArray();
        var features = candidates[consumer].Features.Select(feature => feature with
        { DataAccess = feature.DataAccess.Where(binding => problem != "unbound" || binding.Execution != "part.part.authorization.view").ToArray() }).ToArray();
        candidates[consumer] = candidates[consumer] with { DataExecutions = contracts, Features = features };
        Assert.False(new CanonicalModulePlanCompiler().Compile(problem, candidates).IsSuccess);
    }

    // 合成固定API合同只锁定分类，不能把界面请求缺声明变成固定API回退。
    [Theory]
    [InlineData("api-missing")]
    [InlineData("api-ui")]
    [InlineData("api-internal")]
    [InlineData("internal-missing-caller")]
    public void NonUiClassification_RequiresItsOwnFixedBinding(string problem)
    {
        var plan = new CanonicalModulePlanCompiler().Compile("classification", Candidates()).Plan!;
        var contract = new DataExecutionContract("synthetic.api", "Example", "example.backend", "api", [],
            [new("target", "submit", "object", ["example.record"], [["read"]])])
        { FixedBindings = [new("api", "synthetic-only", "POST", "/synthetic-test-only")] };
        Assert.Single(DataExecutionContracts.Compile([contract], plan.DataActionCatalog));
        contract = problem switch
        {
            "api-missing" => contract with { FixedBindings = [] },
            "api-ui" => contract with { FeatureBehaviors = ["example.record.authorize"] },
            "api-internal" => contract with { FixedBindings = [new("internal", "synthetic-only", Caller: "example.backend")] },
            _ => contract with { Boundary = "internal", FixedBindings = [new("internal", "synthetic-only")] }
        };
        Assert.Throws<InvalidOperationException>(() => DataExecutionContracts.Compile([contract], plan.DataActionCatalog));
    }

    // 测试沿正式built-in输入，不手工补模块、功能位置或要求。
    private static ModuleCapsuleCandidate[] Candidates() => new ModuleCapsuleCandidateReader().ReadBuiltIn(Path.Combine(AppContext.BaseDirectory, "modules")).ToArray();

    // 冻结fixture位于源码树，运行产物只用于显式导出。
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root missing.");
    }
}
