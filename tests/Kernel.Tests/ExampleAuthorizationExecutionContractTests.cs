using System.Text.Json;
using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

[Trait("Category", "ModuleMetadataConsumer")]
public sealed class ExampleAuthorizationExecutionContractTests
{
    // 正式编译产物同时供API与PageEntry消费；显式导出只写临时目录，普通测试不写authoring。
    [Fact]
    public void FormalExampleAuthorization_UsesCapabilityOwnerFloorAndSharedProjection()
    {
        var result = new CanonicalModulePlanCompiler().Compile("example-authorization", Candidates());
        Assert.True(result.IsSuccess, JsonSerializer.Serialize(result.Diagnostics));
        var requirements = result.Plan!.ActionDataAccessCatalog.Where(item => item.ModuleId == "Example").ToArray();
        var management = requirements.Where(item => item.Provider?.StartsWith("authorization.object.", StringComparison.Ordinal) == true).ToArray();
        Assert.NotEmpty(management);
        Assert.All(management, requirement =>
        {
            Assert.Equal("Authorization", requirement.ProviderModuleId);
            Assert.All(requirement.Requirements!.Where(role => role.Role == "target"),
                role => Assert.Equal(new[] { "read", "authorize" }, Assert.Single(role.AnyOf)));
            if (requirement.Provider == "authorization.object.manage")
            {
                var view = Assert.Single(requirement.Requirements!, role => role.Role == "view");
                Assert.Equal(("prepare", "object"), (view.Stage, view.Shape));
                Assert.Equal(new[] { "read" }, Assert.Single(view.AnyOf));
            }
        });
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
    [InlineData("legacy")]
    [InlineData("unbound")]
    [InlineData("resource")]
    public void FormalProviderConsumer_RejectsUnknownOwnerAndRequirementRewrites(string problem)
    {
        var candidates = Candidates();
        var owner = Array.FindIndex(candidates, item => item.ModuleId == "Authorization");
        var consumer = Array.FindIndex(candidates, item => item.ModuleId == "Example");
        var providers = candidates[owner].DataExecutionProviders.ToArray();
        providers[0] = problem switch { "provider-owner" => providers[0] with { ModuleId = "Example" },
            "backend" => providers[0] with { BackendBehaviorKey = "unregistered.backend" }, _ => providers[0] };
        candidates[owner] = candidates[owner] with { DataExecutionProviders = problem == "unknown-provider" ? [] : providers };
        var contracts = candidates[consumer].DataExecutions.Select(contract => contract.Key != "example.record.authorization.manage" ? contract : problem switch
        {
            "lower-floor" => contract with { Requirements = [new("target", "prepare", "object", ["example.record"], [["read"]])] },
            "legacy" => contract with { LegacyActions = true },
            "resource" => contract with { TargetResource = "organization.person" },
            _ => contract
        }).ToArray();
        var features = candidates[consumer].Features.Select(feature => feature with
        { DataAccess = feature.DataAccess.Where(binding => problem != "unbound" || binding.Execution != "example.record.authorization.manage").ToArray() }).ToArray();
        candidates[consumer] = candidates[consumer] with { DataExecutions = contracts, Features = features };
        Assert.False(new CanonicalModulePlanCompiler().Compile(problem, candidates).IsSuccess);
    }

    // 当前没有正式纯API采用者；合成合同只锁定分类，不能把界面请求缺声明变成固定API回退。
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
