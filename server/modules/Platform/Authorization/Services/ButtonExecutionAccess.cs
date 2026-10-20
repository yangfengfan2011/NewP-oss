using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>只消费活动按钮投影与真实端点绑定；请求只能引用身份和正式业务输入。</summary>
public sealed class ButtonExecutionAccess(CanonicalModulePlan plan, IHttpContextAccessor http,
    IDataScopeResourceCatalog resources, IObjectActionAccess access, IButtonQueryReadScope? queries = null) : IButtonExecutionAccess
{
    /// <inheritdoc/>
    public ButtonBusinessInvocation BindHandler(Type handlerType)
    {
        var request = http.HttpContext ?? throw new ActionDataAccessException(400, "ButtonExecution.ContextRequired");
        if (request.User.Identity?.IsAuthenticated != true) throw new ActionDataAccessException(401, "ButtonExecution.SubjectRequired");
        var ids = request.Request.Headers["X-NewP-Button"];
        var positions = request.Request.Headers["X-NewP-Feature"];
        var declaration = ids.Count == 1 ? plan.ActionDataAccessCatalog.SingleOrDefault(item => item.Id == ids[0]) : null;
        var endpoint = request.GetEndpoint();
        var resource = resources.Resources.SingleOrDefault(item => item.ResourceKey == request.Request.RouteValues["resourceKey"]?.ToString());
        if (request.Request.Headers.ContainsKey("X-NewP-Data-Declaration") || declaration?.BusinessBinding is null
            || declaration.Execution.Length != 0
            || !ButtonBusinessEndpointBindings.Matches(endpoint, declaration.ModuleId, declaration.BusinessBinding, resource)
            || endpoint?.Metadata.GetOrderedMetadata<ResourceFeatureBindingAttribute>().Count > 0
                && string.IsNullOrWhiteSpace(request.Request.RouteValues["objectId"]?.ToString())
            || endpoint?.Metadata.GetMetadata<ButtonBusinessHandlerAttribute>()?.HandlerType != handlerType)
            throw new ActionDataAccessException(400, "ButtonExecution.BindingInvalid");
        if (positions.Count != 1 || !declaration.FeaturePositions.Contains(positions[0]!, StringComparer.Ordinal))
            throw new ActionDataAccessException(403, "ButtonExecution.FeatureMismatch");
        return new(declaration.Id, positions[0]!, declaration.BusinessBinding, declaration.ActionKey, handlerType);
    }

    /// <inheritdoc/>
    public async Task<TResult> ExecuteAsync<TResult>(ButtonBusinessInvocation invocation, ButtonBusinessTarget? target,
        Func<CancellationToken, Task<TResult>> execute, CancellationToken cancellationToken = default)
    {
        if (BindHandler(invocation.HandlerType) != invocation) throw new ActionDataAccessException(400, "ButtonExecution.BindingInvalid");
        var declaration = plan.ActionDataAccessCatalog.Single(item => item.Id == invocation.ButtonId);
        var request = http.HttpContext!;
        if (request.GetEndpoint()!.Metadata.GetOrderedMetadata<ResourceFeatureBindingAttribute>().Count > 0
            && (target is not ButtonExecutionTarget existing
                || existing.ResourceKey != request.Request.RouteValues["resourceKey"]?.ToString()
                || existing.ObjectId != request.Request.RouteValues["objectId"]?.ToString()))
            throw new ActionDataAccessException(400, "ButtonExecution.TargetMismatch");
        await CheckTargetAsync(declaration, target, cancellationToken);
        using var querySource = target is ButtonQueryTarget query
            ? (queries ?? throw new ActionDataAccessException(400, "ButtonExecution.QueryContextRequired")).Enter(invocation, query) : null;
        return await execute(cancellationToken);
    }

    /// <inheritdoc/>
    public ButtonExecutionContext Bind(string resourceKey, string objectName, string operation, string? objectId)
    {
        var request = http.HttpContext ?? throw new ActionDataAccessException(400, "ButtonExecution.ContextRequired");
        var ids = request.Request.Headers["X-NewP-Button"];
        var positions = request.Request.Headers["X-NewP-Feature"];
        var declaration = ids.Count == 1 ? plan.ActionDataAccessCatalog.SingleOrDefault(item => item.Id == ids[0]) : null;
        var resource = resources.Resources.SingleOrDefault(item => item.ResourceKey == resourceKey);
        var owners = request.GetEndpoint()?.Metadata.GetOrderedMetadata<EndpointModuleAttribute>();
        var bindings = request.GetEndpoint()?.Metadata.GetOrderedMetadata<RequireFeatureAttribute>();
        var endpoint = request.GetEndpoint()?.Metadata.GetMetadata<ButtonExecutionEndpoint>();
        if (request.User.Identity?.IsAuthenticated != true) throw new ActionDataAccessException(401, "ButtonExecution.SubjectRequired");
        if (request.Request.Headers.ContainsKey("X-NewP-Data-Declaration") || declaration?.BusinessBinding != resourceKey + "." + operation
            || declaration.Execution.Length != 0 || resource is null || resource.ModelName != objectName
            || declaration.ModuleId != resource.ModuleId
            || (declaration.GetObjectRequirements().Count > 0
                ? !declaration.GetObjectRequirements().Any(item => item.Object == objectName && item.ResourceKey == resourceKey)
                : declaration.Resources.Count > 0 && !declaration.Resources.SequenceEqual([resourceKey])) || owners?.Count != 1 || owners[0].ModuleId != resource.ModuleId
            || endpoint != new ButtonExecutionEndpoint(resourceKey + "." + operation, resourceKey, objectName)
            || bindings?.Count != 1 || bindings[0].BehaviorKey != resourceKey + "." + operation
            || (operation == "create" ? objectId is not null : string.IsNullOrWhiteSpace(objectId) || !objectId.StartsWith(objectName + "_", StringComparison.Ordinal)))
            throw new ActionDataAccessException(400, "ButtonExecution.BindingInvalid");
        if (positions.Count != 1 || !declaration.FeaturePositions.Contains(positions[0]!, StringComparer.Ordinal))
            throw new ActionDataAccessException(403, "ButtonExecution.FeatureMismatch");
        return new(declaration.Id, positions[0]!, declaration.BusinessBinding, resourceKey, objectName, objectId);
    }

    /// <inheritdoc/>
    public async Task<TResult> ExecuteAsync<TResult>(ButtonExecutionContext context, ButtonBusinessTarget target,
        Func<CancellationToken, Task<TResult>> execute, CancellationToken cancellationToken = default)
    {
        var current = Bind(context.ResourceKey, context.ObjectName, context.BusinessBinding[(context.ResourceKey.Length + 1)..], context.ObjectId);
        if (current != context || target is not ButtonObjectTarget objectTarget
            || objectTarget.ResourceKey != context.ResourceKey || objectTarget.ObjectName != context.ObjectName
            || (target is ButtonCreationTarget ? context.ObjectId is not null
                : target is not ButtonExecutionTarget existing || existing.ObjectId != context.ObjectId))
            throw new ActionDataAccessException(400, "ButtonExecution.TargetMismatch");
        var declaration = plan.ActionDataAccessCatalog.Single(item => item.Id == context.ButtonId);
        await CheckTargetAsync(declaration, target, cancellationToken);
        return await execute(cancellationToken);
    }

    // 同一状态中的目标逐项准备并立即检查；全部通过才执行业务，不提前读取后项而改变首错。
    private async Task CheckTargetAsync(ActionDataAccessRequirement declaration, ButtonBusinessTarget? target, CancellationToken token)
    {
        if (target is ButtonQueryTarget query)
        {
            ButtonQuerySources.Validate(plan, declaration, query, resources);
            return;
        }
        var requirements = declaration.GetObjectRequirements();
        if (target is ButtonTargetSequence sequence)
        {
            var source = sequence.Targets?.Invoke(token) ?? throw new ActionDataAccessException(400, "ButtonExecution.TargetMismatch");
            await using var iterator = source.GetAsyncEnumerator(token) ?? throw new ActionDataAccessException(400, "ButtonExecution.TargetMismatch");
            var hasTargets = false;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (!await iterator.MoveNextAsync()) break;
                token.ThrowIfCancellationRequested();
                await CheckObjectAsync(requirements, iterator.Current, token);
                hasTargets = true;
            }
            if (!hasTargets) throw new ActionDataAccessException(400, "ButtonExecution.TargetMismatch");
            return;
        }
        IReadOnlyList<ButtonObjectTarget> targets = target switch
        {
            null => Array.Empty<ButtonObjectTarget>(),
            ButtonTargetSet set when set.Targets is { Count: > 0 } => set.Targets,
            ButtonObjectTarget single => new[] { single },
            _ => throw new ActionDataAccessException(400, "ButtonExecution.TargetMismatch")
        };
        if (requirements.Count > 0 && targets.Count == 0)
            throw new ActionDataAccessException(400, "ButtonExecution.TargetMismatch");
        foreach (var objectTarget in targets)
            await CheckObjectAsync(requirements, objectTarget, token);
    }

    // 候选使用完整事实，已有对象使用原保护时点；两者都不继承前一个目标的允许结果。
    private async Task CheckObjectAsync(IReadOnlyList<ButtonObjectRequirement> requirements, ButtonObjectTarget target, CancellationToken token)
    {
        var resource = resources.Resources.SingleOrDefault(item => item.ResourceKey == target?.ResourceKey);
        var values = target switch { ButtonExecutionTarget existing => existing.Before, ButtonCreationTarget candidate => candidate.Values,
            _ => throw new ActionDataAccessException(400, "ButtonExecution.TargetMismatch") };
        var requirement = requirements.SingleOrDefault(item => item.Object == target.ObjectName && item.ResourceKey == target.ResourceKey);
        if (resource is null || resource.ModelName != target.ObjectName
            || !values.TryGetValue(resource.IdField, out var id) || id is not string actualId || !actualId.StartsWith(target.ObjectName + "_", StringComparison.Ordinal)
            || target is ButtonExecutionTarget before && actualId != before.ObjectId
            || requirements.Count > 0 && requirement is null)
            throw new ActionDataAccessException(400, "ButtonExecution.TargetMismatch");
        if (requirement is null) return;
        var decision = target switch
        {
            ButtonCreationTarget candidate => await access.CheckCandidateAsync(requirement.Object, candidate.Values, requirement.Actions, token),
            ButtonExecutionTarget existing => existing.ReadContext is null
                ? await access.CheckAsync(requirement.Object, existing.ObjectId, requirement.Actions, token)
                : await access.CheckAsync(requirement.Object, existing.ObjectId, requirement.Actions, existing.ReadContext, token),
            _ => throw new ActionDataAccessException(400, "ButtonExecution.TargetMismatch")
        };
        if (decision != DataExecutionDecision.Allowed)
            throw new ActionDataAccessException(decision == DataExecutionDecision.NotFound ? 404 : 403,
                decision == DataExecutionDecision.NotFound ? "StandardCrud.NotFound" : "DataScope.ActionDenied");
    }
}
