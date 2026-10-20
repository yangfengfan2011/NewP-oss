using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Organization.Controllers;

/// <summary>专属候选在原功能资格之后核对具体读取来源；不将宿主CRUD资格当作候选数据要求。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OrganizationLookupExecutionAttribute(string execution, string resourceKey) : Attribute, IAsyncActionFilter
{
    public DataExecutionBindingReference Binding { get; } = new(execution, "organization.backend", "read");
    public string ResourceKey { get; } = resourceKey;

    /// <inheritdoc/>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try
        {
            context.HttpContext.RequestServices.GetRequiredService<IDataExecutionAccess>().Validate([Binding], ResourceKey);
            await next();
        }
        catch (ActionDataAccessException exception)
        {
            context.Result = ActionDataAccessProblem.ToResult(exception, exception.Code);
        }
    }
}
