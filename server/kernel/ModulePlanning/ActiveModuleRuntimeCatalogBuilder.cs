using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>
/// 从 Canonical Module Plan 生成 Host/Shell 唯一活动目录。
/// </summary>
public sealed class ActiveModuleRuntimeCatalogBuilder
{
    private readonly string _publicBaseUrl;

    public ActiveModuleRuntimeCatalogBuilder(string publicBaseUrl = "/plugin-assets")
    {
        _publicBaseUrl = publicBaseUrl;
    }

    public string Build(CanonicalModulePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var permissionBytes = Encoding.UTF8.GetBytes(string.Join(
            "\n",
            plan.Modules
                .SelectMany(static module => module.PermissionCodes)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static code => code, StringComparer.Ordinal)));
        var permissionCatalogHash = Convert.ToHexStringLower(SHA256.HashData(permissionBytes));
        var catalog = new
        {
            schemaVersion = "1.0",
            snapshotId = plan.SnapshotId,
            compilerContractVersion = plan.CompilerContractVersion,
            planHash = plan.PlanHash,
            permissionCatalogHash,
            projections = new
            {
                frontend = "/plugins/frontend-plugin-manifest.json",
                metadata = "/plugins/metadata-runtime-manifest.json",
                localization = "/plugins/localization-manifest.json",
                projectionHash = plan.PlanHash
            },
            modules = plan.Modules.Select(module => new
            {
                moduleId = module.ModuleId,
                version = module.Version,
                displayName = module.DisplayName,
                description = module.Description,
                dependencies = module.Dependencies,
                backend = module.BackendEntry is null
                    ? null
                    : new
                    {
                        entryAssembly = module.BackendEntry,
                        behaviorKey = module.BackendBehaviorKey
                    },
                frontend = module.FrontendEntry is null
                    ? null
                    : new
                    {
                        moduleId = module.Provenance.SourceKind == "built-in" ? module.FrontendEntry : null,
                        url = module.Provenance.SourceKind == "built-in"
                            ? null
                            : CombinePublicPath(_publicBaseUrl, module.ModuleId, module.Version, module.FrontendEntry),
                        behaviorKey = module.FrontendBehaviorKey,
                        bindingMode = module.FrontendBindingMode,
                        bindings = module.FrontendBindings
                    },
                metadataBasePath = $"/plugins/runtime/modules/{module.ModuleId}/metadata",
                permissionCodes = module.PermissionCodes,
                modelKeys = module.ModelKeys
            }).ToArray()
        };

        return JsonSerializer.Serialize(catalog, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }) + Environment.NewLine;
    }

    private static string CombinePublicPath(
        string publicBaseUrl,
        string moduleId,
        string version,
        string entry)
    {
        return $"{publicBaseUrl.TrimEnd('/')}/{moduleId}/{version}/{entry.TrimStart('/')}";
    }
}
