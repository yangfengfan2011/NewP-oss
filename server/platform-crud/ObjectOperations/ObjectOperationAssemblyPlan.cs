using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Crud.ObjectOperations;

internal sealed class ObjectOperationAssemblyOptions
{
    internal List<ObjectBehaviorRegistration> Behaviors { get; } = [];
    internal List<ObjectOperationExtensionRegistration> Extensions { get; } = [];
}

internal sealed record ObjectBehaviorRegistration(
    string OwnerModuleId,
    ObjectBehaviorDefinition Definition);

internal sealed record ObjectOperationExtensionRegistration(
    string ProviderModuleId,
    IReadOnlyList<string> ProviderDependencies,
    ObjectOperationExtensionDefinition Definition);

internal readonly record struct ObjectOperationExtensionIdentity(
    string ObjectKey,
    ObjectOperationAction Action,
    string ExtensionKey);

internal readonly record struct ObjectOperationIdentity(
    string ObjectKey,
    ObjectOperationAction Action);

internal sealed record ObjectOperationExtensionPlan(
    string ExtensionKey,
    IReadOnlyList<ObjectOperationTarget> Calls);

internal sealed record ObjectOperationCoordinationPlan(
    IReadOnlyCollection<string> ModelKeys,
    IReadOnlyDictionary<ObjectOperationIdentity, IReadOnlySet<ObjectOperationIdentity>> CallsByOperation);

internal sealed record ObjectBehaviorPlan(
    ObjectBehaviorDefinition Definition,
    IReadOnlyDictionary<ObjectOperationAction, IReadOnlyList<ObjectOperationExtensionPlan>> Extensions,
    IReadOnlyDictionary<ObjectOperationAction, ObjectOperationCoordinationPlan> Coordinations);

internal interface IObjectBehaviorContribution
{
    string ObjectKey { get; }
    IObjectBehavior Behavior { get; }
}

internal interface IObjectOperationExtensionContribution
{
    ObjectOperationExtensionIdentity Identity { get; }
    IObjectOperationExtension Extension { get; }
}

internal sealed class ObjectBehaviorContribution : IObjectBehaviorContribution
{
    // 将一次模块行为声明与本作用域对应实例绑定，避免按实现类型猜测身份。
    internal ObjectBehaviorContribution(string objectKey, IObjectBehavior behavior)
    {
        ObjectKey = objectKey;
        Behavior = behavior;
    }

    public string ObjectKey { get; }
    public IObjectBehavior Behavior { get; }
}

internal sealed class ObjectOperationExtensionContribution : IObjectOperationExtensionContribution
{
    // 将扩展点内的显式 identity 与本作用域对应实例绑定，允许同一实现类型复用。
    internal ObjectOperationExtensionContribution(
        ObjectOperationExtensionIdentity identity,
        IObjectOperationExtension extension)
    {
        Identity = identity;
        Extension = extension;
    }

    public ObjectOperationExtensionIdentity Identity { get; }
    public IObjectOperationExtension Extension { get; }
}

internal sealed class ObjectOperationContributionSet
{
    // 解析一次作用域内的实际贡献，并用静态计划验证 identity 与行为声明完全一致。
    public ObjectOperationContributionSet(
        IEnumerable<IObjectBehaviorContribution> behaviors,
        IEnumerable<IObjectOperationExtensionContribution> extensions,
        ObjectOperationAssemblyPlan plan)
    {
        ArgumentNullException.ThrowIfNull(behaviors);
        ArgumentNullException.ThrowIfNull(extensions);
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        Behaviors = IndexBehaviors(behaviors);
        Extensions = IndexExtensions(extensions);
        ValidateResolvedContributions();
    }

    internal ObjectOperationAssemblyPlan Plan { get; }
    internal IReadOnlyDictionary<string, IObjectBehavior> Behaviors { get; }
    internal IReadOnlyDictionary<ObjectOperationExtensionIdentity, IObjectOperationExtension> Extensions { get; }

    // 行为按对象 identity 绑定；实现类型可由多个对象声明安全复用。
    private static IReadOnlyDictionary<string, IObjectBehavior> IndexBehaviors(
        IEnumerable<IObjectBehaviorContribution> contributions)
    {
        var result = new Dictionary<string, IObjectBehavior>(StringComparer.Ordinal);
        foreach (var contribution in contributions)
        {
            if (!result.TryAdd(contribution.ObjectKey, contribution.Behavior))
            {
                throw new InvalidOperationException(
                    $"OBJECT_OPERATION_BEHAVIOR_CONTRIBUTION_DUPLICATED: {contribution.ObjectKey}");
            }
        }
        return result;
    }

