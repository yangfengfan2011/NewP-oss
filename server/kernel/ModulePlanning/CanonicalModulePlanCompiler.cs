using System.Security.Cryptography;
using System.Text.Json;

namespace ICP.Platform.Kernel.ModulePlanning;

/// <summary>
/// 把模块胶囊事实编译为稳定、全有或全无的 Canonical Module Plan。
/// </summary>
public sealed class CanonicalModulePlanCompiler
{
    public const string ContractVersion = "1.0";

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// 编译候选模块集合；输入枚举顺序不会影响计划 bytes 或 hash。
    /// </summary>
    public ModulePlanCompilationResult Compile(
        string candidateId,
        IEnumerable<ModuleCapsuleCandidate> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentNullException.ThrowIfNull(candidates);

        var candidateArray = candidates
            .OrderBy(static candidate => candidate.ModuleId, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Version, StringComparer.Ordinal)
            .ToArray();
        var diagnostics = ValidateIdentity(candidateId, candidateArray)
            .Concat(ValidateDependencies(candidateId, candidateArray))
            .Concat(ValidateBindings(candidateId, candidateArray))
            .Concat(ValidateRouteOwners(candidateId, candidateArray))
            .Concat(ValidateModelOwners(candidateId, candidateArray)).Concat(ValidateFeatures(candidateId, candidateArray))
            .Concat(ValidateDataActions(candidateId, candidateArray))
            .OrderBy(static diagnostic => diagnostic.Stage, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.ModuleId, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.CanonicalKey, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.SourcePath, StringComparer.Ordinal)
            .ToArray();
        if (diagnostics.Length > 0)
        {
            return new ModulePlanCompilationResult(null, diagnostics);
        }

        var modules = candidateArray
            .Select(Normalize)
            .ToArray();

        var canonicalDocument = new
        {
            schemaVersion = "1.0",
            candidateId,
            compilerContractVersion = ContractVersion,
            modules,
            verifiedArtifactDigests = modules
                .Select(module => new
                {
                    module.ModuleId,
                    digest = module.Provenance.GetAuthenticatedBuiltInArtifactDigest(module)
                })
                .Where(static artifact => artifact.digest is not null)
                .OrderBy(static artifact => artifact.ModuleId, StringComparer.Ordinal)
                .ToArray()
        };
        var canonicalBytes = JsonSerializer.SerializeToUtf8Bytes(canonicalDocument, CanonicalJsonOptions);
        var planHash = Convert.ToHexStringLower(SHA256.HashData(canonicalBytes));

        return new ModulePlanCompilationResult(
            new CanonicalModulePlan(
                candidateId,
                ContractVersion,
                planHash,
                planHash,
                canonicalBytes,
                modules),
            Array.Empty<ModulePlanDiagnostic>());
    }

