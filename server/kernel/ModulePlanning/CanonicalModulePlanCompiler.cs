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
    /// 平台组合角色由发布物指定，不从组件名称推断。
    /// </summary>
    public ModulePlanCompilationResult Compile(
        string candidateId,
        IEnumerable<ModuleCapsuleCandidate> candidates,
        string? platformCompositionModuleId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateId);
        ArgumentNullException.ThrowIfNull(candidates);

        var candidateArray = candidates
            .OrderBy(static candidate => candidate.ModuleId, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Version, StringComparer.Ordinal)
            .ToArray();
        var graphDiagnostics = ValidateIdentity(candidateId, candidateArray)
            .Concat(ValidateComposition(candidateId, candidateArray)).ToArray();
        var activeIds = graphDiagnostics.Length == 0
            ? ResolveEffectiveModuleIds(candidateArray)
            : candidateArray.Where(candidate => candidate.Enabled).Select(candidate => candidate.ModuleId).ToHashSet(StringComparer.Ordinal);
        var activeCandidates = candidateArray.Where(candidate => activeIds.Contains(candidate.ModuleId)).ToArray();
        var required = graphDiagnostics.Length == 0
            ? CompileRequiredModules(candidateId, candidateArray, platformCompositionModuleId)
            : (ModuleIds: (IReadOnlyList<string>)[], Diagnostics: (IReadOnlyList<ModulePlanDiagnostic>)[]);
        var dependencyDiagnostics = ValidateDependencies(candidateId, activeCandidates);
        var diagnostics = graphDiagnostics
            .Concat(required.Diagnostics)
            .Concat(dependencyDiagnostics)
            .Concat(ValidateBindings(candidateId, activeCandidates))
            .Concat(ValidateRouteOwners(candidateId, activeCandidates))
            .Concat(ValidateModelOwners(candidateId, activeCandidates)).Concat(ValidateFeatures(candidateId, activeCandidates))
            .Concat(ValidateDataActions(candidateId, activeCandidates, dependencyDiagnostics.Count == 0))
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

        var selectedModules = candidateArray
            .Select(Normalize)
            .ToArray();
        var modules = selectedModules.Where(module => activeIds.Contains(module.ModuleId)).ToArray();
        var compositionGraph = BuildCompositionGraph(selectedModules);

        var canonicalDocument = new
        {
            schemaVersion = "1.0",
            candidateId,
            compilerContractVersion = ContractVersion,
            modules,
            selectedModules,
            compositionGraph,
            platformCompositionModuleId,
            requiredModuleIds = required.ModuleIds,
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
                modules)
            {
                SelectedModules = selectedModules, CompositionGraph = compositionGraph,
                PlatformCompositionModuleId = platformCompositionModuleId, RequiredModuleIds = required.ModuleIds
            },
            Array.Empty<ModulePlanDiagnostic>());
    }

    // 在完整无环选定图上派生基础集合，直接拒绝停用及外部祖先屏蔽，不改写自身选择。
    private static (IReadOnlyList<string> ModuleIds, IReadOnlyList<ModulePlanDiagnostic> Diagnostics) CompileRequiredModules(
        string candidateId, IReadOnlyList<ModuleCapsuleCandidate> candidates, string? platformCompositionModuleId)
    {
        if (platformCompositionModuleId is null) return ([], []);
        var byId = candidates.ToDictionary(candidate => candidate.ModuleId, StringComparer.Ordinal);
        if (!byId.TryGetValue(platformCompositionModuleId, out var root))
            return ([], [new(candidateId, platformCompositionModuleId, "", platformCompositionModuleId,
                "module-composition.json", "/platformCompositionModuleId", "platform-composition", "MODULE_PLATFORM_COMPOSITION_MISSING",
                $"The distribution platform composition '{platformCompositionModuleId}' is missing.")]);
        if (root.CompositionMembers.Count == 0)
            return ([], [new(candidateId, root.ModuleId, root.Version, root.ModuleId,
                root.Provenance.SourcePath, "/composition/members", "platform-composition", "MODULE_PLATFORM_COMPOSITION_INVALID",
                "The distribution platform composition must declare members.")]);

        var required = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(root.ModuleId);
        while (pending.TryPop(out var id))
        {
            if (!required.Add(id)) continue;
            foreach (var member in byId[id].CompositionMembers) pending.Push(member);
        }
        var diagnostics = required.Where(id => !byId[id].Enabled).Select(id =>
            new ModulePlanDiagnostic(candidateId, id, byId[id].Version, id, byId[id].Provenance.SourcePath,
                "/enabled", "platform-composition", "MODULE_REQUIRED_DISABLED", $"Required component '{id}' cannot be disabled.")).ToList();
        var parents = candidates.SelectMany(candidate => candidate.CompositionMembers.Select(member => (member, candidate.ModuleId)))
            .ToDictionary(edge => edge.member, edge => edge.ModuleId, StringComparer.Ordinal);
        var ancestor = parents.GetValueOrDefault(root.ModuleId);
        while (ancestor is not null)
        {
            if (!byId[ancestor].Enabled)
                diagnostics.Add(new(candidateId, root.ModuleId, root.Version, ancestor, byId[ancestor].Provenance.SourcePath,
                    "/enabled", "platform-composition", "MODULE_REQUIRED_ANCESTOR_DISABLED",
                    $"Required composition '{root.ModuleId}' cannot be masked by disabled ancestor '{ancestor}'."));
            ancestor = parents.GetValueOrDefault(ancestor);
        }
        return (required.Order(StringComparer.Ordinal).ToArray(), diagnostics);
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

    // 静态组合声明先在完整选定集合上校验，停用不豁免成员声明。
    private static IReadOnlyList<ModulePlanDiagnostic> ValidateComposition(string candidateId, IReadOnlyList<ModuleCapsuleCandidate> candidates)
    {
        var diagnostics = candidates.SelectMany(candidate => candidate.CompositionMembers
            .GroupBy(member => member, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => new ModulePlanDiagnostic(candidateId, candidate.ModuleId, candidate.Version, group.Key,
                candidate.Provenance.SourcePath, "/composition/members", "composition-graph", "MODULE_COMPOSITION_MEMBER_DUPLICATE",
                $"Component '{candidate.ModuleId}' declares member '{group.Key}' more than once."))).ToList();
        var ids = candidates.Select(candidate => candidate.ModuleId).ToHashSet(StringComparer.Ordinal);
        var parents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            foreach (var member in candidate.CompositionMembers.Distinct(StringComparer.Ordinal))
            {
                var self = string.Equals(candidate.ModuleId, member, StringComparison.OrdinalIgnoreCase);
                if (self || !ids.Contains(member))
                    diagnostics.Add(new(candidateId, candidate.ModuleId, candidate.Version, member,
                        candidate.Provenance.SourcePath, "/composition/members", "composition-graph",
                        self ? "MODULE_COMPOSITION_SELF_MEMBER" : "MODULE_COMPOSITION_MEMBER_MISSING",
                        self ? $"Component '{candidate.ModuleId}' cannot contain itself." : $"Component '{candidate.ModuleId}' references missing member '{member}'."));
                else if (!parents.TryAdd(member, candidate.ModuleId) && parents[member] != candidate.ModuleId)
                    diagnostics.Add(new(candidateId, candidate.ModuleId, candidate.Version, member,
                        candidate.Provenance.SourcePath, "/composition/members", "composition-graph", "MODULE_COMPOSITION_MULTIPLE_PARENTS",
                        $"Member '{member}' belongs to both '{parents[member]}' and '{candidate.ModuleId}'.", parents[member]));
            }
        }
        if (diagnostics.Count > 0) return diagnostics;

        // 单父图可沿父链迭代访问，每个节点至多完成一次，深层组合不使用递归调用栈。
        var byId = candidates.GroupBy(candidate => candidate.ModuleId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var completed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids.Order(StringComparer.Ordinal))
        {
            var path = new List<string>();
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            string? current = id;
            while (current is not null && !completed.Contains(current))
            {
                if (positions.TryGetValue(current, out var cycleStart))
                {
                    var cycle = CanonicalizeCycle(path.Skip(cycleStart).Reverse().ToArray());
                    var owner = byId[cycle.Split(" -> ", StringSplitOptions.None)[0]];
                    diagnostics.Add(new(candidateId, owner.ModuleId, owner.Version, cycle,
                        owner.Provenance.SourcePath, "/composition/members", "composition-graph", "MODULE_COMPOSITION_CYCLE",
                        $"Component composition cycle detected: {cycle}."));
                    break;
                }
                positions[current] = path.Count;
                path.Add(current);
                current = parents.GetValueOrDefault(current);
            }
            completed.UnionWith(path);
        }
        return diagnostics;
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

    // 技术名称跨 owner 唯一；大小写折叠仅用于冲突判定，计划保留声明名称。
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
            .GroupBy(static item => item.ModelKey, StringComparer.OrdinalIgnoreCase)
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
        if (diagnostics.Count == 0)
        {
            try { FeatureCatalogCompiler.Compile(candidates.SelectMany(candidate => candidate.Features)); }
            catch (InvalidOperationException exception)
            {
                diagnostics.Add(new(candidateId, "*", "", "features", "", "/features", "features", "MODULE_FEATURE_INVALID", exception.Message));
            }
        }
        return diagnostics;
    }

    // 动作身份跨模块唯一，候选冲突必须作为编译诊断拒绝，不能延迟到发布后。
    private static IReadOnlyList<ModulePlanDiagnostic> ValidateDataActions(string candidateId, IReadOnlyList<ModuleCapsuleCandidate> candidates, bool dependenciesAvailable)
    {
        try
        {
            var actions = DataActionCatalogCompiler.Compile(candidates.SelectMany(candidate => candidate.DataActions));
            // 缺失依赖已拒绝候选；不把其资源消失再报告成按钮声明错误。
            if (dependenciesAvailable) ActionDataAccessCompiler.Compile(candidates.SelectMany(candidate => candidate.Features).ToArray(), actions);
            return [];
        }
        catch (InvalidOperationException exception)
        { return [new(candidateId, "*", "", "dataActions", "", "/dataActions", "data-actions", "MODULE_DATA_ACTION_INVALID", exception.Message)]; }
    }

    // 从已经完整校验的单父图计算活动资格，不改写各组件自己的启用选择。
    private static HashSet<string> ResolveEffectiveModuleIds(IReadOnlyList<ModuleCapsuleCandidate> candidates)
    {
        var byId = candidates.ToDictionary(candidate => candidate.ModuleId, StringComparer.Ordinal);
        var parents = candidates.SelectMany(candidate => candidate.CompositionMembers.Select(member => (member, candidate.ModuleId)))
            .ToDictionary(edge => edge.member, edge => edge.ModuleId, StringComparer.Ordinal);
        var enabled = new Dictionary<string, bool>(StringComparer.Ordinal);

        // 图已通过单父、无环校验；缓存祖先结果，保留声明中的成员自身选择。
        bool IsEnabled(string id)
        {
            if (enabled.TryGetValue(id, out var result)) return result;
            return enabled[id] = byId[id].Enabled && (!parents.TryGetValue(id, out var parent) || IsEnabled(parent));
        }

        return byId.Keys.Where(IsEnabled).ToHashSet(StringComparer.Ordinal);
    }

    // 组合投影只记录选定的关系，不从能力依赖推断父子或改变模型归属。
    private static IReadOnlyList<ModuleCompositionNode> BuildCompositionGraph(IReadOnlyList<CanonicalModulePlanModule> modules)
    {
        var parents = modules.SelectMany(module => module.CompositionMembers.Select(member => (member, module.ModuleId)))
            .ToDictionary(edge => edge.member, edge => edge.ModuleId, StringComparer.Ordinal);
        return modules.Select(module => new ModuleCompositionNode(module.ModuleId, module.Version, module.Enabled,
            parents.GetValueOrDefault(module.ModuleId), module.CompositionMembers)).ToArray();
    }

    // 规范化当前声明，将组合、启用选择与前端交付方式绑定到同一可信来源指纹。
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
            NormalizePersistence(candidate.Persistence))
        {
            Features = candidate.Features.OrderBy(node => node.Key, StringComparer.Ordinal).ToArray(),
            DataActions = candidate.DataActions.ToArray(),
            CompositionMembers = Sort(candidate.CompositionMembers),
            Enabled = candidate.Enabled,
            FrontendDelivery = candidate.FrontendDelivery
        };
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