    // 扩展按目标动作和扩展键绑定；同一实现类型可贡献多个不同 identity。
    private static IReadOnlyDictionary<ObjectOperationExtensionIdentity, IObjectOperationExtension> IndexExtensions(
        IEnumerable<IObjectOperationExtensionContribution> contributions)
    {
        var result = new Dictionary<ObjectOperationExtensionIdentity, IObjectOperationExtension>();
        foreach (var contribution in contributions)
        {
            if (!result.TryAdd(contribution.Identity, contribution.Extension))
            {
                throw new InvalidOperationException(
                    $"OBJECT_OPERATION_EXTENSION_CONTRIBUTION_DUPLICATED: {contribution.Identity.ObjectKey}/{contribution.Identity.Action.Value}/{contribution.Identity.ExtensionKey}");
            }
        }
        return result;
    }

    // 实际贡献必须与启动计划精确对应，工厂不能替换声明或绕过组件装配。
    private void ValidateResolvedContributions()
    {
        if (!Behaviors.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(Plan.Behaviors.Keys))
        {
            throw new InvalidOperationException("OBJECT_OPERATION_BEHAVIOR_CONTRIBUTION_MISMATCH");
        }
        foreach (var behavior in Plan.Behaviors)
        {
            var resolved = Behaviors[behavior.Key].Definition;
            if (!DefinitionsMatch(resolved, behavior.Value.Definition))
            {
                throw new InvalidOperationException(
                    $"OBJECT_OPERATION_BEHAVIOR_DEFINITION_MISMATCH: {behavior.Key}");
            }
        }
        var plannedExtensions = Plan.Behaviors
            .SelectMany(behavior => behavior.Value.Extensions.SelectMany(extension =>
                extension.Value.Select(plan => new ObjectOperationExtensionIdentity(
                    behavior.Key,
                    extension.Key,
                    plan.ExtensionKey))))
            .ToHashSet();
        if (!Extensions.Keys.ToHashSet().SetEquals(plannedExtensions))
        {
            throw new InvalidOperationException("OBJECT_OPERATION_EXTENSION_CONTRIBUTION_MISMATCH");
        }
    }

    // 行为可持有声明副本，但 owner、协调范围、动作和保护字段必须与计划一致。
    private static bool DefinitionsMatch(ObjectBehaviorDefinition left, ObjectBehaviorDefinition right) =>
        string.Equals(left.ObjectKey, right.ObjectKey, StringComparison.Ordinal)
        && string.Equals(left.OwnerModuleId, right.OwnerModuleId, StringComparison.Ordinal)
        && string.Equals(left.ModelKey, right.ModelKey, StringComparison.Ordinal)
        && left.SupportedActions.ToHashSet().SetEquals(right.SupportedActions)
        && left.OpenExtensionActions.ToHashSet().SetEquals(right.OpenExtensionActions)
        && left.ProtectedInputFields.ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(right.ProtectedInputFields)
        && left.Calls.ToHashSet().SetEquals(right.Calls);
}

/// <summary>
/// 编译并冻结对象行为与扩展的启动期装配结果。
/// </summary>
internal sealed class ObjectOperationAssemblyPlan
{
    /// <summary>从模块注册声明编译唯一行为和稳定扩展拓扑。</summary>
    public ObjectOperationAssemblyPlan(IOptions<ObjectOperationAssemblyOptions> options)
        : this(options?.Value ?? throw new ArgumentNullException(nameof(options)))
    {
    }

    // 内部候选只在完整编译成功后发布为计划。
    private ObjectOperationAssemblyPlan(ObjectOperationAssemblyOptions options)
    {
        Behaviors = Compile(options);
    }

    internal IReadOnlyDictionary<string, ObjectBehaviorPlan> Behaviors { get; }

    // 测试与旧直接构造只装配行为，不创建组件扩展旁路。
    internal static ObjectOperationAssemblyPlan ForBehaviors(IEnumerable<IObjectBehavior> behaviors)
    {
        var options = new ObjectOperationAssemblyOptions();
        foreach (var behavior in behaviors)
        {
            options.Behaviors.Add(new(
                behavior.Definition.OwnerModuleId,
                behavior.Definition));
        }
        return new ObjectOperationAssemblyPlan(options);
    }

