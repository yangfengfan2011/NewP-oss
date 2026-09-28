using System.Security.Claims;
using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Modules.Audit.Controllers;
using ICP.Platform.Modules.Audit.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;
namespace ICP.Platform.Modules.Audit.Tests;
public sealed class BusinessChangeQueryErrorTests
{
    [Fact]
    public async Task InvalidTimeRange_PreservesBadRequestAndProvidesStableTranslationFact()
    {
        var controller = new BusinessChangesController(null!, new Administrator())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin")])) } }
        };
        var response = Assert.IsType<BadRequestObjectResult>(await controller.List(new BusinessAuditFilter(From: new DateTime(2026, 9, 10)), default));
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(400, problem.Status);
        Assert.Equal("时间范围必须为有序 UTC 时间点。", problem.Title);
        Assert.Equal("Audit.InvalidTimeRange", problem.Extensions["code"]);
        Assert.Equal("BusinessChangeView.InvalidTimeRange", problem.Extensions["reasonKey"]);
    }
    private sealed class Administrator : ILocalAdministratorRoleAssignment
    {
        public ValueTask AssignAsync(string accountEid, string actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<LocalAdministratorAssignmentState> InspectAsync(string accountEid, CancellationToken cancellationToken = default) => ValueTask.FromResult(LocalAdministratorAssignmentState.Assigned);
    }
}