    private static IReadOnlyList<ModulePlanDiagnostic> ValidateIdentity(
        string candidateId,
        IReadOnlyList<ModuleCapsuleCandidate> candidates)
    {
        return candidates
            .GroupBy(static candidate => candidate.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var owners = group.OrderBy(static candidate => candidate.ModuleId, StringComparer.Ordinal).ToArray();
                var owner = owners[0];
                return owners.Skip(1).Select(candidate => new ModulePlanDiagnostic(
                    candidateId,
                    candidate.ModuleId,
                    candidate.Version,
                    candidate.ModuleId.ToLowerInvariant(),
                    candidate.Provenance.SourcePath,
                    candidate.Provenance.JsonPointer,
                    "canonical-identity",
                    "MODULE_IDENTITY_MISMATCH",
                    $"Module id '{candidate.ModuleId}' collides with '{owner.ModuleId}' after case folding.",
                    owner.ModuleId));
            })
            .ToArray();
    }

    private static IReadOnlyList<ModulePlanDiagnostic> ValidateDependencies(
        string candidateId,
        IReadOnlyList<ModuleCapsuleCandidate> candidates)
    {
        var candidatesById = candidates
            .GroupBy(static candidate => candidate.ModuleId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var diagnostics = candidates
            .SelectMany(candidate => candidate.Dependencies
                .Where(dependency => !candidatesById.ContainsKey(dependency))
                .Distinct(StringComparer.Ordinal)
                .Select(dependency => new ModulePlanDiagnostic(
                    candidateId,
                    candidate.ModuleId,
                    candidate.Version,
                    dependency,
                    candidate.Provenance.SourcePath,
                    candidate.Provenance.JsonPointer,
                    "dependency-dag",
                    "MODULE_DEPENDENCY_MISSING",
                    $"Module '{candidate.ModuleId}' depends on missing module '{dependency}'.")))
            .ToList();

        var visitState = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new List<string>();
        var cycles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var moduleId in candidatesById.Keys.OrderBy(static value => value, StringComparer.Ordinal))
        {
            Visit(moduleId);
        }

        foreach (var cycle in cycles.OrderBy(static value => value, StringComparer.Ordinal))
        {
            var ownerId = cycle.Split(" -> ", StringSplitOptions.None)[0];
            var owner = candidatesById[ownerId];
            diagnostics.Add(new ModulePlanDiagnostic(
                candidateId,
                owner.ModuleId,
                owner.Version,
                cycle,
                owner.Provenance.SourcePath,
                owner.Provenance.JsonPointer,
                "dependency-dag",
                "MODULE_DEPENDENCY_CYCLE",
                $"Module dependency cycle detected: {cycle}."));
        }

        return diagnostics;

        void Visit(string moduleId)
        {
            if (visitState.TryGetValue(moduleId, out var state) && state == 2)
            {
                return;
            }

            if (state == 1)
            {
                var cycleStart = stack.IndexOf(moduleId);
                if (cycleStart >= 0)
                {
                    cycles.Add(CanonicalizeCycle(stack.Skip(cycleStart).ToArray()));
                }
                return;
            }

            visitState[moduleId] = 1;
            stack.Add(moduleId);

            foreach (var dependency in candidatesById[moduleId].Dependencies
                         .Where(candidatesById.ContainsKey)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(static value => value, StringComparer.Ordinal))
            {
                Visit(dependency);
            }

            stack.RemoveAt(stack.Count - 1);
            visitState[moduleId] = 2;
        }
    }

    private static string CanonicalizeCycle(IReadOnlyList<string> cycle)
    {
        var startIndex = Enumerable.Range(0, cycle.Count)
            .OrderBy(index => cycle[index], StringComparer.Ordinal)
            .First();
        var ordered = Enumerable.Range(0, cycle.Count)
            .Select(offset => cycle[(startIndex + offset) % cycle.Count])
            .ToList();
        ordered.Add(ordered[0]);
        return string.Join(" -> ", ordered);
    }

    private static IReadOnlyList<ModulePlanDiagnostic> ValidateBindings(
        string candidateId,
        IReadOnlyList<ModuleCapsuleCandidate> candidates)
    {
        return candidates
            .SelectMany(candidate =>
            {
                var bindings = candidate.FrontendBindings.ToHashSet(StringComparer.Ordinal);
                var references = candidate.FrontendBindingReferences.ToHashSet(StringComparer.Ordinal);
                var missing = references
                    .Where(reference => !bindings.Contains(reference))
                    .Select(reference => new ModulePlanDiagnostic(
                        candidateId,
                        candidate.ModuleId,
                        candidate.Version,
                        reference,
                        candidate.Provenance.SourcePath,
                        candidate.Provenance.JsonPointer,
                        "behavior-bindings",
                        "MODULE_BINDING_MISSING",
                        $"Frontend behavior reference '{reference}' has no binding in module '{candidate.ModuleId}'."));
                var orphaned = bindings
                    .Where(binding => !references.Contains(binding))
                    .Select(binding => new ModulePlanDiagnostic(
                        candidateId,
                        candidate.ModuleId,
                        candidate.Version,
                        binding,
                        candidate.Provenance.SourcePath,
                        candidate.Provenance.JsonPointer,
                        "behavior-bindings",
                        "MODULE_BINDING_ORPHAN",
                        $"Frontend behavior binding '{binding}' has no authoring owner in module '{candidate.ModuleId}'."));
                return missing.Concat(orphaned);
            })
            .ToArray();
    }

    private static IReadOnlyList<ModulePlanDiagnostic> ValidateRouteOwners(
        string candidateId,
        IReadOnlyList<ModuleCapsuleCandidate> candidates)
    {
        return candidates
            .SelectMany(candidate => candidate.Routes.Select(route => new
            {
                Candidate = candidate,
                CanonicalRoute = NormalizeRoute(route)
            }))
            .GroupBy(static item => item.CanonicalRoute, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .SelectMany(group =>
            {
                var owners = group
                    .OrderBy(static item => item.Candidate.ModuleId, StringComparer.Ordinal)
                    .ThenBy(static item => item.Candidate.Provenance.SourcePath, StringComparer.Ordinal)
                    .ToArray();
                var owner = owners[0].Candidate;
                return owners.Skip(1).Select(item => new ModulePlanDiagnostic(
                    candidateId,
                    item.Candidate.ModuleId,
                    item.Candidate.Version,
                    group.Key,
                    item.Candidate.Provenance.SourcePath,
                    item.Candidate.Provenance.JsonPointer,
                    "global-conflicts",
                    "MODULE_ROUTE_CONFLICT",
                    $"Route '{group.Key}' has multiple owners.",
                    owner.ModuleId));
            })
            .ToArray();
    }

    private static string NormalizeRoute(string route)
    {
        var normalized = route.Trim().Replace('\\', '/');
        if (normalized.Length > 1)
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized.ToLowerInvariant();
    }

    private static IReadOnlyList<ModulePlanDiagnostic> ValidateModelOwners(
        string candidateId,
        IReadOnlyList<ModuleCapsuleCandidate> candidates)
    {
        return candidates
            .SelectMany(candidate => candidate.ModelKeys.Select(modelKey => new
            {
                Candidate = candidate,
                ModelKey = modelKey
            }))
            .GroupBy(static item => item.ModelKey, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .SelectMany(group =>
            {
                var owners = group
                    .OrderBy(static item => item.Candidate.ModuleId, StringComparer.Ordinal)
                    .ThenBy(static item => item.Candidate.Provenance.SourcePath, StringComparer.Ordinal)
                    .ToArray();
                var owner = owners[0].Candidate;
                return owners.Skip(1).Select(item => new ModulePlanDiagnostic(
                    candidateId,
                    item.Candidate.ModuleId,
                    item.Candidate.Version,
                    group.Key,
                    item.Candidate.Provenance.SourcePath,
                    item.Candidate.Provenance.JsonPointer,
                    "global-conflicts",
                    "MODULE_MODEL_OWNER_CONFLICT",
                    $"Model key '{group.Key}' has multiple owners.",
                    owner.ModuleId));
            })
            .ToArray();
    }

    // 功能结构与模块归属在 canonical candidate 阶段失败关闭，不能留到首次页面访问。
    private static IReadOnlyList<ModulePlanDiagnostic> ValidateFeatures(string candidateId, IEnumerable<ModuleCapsuleCandidate> candidates)
    {
        var diagnostics = new List<ModulePlanDiagnostic>();
        foreach (var candidate in candidates)
        {
            try
            {
                if (candidate.Features.Any(node => node.ModuleId != candidate.ModuleId))
                    throw new InvalidOperationException("功能声明不能跨模块冒领归属。");
                FeatureCatalogCompiler.Compile(candidate.Features);
                if (candidate.DataActions.Any(action => action.ModuleId != candidate.ModuleId))
                    throw new InvalidOperationException("数据动作声明不能跨模块冒领归属。");
                DataActionCatalogCompiler.Compile(candidate.DataActions);
            }
            catch (InvalidOperationException exception)
            {
                diagnostics.Add(new(candidateId, candidate.ModuleId, candidate.Version, "features",
                    candidate.Provenance.SourcePath, "/features", "features", "MODULE_FEATURE_INVALID", exception.Message));
            }
        }
        return diagnostics;
    }

    // 动作身份跨模块唯一，候选冲突必须作为编译诊断拒绝，不能延迟到发布后。
    private static IReadOnlyList<ModulePlanDiagnostic> ValidateDataActions(string candidateId, IReadOnlyList<ModuleCapsuleCandidate> candidates)
    {
        try { DataActionCatalogCompiler.Compile(candidates.SelectMany(candidate => candidate.DataActions)); return []; }
        catch (InvalidOperationException exception)
        { return [new(candidateId, "*", "", "dataActions", "", "/dataActions", "data-actions", "MODULE_DATA_ACTION_INVALID", exception.Message)]; }
    }

    // 保留动作声明与开关的规范排序，将其纳入候选内容身份。
    private static CanonicalModulePlanModule Normalize(ModuleCapsuleCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var provenance = candidate.Provenance.BindToCanonicalCandidate(candidate);

        return new CanonicalModulePlanModule(
            candidate.ModuleId,
            candidate.Version,
            candidate.DisplayName,
            candidate.Description,
            candidate.PackageRoot,
            candidate.BackendEntry,
            candidate.BackendBehaviorKey,
            candidate.FrontendEntry,
            candidate.FrontendBehaviorKey,
            candidate.FrontendBindingMode,
            Sort(candidate.Dependencies),
            Sort(candidate.Routes),
            Sort(candidate.FrontendBindings),
            Sort(candidate.FrontendBindingReferences),
            Sort(candidate.ModelKeys),
            provenance,
            NormalizePersistence(candidate.Persistence)) { Features = candidate.Features.OrderBy(node => node.Key, StringComparer.Ordinal).ToArray(), DataActions = candidate.DataActions.ToArray() };
    }

    private static ModulePersistenceContribution? NormalizePersistence(ModulePersistenceContribution? persistence)
    {
        if (persistence is null)
        {
            return null;
        }

        return new ModulePersistenceContribution(
            Sort(persistence.ModelPaths),
            persistence.MigrationBundles
                .OrderBy(static bundle => bundle.Provider, StringComparer.Ordinal)
                .ThenBy(static bundle => bundle.AssemblyPath, StringComparer.Ordinal)
                .ThenBy(static bundle => bundle.ManifestPath, StringComparer.Ordinal)
                .ToArray());
    }

    private static IReadOnlyList<string> Sort(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
    }
}
