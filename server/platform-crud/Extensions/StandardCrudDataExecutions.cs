using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
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
    /// <summary>核对实际标准执行与新按钮处理器绑定；活动对象按钮缺真实端点或处理器注册时拒绝启动。</summary>
    public static void ValidateCoverage(IEnumerable<EndpointDataSource> sources, CanonicalModulePlan plan, IServiceProvider? services = null)
    {
        var bound = sources.SelectMany(source => source.Endpoints)
            .Select(endpoint => endpoint.Metadata.GetMetadata<StandardCrudDataExecutionEndpoint>())
            .Where(endpoint => endpoint is not null).SelectMany(endpoint => endpoint!.Bindings)
            .Concat(sources.SelectMany(source => source.Endpoints)
                .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<StandardCrudDataExecutionAttribute>())
                .SelectMany(endpoint => endpoint.Bindings)).ToArray();
        var buttonBindings = sources.SelectMany(source => source.Endpoints)
            .Select(endpoint => endpoint.Metadata.GetMetadata<ButtonExecutionEndpoint>()).Where(item => item is not null).ToArray();
        var handlers = sources.SelectMany(source => source.Endpoints)
            .Where(endpoint => endpoint.Metadata.GetMetadata<ButtonBusinessHandlerAttribute>() is not null).ToArray();
        foreach (var endpoint in handlers)
        {
            var handler = endpoint.Metadata.GetMetadata<ButtonBusinessHandlerAttribute>()!.HandlerType;
            var owners = endpoint.Metadata.GetOrderedMetadata<EndpointModuleAttribute>();
            var features = endpoint.Metadata.GetOrderedMetadata<RequireFeatureAttribute>();
            if (handler.IsAbstract || !handler.GetInterfaces().Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IButtonBusinessHandler<,,>))
                || services is not null && services.GetRequiredService<IServiceProviderIsService>().IsService(handler) != true
                || owners.Count != 1 || features.Count != 1 || !plan.ActionDataAccessCatalog.Any(button =>
                    button.ModuleId == owners[0].ModuleId && button.BusinessBinding == features[0].BehaviorKey))
                throw new InvalidOperationException("BUTTON_BUSINESS_HANDLER_INVALID");
        }
        foreach (var button in plan.ActionDataAccessCatalog.Where(item => item.BusinessBinding is not null && item.Object is not null))
            if (!buttonBindings.Any(binding => binding!.BusinessBinding == button.BusinessBinding
                && binding.ObjectName == button.Object && button.Resources.SequenceEqual([binding.ResourceKey]))
                && !handlers.Any(endpoint => endpoint.Metadata.GetMetadata<EndpointModuleAttribute>()!.ModuleId == button.ModuleId
                    && endpoint.Metadata.GetMetadata<RequireFeatureAttribute>()!.BehaviorKey == button.BusinessBinding))
                throw new InvalidOperationException("BUTTON_EXECUTION_ENDPOINT_MISSING");
        foreach (var execution in plan.Modules.SelectMany(module => module.DataExecutions)
            .Where(contract => contract.Boundary == "entry" && contract.Provider.StartsWith("standardCrud.", StringComparison.Ordinal)))
            if (!bound.Any(binding => binding.Execution == execution.Key && binding.Provider == execution.Provider))
                throw new InvalidOperationException("STANDARD_CRUD_DATA_EXECUTION_ENDPOINT_MISSING");
    }

    /// <summary>详情允许自身完整读取或已登记更新的准备阶段；调用方不能通过声明缺失切换旧路径。</summary>
    public static IReadOnlyList<DataExecutionBindingReference> DetailReadBindings(StandardCrudDescriptor descriptor)
    {
        if (!descriptor.DataExecutions.TryGetValue("read", out var read)) return [];
        // 包未声明独立详情时，read映射必须显式引用本资源update，不能从缺失映射推断。
        if (descriptor.DataExecutions.TryGetValue("update", out var preparation) && read == preparation)
            return [new(read, "standardCrud.update", "prepare")];
        var bindings = new List<DataExecutionBindingReference> { new(read, "standardCrud.read", "read") };
        if (descriptor.DataExecutions.TryGetValue("update", out var update))
            bindings.Add(new(update, "standardCrud.update", "prepare"));
        return bindings;
    }

    /// <summary>将绑定固定到真实端点，并在业务执行前验证声明身份；正式写入仍在原有事务内复核要求。</summary>
    public static RouteHandlerBuilder WithDataExecution(this RouteHandlerBuilder endpoint, IServiceProvider services,
        StandardCrudDescriptor descriptor, string operation)
    {
        var capability = operation switch { "create" => StandardCrudOperation.Create, "update" => StandardCrudOperation.Update,
            "enable" => StandardCrudOperation.Enable, "disable" => StandardCrudOperation.Disable,
            "delete" => StandardCrudOperation.Delete, "list" => StandardCrudOperation.List, "read" => StandardCrudOperation.Detail,
            "options" => StandardCrudOperation.Options, "export" => StandardCrudOperation.Export, _ => (StandardCrudOperation?)null };
        // 关闭URL的DenyAll保持原样；内部合同不能为它增加入口声明或启用能力。
        if (capability is not null && !descriptor.Authorization!.FeatureOperations.Contains(capability.Value)) return endpoint;
        var executionOperation = operation is "enable" or "disable" ? "update" : operation;
        if (!descriptor.DataExecutions.TryGetValue(executionOperation, out var execution))
        {
            var buttons = services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog
                .Where(item => item.BusinessBinding == descriptor.ResourceKey + "." + operation).ToArray();
            if (operation != "update" || buttons.Length == 0 || buttons.Any(item => item.ModuleId != descriptor.ModuleId
                || item.Object is not null && item.Object != descriptor.ModelName))
                throw new InvalidOperationException("STANDARD_CRUD_BUTTON_BINDING_INVALID");
            endpoint.WithMetadata(new ButtonExecutionEndpoint(descriptor.ResourceKey + "." + operation, descriptor.ResourceKey, descriptor.ModelName));
            endpoint.AddEndpointFilter(async (context, next) =>
            {
                try
                {
                    var id = context.HttpContext.Request.RouteValues["id"]?.ToString() ?? "";
                    context.HttpContext.RequestServices.GetRequiredService<IButtonExecutionAccess>()
                        .Bind(descriptor.ResourceKey, descriptor.ModelName, operation, id);
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
        var plan = services.GetRequiredService<CanonicalModulePlan>();
        var contract = plan.Modules.SelectMany(module => module.DataExecutions).SingleOrDefault(item => item.Key == execution);
        var prepareOnly = operation == "read" && descriptor.DataExecutions.TryGetValue("update", out var update) && execution == update;
        var provider = "standardCrud." + (prepareOnly ? "update" : executionOperation);
        if (contract is null || contract.Boundary != "entry" || contract.ModuleId != descriptor.ModuleId || contract.Provider != provider
            || !contract.Requirements.All(requirement => requirement.Resources.SequenceEqual([descriptor.ResourceKey]))
            || !plan.ActionDataAccessCatalog.Any(requirement => requirement.Execution == execution))
            throw new InvalidOperationException("STANDARD_CRUD_DATA_EXECUTION_BINDING_INVALID");
        var bindings = new List<DataExecutionBindingReference> { new(execution, provider, operation is "read" or "list" or "export" or "options" ? "read" : "submit") };
        if (operation == "read") bindings = DetailReadBindings(descriptor).ToList();
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
