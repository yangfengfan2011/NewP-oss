using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Modules.Authorization.Services;

/// <summary>只消费活动按钮投影与真实端点绑定；请求只能引用身份和正式业务输入。</summary>
public sealed class ButtonExecutionAccess(CanonicalModulePlan plan, IHttpContextAccessor http,
    IDataScopeResourceCatalog resources, IObjectActionAccess access) : IButtonExecutionAccess
{
    /// <inheritdoc/>
    public ButtonExecutionContext Bind(string resourceKey, string objectName, string operation, string objectId)
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
            || declaration.Object is not null && declaration.Object != objectName || declaration.ModuleId != resource.ModuleId
            || !declaration.Resources.SequenceEqual([resourceKey]) || owners?.Count != 1 || owners[0].ModuleId != resource.ModuleId
            || endpoint != new ButtonExecutionEndpoint(resourceKey + "." + operation, resourceKey, objectName)
            || bindings?.Count != 1 || bindings[0].BehaviorKey != resourceKey + "." + operation
            || string.IsNullOrWhiteSpace(objectId) || !objectId.StartsWith(objectName + "_", StringComparison.Ordinal))
            throw new ActionDataAccessException(400, "ButtonExecution.BindingInvalid");
        if (positions.Count != 1 || !declaration.FeaturePositions.Contains(positions[0]!, StringComparer.Ordinal))
            throw new ActionDataAccessException(403, "ButtonExecution.FeatureMismatch");
        return new(declaration.Id, positions[0]!, declaration.BusinessBinding, resourceKey, objectName, objectId);
    }

    /// <inheritdoc/>
    public async Task<TResult> ExecuteAsync<TResult>(ButtonExecutionContext context, ButtonExecutionTarget target,
        Func<CancellationToken, Task<TResult>> execute, CancellationToken cancellationToken = default)
    {
        var current = Bind(context.ResourceKey, context.ObjectName, context.BusinessBinding[(context.ResourceKey.Length + 1)..], context.ObjectId);
        var resource = resources.Resources.Single(item => item.ResourceKey == context.ResourceKey);
        if (current != context || target.ResourceKey != context.ResourceKey || target.ObjectName != context.ObjectName
            || target.ObjectId != context.ObjectId || !target.Before.TryGetValue(resource.IdField, out var id) || id?.ToString() != target.ObjectId)
            throw new ActionDataAccessException(400, "ButtonExecution.TargetMismatch");
        var declaration = plan.ActionDataAccessCatalog.Single(item => item.Id == context.ButtonId);
        if (declaration.Object is not null)
        {
            var decision = await access.CheckAsync(declaration.Object, target.ObjectId, declaration.Actions, cancellationToken);
            if (decision != DataExecutionDecision.Allowed)
                throw new ActionDataAccessException(decision == DataExecutionDecision.NotFound ? 404 : 403,
                    decision == DataExecutionDecision.NotFound ? "StandardCrud.NotFound" : "DataScope.ActionDenied");
        }
        return await execute(cancellationToken);
    }
}
