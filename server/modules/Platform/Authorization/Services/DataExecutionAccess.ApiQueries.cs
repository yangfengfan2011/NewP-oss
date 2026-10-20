using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ICP.Platform.Modules.Authorization.Services;

public sealed partial class DataExecutionAccess
{
    /// <inheritdoc/>
    public Task<DataScopeReadPlan> GetApiQueryReadPlanAsync(DataExecutionApiQueryContext context, string resourceKey,
        CancellationToken cancellationToken = default)
    {
        var contract = ResolveApiSource(context?.Binding, context?.Identity, resourceKey);
        if (contract.Requirements.Count != 1 || contract.Branches.Count != 0) throw new ActionDataAccessException(400, "ActionDataAccess.QueryInvalid");
        var requirement = contract.Requirements[0];
        if (context!.Binding.Stage != "read" || context.Role != "query" || requirement.Role != context.Role || requirement.Stage != "read" || requirement.Shape != "query"
            || requirement.MinimumCount != 0 || requirement.MaximumCount is not null || requirement.Branch is not null)
            throw new ActionDataAccessException(400, "ActionDataAccess.QueryInvalid");
        RequireReadPlanRole(requirement, resourceKey);
        return (queryReads ?? throw new InvalidOperationException("DATA_EXECUTION_QUERY_READS_REQUIRED")).GetReadPlanAsync(resourceKey, cancellationToken);
    }

    // 只读计划与对象执行共享固定API来源核对；阶段、角色和目标形状由各自入口严格限定。
    private DataExecutionContract ResolveApiSource(DataExecutionBindingReference? binding, string? identity, string resourceKey)
    {
        var request = http.HttpContext ?? throw new ActionDataAccessException(400, "ActionDataAccess.ContextRequired");
        if (request.User.Identity?.IsAuthenticated != true) throw new ActionDataAccessException(401, "ActionDataAccess.SubjectRequired");
        // 此纯固定API不开放客户端声明选择；拒绝foreign头，不将entry缺声明请求导向该方法。
        if (request.Request.Headers.ContainsKey("X-NewP-Data-Declaration") || binding is null
            || binding.Branch is not null || string.IsNullOrWhiteSpace(identity)
            || request.GetEndpoint() is not RouteEndpoint endpoint || string.IsNullOrWhiteSpace(endpoint.RoutePattern.RawText))
            throw ApiBindingInvalid();
        var owners = endpoint.Metadata.GetOrderedMetadata<EndpointModuleAttribute>();
        var methods = endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>();
        if (owners.Count != 1 || methods.Count != 1 || !methods[0].HttpMethods.Contains(request.Request.Method, StringComparer.Ordinal))
            throw ApiBindingInvalid();
        var module = plan.Modules.SingleOrDefault(module => module.ModuleId == owners[0].ModuleId);
        var contract = module?.DataExecutions.SingleOrDefault(contract => contract.Key == binding.Execution);
        if (contract is null || contract.ModuleId != module!.ModuleId || contract.Provider != binding.Provider
            || module.BackendBehaviorKey != binding.Provider || contract.Boundary != "api" || contract.LegacyActions
            || contract.FixedBindings.Count(source => source.Kind == "api" && source.Identity == identity
                && source.HttpMethod == request.Request.Method && source.Route is not null
                && ApiRouteIdentity(source.Route) == ApiRouteIdentity(endpoint.RoutePattern.RawText)) != 1)
            throw ApiBindingInvalid();
        var resource = (resources ?? throw new InvalidOperationException("DATA_EXECUTION_RESOURCE_CATALOG_REQUIRED"))
            .Resources.SingleOrDefault(resource => resource.ResourceKey == resourceKey);
        if (resource is null || !ModuleDependencyPolicy.Allows(plan, module.ModuleId, resource.ModuleId)) throw ApiBindingInvalid();
        return contract;
    }

    // MVC与Minimal模板可能有或没有同一个前导斜杠；不使用请求Path或移除模板内部结构。
    private static string ApiRouteIdentity(string route) => route.StartsWith('/') ? route : "/" + route;

    // API来源错误与entry来源错误独立，不能在异常分支改选另一种绑定。
    private static ActionDataAccessException ApiBindingInvalid() => new(400, "ActionDataAccess.ApiBindingInvalid");
}