    // 一次性校验完整候选；任何错误都不返回部分行为或部分扩展计划。
    private static IReadOnlyDictionary<string, ObjectBehaviorPlan> Compile(ObjectOperationAssemblyOptions options)
    {
        var behaviors = new Dictionary<string, ObjectBehaviorRegistration>(StringComparer.Ordinal);
        foreach (var registration in options.Behaviors)
        {
            ValidateBehavior(registration);
            if (!behaviors.TryAdd(registration.Definition.ObjectKey, registration))
            {
                throw new InvalidOperationException(
                    $"OBJECT_OPERATION_BEHAVIOR_DUPLICATED: {registration.Definition.ObjectKey}");
            }
        }

        var extensions = options.Extensions
            .GroupBy(static registration =>
                (registration.Definition.ObjectKey, registration.Definition.Action))
            .ToDictionary(
                static group => group.Key,
                group => CompileExtensions(group.Key.ObjectKey, group.Key.Action, group.ToArray(), behaviors));

        var plans = behaviors.ToDictionary(
            static pair => pair.Key,
            pair => new ObjectBehaviorPlan(
                pair.Value.Definition,
                extensions
                    .Where(extension => string.Equals(extension.Key.ObjectKey, pair.Key, StringComparison.Ordinal))
                    .ToDictionary(static extension => extension.Key.Action, static extension => extension.Value),
                new Dictionary<ObjectOperationAction, ObjectOperationCoordinationPlan>()),
            StringComparer.Ordinal);
        return plans.ToDictionary(
            static pair => pair.Key,
            pair => pair.Value with
            {
                Coordinations = pair.Value.Definition.SupportedActions.ToDictionary(
                    static action => action,
                    action => CompileCoordination(new(pair.Key, action), plans))
            },
            StringComparer.Ordinal);
    }

    // 行为 owner、动作集合和开放动作必须在任何扩展解析前成立。
    private static void ValidateBehavior(ObjectBehaviorRegistration registration)
    {
        var definition = registration.Definition;
        if (!string.Equals(registration.OwnerModuleId, definition.OwnerModuleId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"OBJECT_OPERATION_BEHAVIOR_OWNER_MISMATCH: {definition.ObjectKey}/{registration.OwnerModuleId}/{definition.OwnerModuleId}");
        }
        if (definition.SupportedActions.Count == 0)
        {
            throw new InvalidOperationException($"OBJECT_OPERATION_ACTIONS_REQUIRED: {definition.ObjectKey}");
        }
        var unsupported = definition.OpenExtensionActions
            .FirstOrDefault(action => !definition.SupportedActions.Contains(action));
        if (unsupported is not null)
        {
            throw new InvalidOperationException(
                $"OBJECT_OPERATION_OPEN_ACTION_UNSUPPORTED: {definition.ObjectKey}/{unsupported.Value}");
        }
        var unsupportedCall = definition.Calls.FirstOrDefault(call =>
            !definition.SupportedActions.Contains(call.SourceAction));
        if (unsupportedCall is not null)
        {
            throw new InvalidOperationException(
                $"OBJECT_OPERATION_CALL_SOURCE_UNSUPPORTED: {definition.ObjectKey}/{unsupportedCall.SourceAction.Value}");
        }
    }

    // 扩展先完成目标与模块依赖校验，再对同一动作建立稳定拓扑。
    private static IReadOnlyList<ObjectOperationExtensionPlan> CompileExtensions(
        string objectKey,
        ObjectOperationAction action,
        IReadOnlyList<ObjectOperationExtensionRegistration> registrations,
        IReadOnlyDictionary<string, ObjectBehaviorRegistration> behaviors)
    {
        if (!behaviors.TryGetValue(objectKey, out var behavior))
        {
            throw new InvalidOperationException(
                $"OBJECT_OPERATION_EXTENSION_TARGET_NOT_FOUND: {objectKey}/{action.Value}");
        }
        if (!behavior.Definition.OpenExtensionActions.Contains(action))
        {
            throw new InvalidOperationException(
                $"OBJECT_OPERATION_EXTENSION_ACTION_NOT_OPEN: {objectKey}/{action.Value}");
        }

        var byKey = new Dictionary<string, ObjectOperationExtensionRegistration>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            var definition = registration.Definition;
            if (!string.Equals(definition.TargetModuleId, behavior.Definition.OwnerModuleId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"OBJECT_OPERATION_EXTENSION_TARGET_MODULE_MISMATCH: {definition.ExtensionKey}/{definition.TargetModuleId}/{behavior.Definition.OwnerModuleId}");
            }
            if (!string.Equals(registration.ProviderModuleId, definition.TargetModuleId, StringComparison.Ordinal)
                && !registration.ProviderDependencies.Contains(definition.TargetModuleId, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"OBJECT_OPERATION_EXTENSION_MODULE_DEPENDENCY_REQUIRED: {definition.ExtensionKey}/{registration.ProviderModuleId}/{definition.TargetModuleId}");
            }
            if (!byKey.TryAdd(definition.ExtensionKey, registration))
            {
                throw new InvalidOperationException(
                    $"OBJECT_OPERATION_EXTENSION_DUPLICATED: {objectKey}/{action.Value}/{definition.ExtensionKey}");
            }
        }

