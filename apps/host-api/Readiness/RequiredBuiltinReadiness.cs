using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.Planning;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.HostApi.Readiness;

public static class RequiredBuiltinReadinessFailureCodes
{
    public const string ModuleMissing = "REQUIRED_BUILTIN_MISSING";
    public const string ModuleDuplicate = "REQUIRED_BUILTIN_DUPLICATE";
    public const string ModuleInvalidSource = "REQUIRED_BUILTIN_SOURCE_INVALID";
    public const string ModelMissing = "REQUIRED_BUILTIN_MODEL_MISSING";
    public const string MigrationMissing = "REQUIRED_BUILTIN_MIGRATION_MISSING";
}

/// <summary>
/// 对 Identity 与 Authentication 的静态装配和基础 schema 计划做必需 readiness 检查。
/// 空 Identity 的业务 readiness 由后续 bootstrap 票据接管。
/// </summary>
public sealed class RequiredBuiltinReadinessContributor : IReadinessContributor
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RequiredModels =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["Identity"] = new HashSet<string>(
                ["IdentityAccount", "LocalCredential", "IdentitySecurityEvent"],
                StringComparer.Ordinal),
            ["Authentication"] = new HashSet<string>(["AuthenticationSession"], StringComparer.Ordinal)
        };

    private readonly CanonicalModulePlan _modulePlan;
    private readonly CompiledPersistencePlan _persistencePlan;

    public RequiredBuiltinReadinessContributor(
        CanonicalModulePlan modulePlan,
        CompiledPersistencePlan persistencePlan)
    {
        _modulePlan = modulePlan;
        _persistencePlan = persistencePlan;
    }

    public string Name => "identity-foundation";

    public int Order => 90;

    public bool IsRequired => true;

    public ValueTask<ReadinessContribution> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var requiredModuleId in RequiredModels.Keys)
        {
            var matches = _modulePlan.Modules
                .Where(module => string.Equals(module.ModuleId, requiredModuleId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length == 0)
            {
                return Failed(RequiredBuiltinReadinessFailureCodes.ModuleMissing, $"Required built-in '{requiredModuleId}' is missing.");
            }

            if (matches.Length > 1)
            {
                return Failed(RequiredBuiltinReadinessFailureCodes.ModuleDuplicate, $"Required built-in '{requiredModuleId}' is duplicated.");
            }

            if (!matches[0].Provenance.AuthenticatesBuiltIn(matches[0]))
            {
                return Failed(RequiredBuiltinReadinessFailureCodes.ModuleInvalidSource, $"Required built-in '{requiredModuleId}' has an invalid source.");
            }

            foreach (var modelName in RequiredModels[requiredModuleId])
            {
                var modelCount = _persistencePlan.Models.Count(model =>
                    string.Equals(model.OwnerModuleId, requiredModuleId, StringComparison.Ordinal)
                    && string.Equals(model.Name, modelName, StringComparison.Ordinal));
                if (modelCount != 1)
                {
                    return Failed(
                        RequiredBuiltinReadinessFailureCodes.ModelMissing,
                        $"Required model '{requiredModuleId}/{modelName}' is unavailable.");
                }
            }
        }

        if (_persistencePlan.Provider != PersistenceProvider.SqlServer
            || !HasRequiredMigration("Identity", PlatformCoreMigrationFrontiers.IdentitySqlServerFoundation)
            || !HasRequiredMigration("Authentication", PlatformCoreMigrationFrontiers.AuthenticationSqlServerFoundation))
        {
            return Failed(
                RequiredBuiltinReadinessFailureCodes.MigrationMissing,
                "Required built-in migration plan is unavailable.");
        }

        return ValueTask.FromResult(ReadinessContribution.Ready("Identity foundation schema is available."));
    }

    private bool HasRequiredMigration(string moduleId, string expectedFrontier) =>
        _persistencePlan.Streams.Any(stream =>
            !stream.IsPlatformStream
            && string.Equals(stream.ModuleId, moduleId, StringComparison.Ordinal)
            && stream.Provider == PersistenceProvider.SqlServer
            && string.Equals(stream.ExpectedFrontier, expectedFrontier, StringComparison.Ordinal));

    private static ValueTask<ReadinessContribution> Failed(string code, string diagnostic) =>
        ValueTask.FromResult(new ReadinessContribution(ReadinessState.Failed, code, diagnostic));
}

public static class RequiredBuiltinReadinessServiceCollectionExtensions
{
    public static IServiceCollection AddRequiredBuiltinReadiness(
        this IServiceCollection services,
        CanonicalModulePlan modulePlan,
        CompiledPersistencePlan persistencePlan)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(modulePlan);
        ArgumentNullException.ThrowIfNull(persistencePlan);

        services.AddSingleton<IReadinessContributor>(
            new RequiredBuiltinReadinessContributor(modulePlan, persistencePlan));
        return services;
    }
}
