using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Authorization.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Crud.Extensions;

/// <summary>原MVC协议在原功能/Host资格之后核对标准读取来源；不会启用其关闭的通用写入协议。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StandardCrudDataExecutionAttribute(string resourceKey, string execution, string operation) : Attribute, IAsyncActionFilter
{
    public string ResourceKey { get; } = resourceKey;
    public string Operation { get; } = operation;
    public IReadOnlyList<DataExecutionBindingReference> Bindings { get; } = [new(execution, "standardCrud." + operation, "read")];

    /// <inheritdoc/>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var descriptor = services.GetRequiredService<IStandardCrudResourceRegistry>().FindByResourceKey(ResourceKey);
        if (Operation is not ("list" or "read") || descriptor is null
            || !descriptor.DataExecutions.TryGetValue(Operation, out var registered) || registered != Bindings[0].Execution)
            throw new InvalidOperationException("STANDARD_CRUD_DATA_EXECUTION_BINDING_INVALID");
        try
        {
            services.GetRequiredService<IDataExecutionAccess>().Validate(Bindings, ResourceKey);
            await next();
        }
        catch (ActionDataAccessException exception)
        {
            context.Result = ActionDataAccessProblem.ToResult(exception, exception.Code);
        }
    }
}