        var outgoing = byKey.Keys.ToDictionary(static key => key, static _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var indegree = byKey.Keys.ToDictionary(static key => key, static _ => 0, StringComparer.Ordinal);
        foreach (var registration in byKey.Values)
        {
            foreach (var dependency in registration.Definition.RunsAfter)
            {
                AddEdge(dependency, registration.Definition.ExtensionKey, byKey, outgoing, indegree, objectKey, action);
            }
            foreach (var dependency in registration.Definition.RunsBefore)
            {
                AddEdge(registration.Definition.ExtensionKey, dependency, byKey, outgoing, indegree, objectKey, action);
            }
        }

        var ready = new SortedSet<string>(indegree.Where(static pair => pair.Value == 0).Select(static pair => pair.Key), StringComparer.Ordinal);
        var ordered = new List<ObjectOperationExtensionPlan>(byKey.Count);
        while (ready.Count > 0)
        {
            var key = ready.Min!;
            ready.Remove(key);
            ordered.Add(new(key, byKey[key].Definition.Calls));
            foreach (var dependent in outgoing[key].Order(StringComparer.Ordinal))
            {
                if (--indegree[dependent] == 0)
                {
                    ready.Add(dependent);
                }
            }
        }
        if (ordered.Count != byKey.Count)
        {
            throw new InvalidOperationException(
                $"OBJECT_OPERATION_EXTENSION_ORDER_CYCLE: {objectKey}/{action.Value}");
        }
        return ordered;
    }

    // 从根操作沿行为与扩展的静态声明计算完整闭包；visited 只避免重复展开，不裁决运行时递归。
    private static ObjectOperationCoordinationPlan CompileCoordination(
        ObjectOperationIdentity root,
        IReadOnlyDictionary<string, ObjectBehaviorPlan> behaviors)
    {
        var operations = new HashSet<ObjectOperationIdentity>();
        var callsByOperation = new Dictionary<ObjectOperationIdentity, IReadOnlySet<ObjectOperationIdentity>>();
        var pending = new Stack<ObjectOperationIdentity>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            if (!operations.Add(current))
            {
                continue;
            }

            if (!behaviors.TryGetValue(current.ObjectKey, out var behavior)
                || !behavior.Definition.SupportedActions.Contains(current.Action))
            {
                throw new InvalidOperationException(
                    $"OBJECT_OPERATION_CALL_TARGET_NOT_FOUND: {current.ObjectKey}/{current.Action.Value}");
            }

            var directTargets = DirectTargets(current, behavior)
                .Select(target => new ObjectOperationIdentity(target.ObjectKey, target.Action))
                .ToHashSet();
            callsByOperation[current] = directTargets;
            foreach (var targetOperation in directTargets)
            {
                pending.Push(targetOperation);
            }
        }

        var modelKeys = operations
            .Select(operation => behaviors[operation.ObjectKey].Definition.ModelKey)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new ObjectOperationCoordinationPlan(modelKeys, callsByOperation);
    }

    // 一个动作的直接目标同时来自对象拥有者和该动作上全部正式扩展。
    private static IEnumerable<ObjectOperationTarget> DirectTargets(
        ObjectOperationIdentity operation,
        ObjectBehaviorPlan behavior)
    {
        foreach (var call in behavior.Definition.Calls.Where(call => call.SourceAction == operation.Action))
        {
            yield return call.Target;
        }
        if (!behavior.Extensions.TryGetValue(operation.Action, out var extensions))
        {
            yield break;
        }
        foreach (var target in extensions.SelectMany(static extension => extension.Calls))
        {
            yield return target;
        }
    }

    // 顺序依赖必须引用同一扩展点内的已登记扩展，重复边只计算一次。
    private static void AddEdge(
        string before,
        string after,
        IReadOnlyDictionary<string, ObjectOperationExtensionRegistration> registrations,
        IDictionary<string, HashSet<string>> outgoing,
        IDictionary<string, int> indegree,
        string objectKey,
        ObjectOperationAction action)
    {
        if (!registrations.ContainsKey(before) || !registrations.ContainsKey(after))
        {
            var missing = !registrations.ContainsKey(before) ? before : after;
            throw new InvalidOperationException(
                $"OBJECT_OPERATION_EXTENSION_ORDER_DEPENDENCY_NOT_FOUND: {objectKey}/{action.Value}/{missing}");
        }
        if (outgoing[before].Add(after))
        {
            indegree[after]++;
        }
    }
}

// Host 在接受请求前解析静态计划并构造实际贡献；验证作用域随后释放。
internal sealed class ObjectOperationAssemblyStartupValidator : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;

    // 保存作用域工厂，启动时用独立短作用域实例化并验证全部正式贡献。
    public ObjectOperationAssemblyStartupValidator(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    // 在 Host 接受请求前解析实际工厂；构造失败或声明漂移都会拒绝整份候选。
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = _scopeFactory.CreateAsyncScope();
        _ = scope.ServiceProvider.GetRequiredService<ObjectOperationAssemblyPlan>();
        _ = scope.ServiceProvider.GetRequiredService<ObjectOperationContributionSet>();
    }

    // 静态计划没有停止期资源。
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
