using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Planning;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class DataScopeReadConstraintRegistrationTests
{
    [Theory]
    [InlineData("missing", "DATA_SCOPE_READ_CONSTRAINT_BINDING_MISSING")]
    [InlineData("duplicate", "DATA_SCOPE_READ_CONSTRAINT_DUPLICATE")]
    [InlineData("owner", "DATA_SCOPE_READ_CONSTRAINT_OWNER_INVALID")]
    [InlineData("resource", "DATA_SCOPE_READ_CONSTRAINT_RESOURCE_UNKNOWN")]
    [InlineData("implementation", "DATA_SCOPE_READ_CONSTRAINT_BINDING_MISSING")]
    public void Preparation_RejectsInvalidExplicitBindingWithoutConstructingScopedConstraint(string invalid, string expected)
    {
        using var services = Services(invalid);
        var error = Assert.Throws<InvalidOperationException>(() => Prepare(services, invalid));
        Assert.Equal(expected, error.Message);
    }

    [Fact]
    public void Preparation_UsesStaticBindingAndNeverConstructsScopedFolderConstraint()
    {
        using var services = Services("valid");
        var catalog = Prepare(services, "valid");
        Assert.Equal("workspace.folder.scope", Assert.Single(catalog.Resources).ReadConstraintKey);
    }

    [Fact]
    public void Resource_PreservesLegacyFourArgumentConstructorAndDeconstruction()
    {
        var legacy = new DataScopeResource("workspace.folder", "Workspace", "WorkspaceFolder", "EID");
        var (resource, owner, model, field) = legacy;
        Assert.Equal("workspace.folder", resource);
        Assert.Equal("Workspace", owner);
        Assert.Equal("WorkspaceFolder", model);
        Assert.Equal("EID", field);
        Assert.Null(legacy.ReadConstraintKey);
        Assert.NotNull(typeof(DataScopeResource).GetConstructor([typeof(string), typeof(string), typeof(string), typeof(string)]));
    }

    [Theory]
    [InlineData("duplicate", "DATA_SCOPE_RESOURCE_ACTION_DUPLICATE")]
    [InlineData("owner", "DATA_SCOPE_RESOURCE_ACTION_OWNER_INVALID")]
    [InlineData("resource", "DATA_SCOPE_RESOURCE_ACTION_RESOURCE_UNKNOWN")]
    [InlineData("unknown-key", "DATA_SCOPE_RESOURCE_ACTION_KEYS_INVALID")]
    [InlineData("duplicate-key", "DATA_SCOPE_RESOURCE_ACTION_KEYS_INVALID")]
    public void Preparation_RejectsInvalidResourceActionDeclarations(string invalid, string expected)
    {
        using var services = Services("valid");
        var binding = new DataScopeResourceActionBinding(invalid == "resource" ? "unknown" : "workspace.folder",
            invalid == "owner" ? "OtherOwner" : "Workspace", invalid == "unknown-key" ? ["unregistered"]
                : invalid == "duplicate-key" ? ["read", "read"] : ["read"]);
        var exception = Assert.Throws<InvalidOperationException>(() => Prepare(services, "valid",
            invalid == "duplicate" ? [binding, binding] : [binding]));
        Assert.Equal(expected, exception.Message);
    }

    [Fact]
    public void Preparation_ResourceActionOptInDoesNotChangeUnspecifiedLegacyResource()
    {
        using var services = Services("valid");
        Assert.Null(Prepare(services, "valid").ActionKeysFor("workspace.folder"));
        var prepared = Prepare(services, "valid", [new("workspace.folder", "Workspace", ["read"])]);
        Assert.Equal(new[] { "read" }, prepared.ActionKeysFor("workspace.folder"));
    }

    [Fact, Trait("Category", "ModuleMetadataConsumer")]
    public void Preparation_ButtonRequirementsMustApplyToTheRealRegisteredResource()
    {
        using var services = Services("valid");
        var requirement = new ActionDataAccessDeclaration("Workspace", "workspace.folder.list", "rowActions", "inspect",
            "", ["workspace.folder"], ["edit"]) { BusinessBinding = "workspace.folder.inspect", Object = "WorkspaceFolder" };
        var failure = Assert.Throws<InvalidOperationException>(() => Prepare(services, "valid",
            [new("workspace.folder", "Workspace", ["read"])], requirement));
        Assert.Equal("ACTION_DATA_ACCESS_RESOURCE_ACTION_INVALID", failure.Message);
        Assert.NotNull(Prepare(services, "valid", [new("workspace.folder", "Workspace", ["read"])], requirement with { Actions = ["read"] }));
    }

    // 异构角色的摘要不能被当作每个资源都要求的动作；非法资源动作仍在正式catalog拒绝。
    [Theory, Trait("Category", "ModuleMetadataConsumer")]
    [InlineData(false)]
    [InlineData(true)]
    public void Preparation_ReadSourceStructuredRolesValidateOnlyTheCurrentResourceAlternatives(bool invalid)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        string[] resources = ["workspace.folder", "workspace.other"];
        var contract = new DataExecutionContract("workspace.heterogeneous", "Workspace", "workspace.backend", "entry", ["workspace.folder.read"],
            [new("targets", "read", "objects", resources, [["read"]], 1, 5)
            { ResourceAnyOf = new Dictionary<string, IReadOnlyList<IReadOnlyList<string>>>
                { [resources[0]] = [["edit"]], [resources[1]] = [[invalid ? "edit" : "delete"]] } }]);
        var module = new CanonicalModulePlanModule("Workspace", "1.1.0", "工作空间", null, "", null, null,
            null, null, "", [], [], [], [], [], new("test", "test", "/"))
        {
            DataActions = [new("Workspace", "read", "读取", "data.read"), new("Workspace", "edit", "编辑", "data.edit"), new("Workspace", "delete", "删除", "data.delete")],
            DataExecutions = [contract], Features = [new("Workspace", "read", "navigation", "读取", null, "workspace.folder.read")
            { DataResources = resources, DataAccess = [new("Workspace", "mixed", "read", "", contract.Key, resources, [])] }]
        };
        var plan = new CanonicalModulePlan("candidate", "1.1.0", "", "", [], [module]);
        var persistence = new CompiledPersistencePlan("candidate", "snapshot", "hash", PersistenceProvider.SqlServer,
            "digest", [], new WorkspaceModelMetadataProvider().GetDefinitions(), []);
        DataScopeResourceCatalog PrepareStructured() => new(new StandardCrudResourceRegistry([]), plan, persistence,
            resources.Select(key => new DataScopeResource(key, "Workspace", "WorkspaceFolder", "EID")), [], services.GetRequiredService<IServiceProviderIsKeyedService>(),
            [new(resources[0], "Workspace", ["read", "edit"]), new(resources[1], "Workspace", ["read", "delete"])]);
        if (invalid) Assert.Equal("ACTION_DATA_ACCESS_RESOURCE_ACTION_INVALID", Assert.Throws<InvalidOperationException>(PrepareStructured).Message);
        else Assert.Equal(2, PrepareStructured().Resources.Count);
    }

    [Theory, Trait("Category", "ModuleMetadataConsumer")]
    [InlineData("valid")]
    [InlineData("model")]
    [InlineData("action")]
    public void Preparation_ArrayEntriesUseTheirOwnRegisteredObjectAndApplicableActions(string problem)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        string[] resources = ["workspace.folder", "workspace.space"];
        var module = new CanonicalModulePlanModule("Workspace", "1.1.0", "工作空间", null, "", null, null,
            null, null, "", [], [], [], [], [], new("test", "test", "/"))
        {
            DataActions = [new("Workspace", "read", "读取", "data.read"), new("Workspace", "edit", "编辑", "data.edit"), new("Workspace", "delete", "删除", "data.delete")],
            Features = [new("Workspace", "manage", "navigation", "管理", null, "workspace.manage")
            {
                DataResources = resources,
                DataAccess = [new("Workspace", "mixed", "rowActions", "manage", "", resources, [])
                {
                    BusinessBinding = "workspace.manage",
                    ObjectRequirements = [new("WorkspaceFolder", ["read", "edit"], resources[0]),
                        new(problem == "model" ? "UnknownSpace" : "WorkspaceSpace", [problem == "action" ? "edit" : "delete"], resources[1])]
                }]
            }]
        };
        var plan = new CanonicalModulePlan("candidate", "1.1.0", "", "", [], [module]);
        var persistence = new CompiledPersistencePlan("candidate", "snapshot", "hash", PersistenceProvider.SqlServer,
            "digest", [], new WorkspaceModelMetadataProvider().GetDefinitions(), []);
        DataScopeResourceCatalog PrepareArray() => new(new StandardCrudResourceRegistry([]), plan, persistence,
            [new(resources[0], "Workspace", "WorkspaceFolder", "EID"), new(resources[1], "Workspace", "WorkspaceSpace", "EID")],
            [], services.GetRequiredService<IServiceProviderIsKeyedService>(),
            [new(resources[0], "Workspace", ["read", "edit"]), new(resources[1], "Workspace", ["read", "delete"])]);
        if (problem == "valid") Assert.Equal(2, PrepareArray().Resources.Count);
        else Assert.Equal(problem == "model" ? "BUTTON_DATA_ACCESS_OBJECT_MISMATCH" : "ACTION_DATA_ACCESS_RESOURCE_ACTION_INVALID",
            Assert.Throws<InvalidOperationException>(PrepareArray).Message);
    }

    private static ServiceProvider Services(string invalid)
    {
        var services = new ServiceCollection();
        if (invalid != "implementation")
            services.AddKeyedScoped<IDataScopeResourceReadConstraint>("workspace.folder.scope", (_, _) =>
                throw new InvalidOperationException("scoped constraint must not be constructed during preparation"));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static DataScopeResourceCatalog Prepare(ServiceProvider services, string invalid, DataScopeResourceActionBinding[]? actions = null,
        ActionDataAccessDeclaration? requirement = null)
    {
        var module = new CanonicalModulePlanModule("Workspace", "1.1.0", "工作空间", null, "", null, null,
            null, null, "", [], [], [], [], [], new("test", "test", "/"))
            { DataActions = [new("Workspace", "read", "读取", "data.read"), new("Workspace", "edit", "编辑", "data.edit")],
                DataExecutions = requirement is null || requirement.BusinessBinding is not null ? [] : [new(requirement.Execution, "Workspace", "workspace.backend", "entry", ["workspace.folder.read"],
                    [new("target", "read", "object", ["workspace.folder"], [requirement.Actions])])],
                Features = requirement is null ? [] : [new("Workspace", "read", "navigation", "读取", null, requirement.BusinessBinding ?? "workspace.folder.read")
                    { DataResources = ["workspace.folder"], DataAccess = [requirement.BusinessBinding is null ? requirement with { Actions = [] } : requirement] }] };
        var modules = new CanonicalModulePlan("candidate", "1.1.0", "", "", [], [module]);
        var models = new WorkspaceModelMetadataProvider().GetDefinitions();
        var persistence = new CompiledPersistencePlan("candidate", "snapshot", "hash", PersistenceProvider.SqlServer,
            "digest", [], models, []);
        var binding = new DataScopeResourceReadConstraintBinding("workspace.folder.scope",
            invalid == "resource" ? "unregistered" : "workspace.folder", invalid == "owner" ? "Authorization" : "Workspace");
        DataScopeResourceReadConstraintBinding[] bindings = invalid == "missing" ? []
            : invalid == "duplicate" ? [binding, binding] : [binding];
        return new(new StandardCrudResourceRegistry([]), modules, persistence,
            [new("workspace.folder", "Workspace", "WorkspaceFolder", "EID", "workspace.folder.scope")], bindings,
            services.GetRequiredService<IServiceProviderIsKeyedService>(), actions);
    }
}
