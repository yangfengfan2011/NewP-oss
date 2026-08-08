using ICP.Platform.Kernel.ModulePlanning;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class CanonicalModulePlanCompilerTests
{
    [Fact]
    public void Compile_ShuffledCapsulesAndFacts_ProducesIdenticalPlanBytesAndHash()
    {
        var organization = CreateCapsule(
            moduleId: "Organization",
            dependencies: ["Authorization"],
            routes: ["/organization/tree", "/organization/persons"],
            frontendBindings: ["organization.tree.page", "organization.positions.page"],
            permissions: ["Organization.Person.Update", "Organization.Person.View"],
            models: ["Organization.Person", "Organization.Department"]);
        var authorization = CreateCapsule(
            moduleId: "Authorization",
            dependencies: [],
            routes: ["/authorization/roles"],
            frontendBindings: ["authorization.roles.page"],
            permissions: ["Authorization.Role.View"],
            models: ["Authorization.Role"]);

        var compiler = new CanonicalModulePlanCompiler();

        var first = compiler.Compile("candidate-b8", [organization, authorization]);
        var second = compiler.Compile(
            "candidate-b8",
            [
                authorization with
                {
                    Routes = authorization.Routes.Reverse().ToArray(),
                    FrontendBindings = authorization.FrontendBindings.Reverse().ToArray(),
                    PermissionCodes = authorization.PermissionCodes.Reverse().ToArray(),
                    ModelKeys = authorization.ModelKeys.Reverse().ToArray()
                },
                organization with
                {
                    Dependencies = organization.Dependencies.Reverse().ToArray(),
                    Routes = organization.Routes.Reverse().ToArray(),
                    FrontendBindings = organization.FrontendBindings.Reverse().ToArray(),
                    PermissionCodes = organization.PermissionCodes.Reverse().ToArray(),
                    ModelKeys = organization.ModelKeys.Reverse().ToArray()
                }
            ]);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotNull(first.Plan);
        Assert.NotNull(second.Plan);
        Assert.Equal(first.Plan.CanonicalBytes, second.Plan.CanonicalBytes);
        Assert.Equal(first.Plan.PlanHash, second.Plan.PlanHash);
        Assert.Equal(first.Plan.SnapshotId, second.Plan.SnapshotId);
        Assert.Equal(first.Diagnostics, second.Diagnostics);
    }

    [Fact]
    public void Compile_ModuleIdsDifferOnlyByCase_RejectsWholeCandidate()
    {
        var upper = CreateCapsule(
            moduleId: "Organization",
            dependencies: [],
            routes: ["/organization/persons"],
            frontendBindings: ["organization.person.list"],
            permissions: ["Organization.Person.View"],
            models: ["Organization.Person"]);
        var lower = CreateCapsule(
            moduleId: "organization",
            dependencies: [],
            routes: ["/duplicate"],
            frontendBindings: ["organization.duplicate.page"],
            permissions: ["organization.Duplicate.View"],
            models: ["organization.Duplicate"]);

        var result = new CanonicalModulePlanCompiler().Compile("candidate-collision", [lower, upper]);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MODULE_IDENTITY_MISMATCH", diagnostic.Code);
        Assert.Equal("Organization", diagnostic.ConflictingOwner);
        Assert.Equal("organization", diagnostic.CanonicalKey);
    }

    [Fact]
    public void Compile_DependencyIsMissing_RejectsWholeCandidate()
    {
        var organization = CreateCapsule(
            moduleId: "Organization",
            dependencies: ["Authorization"],
            routes: ["/organization/persons"],
            frontendBindings: ["organization.person.list"],
            permissions: ["Organization.Person.View"],
            models: ["Organization.Person"]);

        var result = new CanonicalModulePlanCompiler().Compile("candidate-missing-dependency", [organization]);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MODULE_DEPENDENCY_MISSING", diagnostic.Code);
        Assert.Equal("Authorization", diagnostic.CanonicalKey);
        Assert.Equal("Organization", diagnostic.ModuleId);
    }

    [Fact]
    public void Compile_DependencyCycle_RejectsWholeCandidate()
    {
        var organization = CreateCapsule(
            moduleId: "Organization",
            dependencies: ["Authorization"],
            routes: ["/organization/persons"],
            frontendBindings: ["organization.person.list"],
            permissions: ["Organization.Person.View"],
            models: ["Organization.Person"]);
        var authorization = CreateCapsule(
            moduleId: "Authorization",
            dependencies: ["Organization"],
            routes: ["/authorization/roles"],
            frontendBindings: ["authorization.roles.page"],
            permissions: ["Authorization.Role.View"],
            models: ["Authorization.Role"]);

        var result = new CanonicalModulePlanCompiler().Compile(
            "candidate-cycle",
            [organization, authorization]);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MODULE_DEPENDENCY_CYCLE", diagnostic.Code);
        Assert.Equal("Authorization -> Organization -> Authorization", diagnostic.CanonicalKey);
    }

    [Fact]
    public void Compile_FrontendBehaviorReferenceHasNoBinding_RejectsWholeCandidate()
    {
        var organization = CreateCapsule(
            moduleId: "Organization",
            dependencies: [],
            routes: ["/organization/tree"],
            frontendBindings: ["organization.tree.page"],
            permissions: ["Organization.Person.View"],
            models: ["Organization.Person"])
            with
            {
                FrontendBindingReferences = ["organization.tree.page", "organization.missing.page"]
            };

        var result = new CanonicalModulePlanCompiler().Compile("candidate-missing-binding", [organization]);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MODULE_BINDING_MISSING", diagnostic.Code);
        Assert.Equal("organization.missing.page", diagnostic.CanonicalKey);
    }

    [Fact]
    public void Compile_PublicFrontendBindingHasNoAuthoringOwner_RejectsWholeCandidate()
    {
        var organization = CreateCapsule(
            moduleId: "Organization",
            dependencies: [],
            routes: ["/organization/tree"],
            frontendBindings: ["organization.tree.page", "organization.orphan.page"],
            permissions: ["Organization.Person.View"],
            models: ["Organization.Person"])
            with
            {
                FrontendBindingReferences = ["organization.tree.page"]
            };

        var result = new CanonicalModulePlanCompiler().Compile("candidate-orphan-binding", [organization]);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MODULE_BINDING_ORPHAN", diagnostic.Code);
        Assert.Equal("organization.orphan.page", diagnostic.CanonicalKey);
    }

    [Fact]
    public void Compile_PermissionReferenceIsNotDeclaredByCandidate_RejectsWholeCandidate()
    {
        var organization = CreateCapsule(
            moduleId: "Organization",
            dependencies: [],
            routes: ["/organization/persons"],
            frontendBindings: ["organization.person.list"],
            permissions: ["Organization.Person.View"],
            models: ["Organization.Person"])
            with
            {
                PermissionReferences = ["Organization.Person.View", "Organization.Person.Missing"]
            };

        var result = new CanonicalModulePlanCompiler().Compile("candidate-invalid-permission", [organization]);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MODULE_PERMISSION_REFERENCE_INVALID", diagnostic.Code);
        Assert.Equal("Organization.Person.Missing", diagnostic.CanonicalKey);
    }

    [Fact]
    public void Compile_NormalizedRoutesHaveDifferentOwners_RejectsWholeCandidate()
    {
        var organization = CreateCapsule(
            moduleId: "Organization",
            dependencies: [],
            routes: ["/organization/persons/"],
            frontendBindings: ["organization.person.list"],
            permissions: ["Organization.Person.View"],
            models: ["Organization.Person"]);
        var partner = CreateCapsule(
            moduleId: "Partner",
            dependencies: [],
            routes: ["/ORGANIZATION/PERSONS"],
            frontendBindings: ["partner.asset.list"],
            permissions: ["Partner.Asset.View"],
            models: ["Partner.Asset"]);

        var result = new CanonicalModulePlanCompiler().Compile("candidate-route-conflict", [partner, organization]);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MODULE_ROUTE_CONFLICT", diagnostic.Code);
        Assert.Equal("/organization/persons", diagnostic.CanonicalKey);
        Assert.Equal("Organization", diagnostic.ConflictingOwner);
        Assert.Equal("Partner", diagnostic.ModuleId);
    }

    [Fact]
    public void Compile_ModelKeyHasMultipleOwners_RejectsWholeCandidate()
    {
        var organization = CreateCapsule(
            moduleId: "Organization",
            dependencies: [],
            routes: ["/organization/persons"],
            frontendBindings: ["organization.person.list"],
            permissions: ["Organization.Person.View"],
            models: ["shared.Person"]);
        var partner = CreateCapsule(
            moduleId: "Partner",
            dependencies: [],
            routes: ["/partner/assets"],
            frontendBindings: ["partner.asset.list"],
            permissions: ["Partner.Asset.View"],
            models: ["shared.Person"]);

        var result = new CanonicalModulePlanCompiler().Compile("candidate-model-conflict", [partner, organization]);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Plan);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("MODULE_MODEL_OWNER_CONFLICT", diagnostic.Code);
        Assert.Equal("shared.Person", diagnostic.CanonicalKey);
        Assert.Equal("Organization", diagnostic.ConflictingOwner);
        Assert.Equal("Partner", diagnostic.ModuleId);
    }

    private static ModuleCapsuleCandidate CreateCapsule(
        string moduleId,
        IReadOnlyList<string> dependencies,
        IReadOnlyList<string> routes,
        IReadOnlyList<string> frontendBindings,
        IReadOnlyList<string> permissions,
        IReadOnlyList<string> models)
    {
        return new ModuleCapsuleCandidate(
            ModuleId: moduleId,
            Version: "1.0.0",
            DisplayName: moduleId,
            Description: $"{moduleId} module",
            PackageRoot: $"packages/{moduleId}/1.0.0",
            BackendEntry: $"{moduleId}Module.dll",
            BackendBehaviorKey: $"{moduleId.ToLowerInvariant()}.backend",
            FrontendEntry: moduleId.ToLowerInvariant(),
            FrontendBehaviorKey: $"{moduleId.ToLowerInvariant()}.frontend",
            FrontendBindingMode: "behavior-bindings",
            Dependencies: dependencies,
            Routes: routes,
            FrontendBindings: frontendBindings,
            FrontendBindingReferences: frontendBindings,
            PermissionCodes: permissions,
            PermissionReferences: permissions,
            ModelKeys: models,
            Provenance: new ModulePlanProvenance(
                SourceKind: "fixture",
                SourcePath: $"fixtures/{moduleId}/newp.plugin.json",
                JsonPointer: "/"));
    }
}
