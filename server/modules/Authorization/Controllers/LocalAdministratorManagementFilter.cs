using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
namespace ICP.Platform.Modules.Authorization.Controllers;
/// <summary>授权管理只接受可信主体的实时 LocalAdmin 资格，不以授予的普通权限替代。</summary>
public sealed class LocalAdministratorManagementFilter(ILocalAdministratorRoleAssignment administrators) : IAsyncActionFilter
{
    /// <inheritdoc/>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var id = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null || await administrators.InspectAsync(id, context.HttpContext.RequestAborted) != LocalAdministratorAssignmentState.Assigned)
        {
            context.Result = new ForbidResult();
            return;
        }
        await next();
    }
}
