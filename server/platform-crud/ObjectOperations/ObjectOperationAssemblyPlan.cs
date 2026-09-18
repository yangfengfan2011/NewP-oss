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

internal sealed record ObjectOperationExtensionPlan(string ExtensionKey);

internal sealed record ObjectBehaviorPlan(
    ObjectBehaviorDefinition Definition,
    IReadOnlyDictionary<ObjectOperationAction, IReadOnlyList<ObjectOperationExtensionPlan>> Extensions);

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

        return behaviors.ToDictionary(
            static pair => pair.Key,
            pair => new ObjectBehaviorPlan(
                pair.Value.Definition,
                extensions
                    .Where(extension => string.Equals(extension.Key.ObjectKey, pair.Key, StringComparison.Ordinal))
                    .ToDictionary(static extension => extension.Key.Action, static extension => extension.Value)),
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
            ordered.Add(new(key));
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

// Host 在接受请求前解析静态计划；构造成功后无需执行运行时代码。
internal sealed class ObjectOperationAssemblyStartupValidator : IHostedService
{
    private readonly ObjectOperationAssemblyPlan _plan;

    // 通过构造依赖强制 Host 启动解析完整静态装配计划。
    public ObjectOperationAssemblyStartupValidator(ObjectOperationAssemblyPlan plan)
    {
        _plan = plan;
    }

    // 访问 plan 保证 DI 已完成完整候选编译。
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = _plan.Behaviors.Count;
        return Task.CompletedTask;
    }

    // 静态计划没有停止期资源。
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
