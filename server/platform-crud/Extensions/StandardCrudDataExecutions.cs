using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Kernel.ModulePlanning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Crud.Extensions;

/// <summary>真实端点的执行绑定，供运行时和逐片覆盖检查共同核对。</summary>
public sealed record StandardCrudDataExecutionEndpoint(string ResourceKey, string Operation,
    IReadOnlyList<DataExecutionBindingReference> Bindings);

/// <summary>标准写绑定实际按钮，读取保留独立来源；请求不能通过缺失身份改变路由。</summary>
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
        var resourceCatalog = services?.GetService<IDataScopeResourceCatalog>();
        // 通用资源端点的声明仍由资源拥有者发布，不能从端点模块推导或补造按钮。
        bool MatchesHandler(Endpoint endpoint, ActionDataAccessRequirement button)
        {
            var features = endpoint.Metadata.GetMetadata<ButtonBusinessHandlerAttribute>()?.OperationKey is not null
                ? plan.FeatureCatalog.Where(point => button.FeaturePositions.Contains(point.Position, StringComparer.Ordinal))
                    .Select(point => point.BehaviorKey).Distinct(StringComparer.Ordinal).Take(2).ToArray() : [];
            return ButtonBusinessEndpointBindings.Matches(endpoint, button.ModuleId, button.BusinessBinding!)
                || resourceCatalog?.Resources.Any(resource => ButtonBusinessEndpointBindings.Matches(endpoint,
                    button.ModuleId, button.BusinessBinding!, resource, features.Length == 1 ? features[0] : null)) == true;
        }
        // 通用实现可先于可选按钮发布者注册；类型和DI仍须有效，实际按钮继续按正向覆盖与调用绑定核验。
        foreach (var endpoint in handlers)
        {
            var handler = endpoint.Metadata.GetMetadata<ButtonBusinessHandlerAttribute>()!.HandlerType;
            if (handler.IsAbstract || !handler.GetInterfaces().Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IButtonBusinessHandler<,,>))
                || services is not null && services.GetRequiredService<IServiceProviderIsService>().IsService(handler) != true
                || !ButtonBusinessEndpointBindings.IsResourceEndpoint(endpoint)
                    && !plan.ActionDataAccessCatalog.Any(button => button.BusinessBinding is not null && MatchesHandler(endpoint, button)))
                throw new InvalidOperationException("BUTTON_BUSINESS_HANDLER_INVALID");
        }
        foreach (var button in plan.ActionDataAccessCatalog.Where(item => item.BusinessBinding is not null && item.GetObjectRequirements().Count > 0))
            if (!buttonBindings.Any(binding => binding!.BusinessBinding == button.BusinessBinding
                && button.GetObjectRequirements().Any(item => item.Object == binding.ObjectName && item.ResourceKey == binding.ResourceKey))
                && !handlers.Any(endpoint => MatchesHandler(endpoint, button)))
                throw new InvalidOperationException("BUTTON_EXECUTION_ENDPOINT_MISSING");
        foreach (var execution in plan.Modules.SelectMany(module => module.DataExecutions)
            .Where(contract => contract.Boundary == "entry" && contract.Provider.StartsWith("standardCrud.", StringComparison.Ordinal)))
            if (!bound.Any(binding => binding.Execution == execution.Key && binding.Provider == execution.Provider))
                throw new InvalidOperationException("STANDARD_CRUD_DATA_EXECUTION_ENDPOINT_MISSING");
    }

    /// <summary>详情与编辑准备只消费独立的正式读取来源。</summary>
    public static IReadOnlyList<DataExecutionBindingReference> DetailReadBindings(StandardCrudDescriptor descriptor)
    {
        if (!descriptor.DataExecutions.TryGetValue("read", out var read)) return [];
        return [new(read, "standardCrud.read", "read")];
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
        StandardCrudDescriptorValidator.ValidateReadSources(descriptor);
        if (operation is "create" or "update" or "delete" or "enable" or "disable")
        {
            var buttons = services.GetRequiredService<CanonicalModulePlan>().ActionDataAccessCatalog
                .Where(item => item.BusinessBinding == descriptor.ResourceKey + "." + operation).ToArray();
            if (buttons.Length == 0 || buttons.Any(item => item.ModuleId != descriptor.ModuleId
                || item.GetObjectRequirements().Count > 0 && !item.GetObjectRequirements().Any(requirement =>
                    requirement.Object == descriptor.ModelName && requirement.ResourceKey == descriptor.ResourceKey)))
                throw new InvalidOperationException("STANDARD_CRUD_BUTTON_BINDING_INVALID");
            endpoint.WithMetadata(new ButtonExecutionEndpoint(descriptor.ResourceKey + "." + operation, descriptor.ResourceKey, descriptor.ModelName));
            endpoint.AddEndpointFilter(async (context, next) =>
            {
                try
                {
                    var id = operation == "create" ? null : context.HttpContext.Request.RouteValues["id"]?.ToString();
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
        if (!descriptor.DataExecutions.TryGetValue(operation, out var execution))
            throw new InvalidOperationException("STANDARD_CRUD_DATA_EXECUTION_BINDING_INVALID");
        var plan = services.GetRequiredService<CanonicalModulePlan>();
        var contract = plan.Modules.SelectMany(module => module.DataExecutions).SingleOrDefault(item => item.Key == execution);
        var provider = "standardCrud." + operation;
        if (contract is null || contract.Boundary != "entry" || contract.ModuleId != descriptor.ModuleId || contract.Provider != provider
            || !contract.Requirements.All(requirement => requirement.Resources.SequenceEqual([descriptor.ResourceKey]))
            || !plan.ActionDataAccessCatalog.Any(requirement => requirement.Execution == execution
                || operation == "export" && requirement.BusinessBinding == execution && requirement.Surface == "export"))
            throw new InvalidOperationException("STANDARD_CRUD_DATA_EXECUTION_BINDING_INVALID");
        var bindings = new List<DataExecutionBindingReference> { new(execution, provider, "read") };
        if (operation == "read") bindings = DetailReadBindings(descriptor).ToList();
        endpoint.WithMetadata(new StandardCrudDataExecutionEndpoint(descriptor.ResourceKey, operation, bindings));
        // 端点按活动声明固定按钮路径，同时保留原查询合同登记；缺失HTTP身份不能回退旧入口。
        if (operation == "export")
        {
            endpoint.WithMetadata(new ButtonBusinessHandlerAttribute(typeof(StandardCrudExportHandler)));
            endpoint.AddEndpointFilter(async (context, next) =>
            {
                try
                {
                    context.HttpContext.RequestServices.GetRequiredService<IButtonExecutionAccess>().BindHandler(typeof(StandardCrudExportHandler));
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
