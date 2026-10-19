using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Crud.Extensions;

/// <summary>真实端点的执行绑定，供运行时和逐片覆盖检查共同核对。</summary>
public sealed record StandardCrudDataExecutionEndpoint(string ResourceKey, string Operation,
    IReadOnlyList<DataExecutionBindingReference> Bindings);

/// <summary>服务端具体执行登记决定迁移路径；请求不能通过缺失或新增声明改变路由。</summary>
public static class StandardCrudDataExecutions
{
    /// <summary>从实际映射的端点核对所有活动标准执行；未绑定的合同不能只靠存在下限启用。</summary>
    public static void ValidateCoverage(IEnumerable<EndpointDataSource> sources, CanonicalModulePlan plan)
    {
        var bound = sources.SelectMany(source => source.Endpoints)
            .Select(endpoint => endpoint.Metadata.GetMetadata<StandardCrudDataExecutionEndpoint>())
            .Where(endpoint => endpoint is not null).SelectMany(endpoint => endpoint!.Bindings).ToArray();
        foreach (var execution in plan.Modules.SelectMany(module => module.DataExecutions)
            .Where(contract => contract.Provider.StartsWith("standardCrud.", StringComparison.Ordinal)))
            if (!bound.Any(binding => binding.Execution == execution.Key && binding.Provider == execution.Provider))
                throw new InvalidOperationException("STANDARD_CRUD_DATA_EXECUTION_ENDPOINT_MISSING");
    }

    /// <summary>将绑定固定到真实端点，并在业务执行前验证声明身份；正式写入仍在原有事务内复核要求。</summary>
    public static RouteHandlerBuilder WithDataExecution(this RouteHandlerBuilder endpoint, IServiceProvider services,
        StandardCrudDescriptor descriptor, string operation)
    {
        if (!descriptor.DataExecutions.TryGetValue(operation, out var execution))
        {
            var published = operation switch { "create" => StandardCrudOperation.Create, "update" => StandardCrudOperation.Update,
                "delete" => StandardCrudOperation.Delete, _ => (StandardCrudOperation?)null };
            if (published is not null && descriptor.Authorization!.FeatureOperations.Contains(published.Value)
                && !descriptor.LegacyDataExecutions.Contains(operation))
                throw new InvalidOperationException("STANDARD_CRUD_DATA_EXECUTION_UNREGISTERED");
            return endpoint;
        }
        var plan = services.GetRequiredService<CanonicalModulePlan>();
        var contract = plan.Modules.SelectMany(module => module.DataExecutions).SingleOrDefault(item => item.Key == execution);
        var provider = "standardCrud." + operation;
        if (contract is null || contract.ModuleId != descriptor.ModuleId || contract.Provider != provider || contract.LegacyActions
            || !contract.Requirements.All(requirement => requirement.Resources.SequenceEqual([descriptor.ResourceKey]))
            || !plan.ActionDataAccessCatalog.Any(requirement => requirement.Execution == execution))
            throw new InvalidOperationException("STANDARD_CRUD_DATA_EXECUTION_BINDING_INVALID");
        var bindings = new List<DataExecutionBindingReference> { new(execution, provider, operation is "read" or "list" ? "read" : "submit") };
        if (operation == "read" && descriptor.DataExecutions.TryGetValue("update", out var update))
            bindings.Add(new(update, "standardCrud.update", "prepare"));
        endpoint.WithMetadata(new StandardCrudDataExecutionEndpoint(descriptor.ResourceKey, operation, bindings));
        endpoint.AddEndpointFilter(async (context, next) =>
        {
            try
            {
                context.HttpContext.RequestServices.GetRequiredService<IDataExecutionAccess>().Validate(bindings, descriptor.ResourceKey);
                return await next(context);
            }
            catch (ActionDataAccessException exception)
            {
                return Results.Problem(statusCode: exception.Status, title: exception.Code,
                    extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
            }
        });
        return endpoint;
    }
}
