using System.Reflection;
using System.Security.Claims;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class PersonLifecycleControllerTests
{
    [Fact]
    public void Controller_DeclaresOnlyDedicatedStatusCommandRoute()
    {
        var method = typeof(PersonLifecycleController).GetMethod(nameof(PersonLifecycleController.SetStatus));

        Assert.Contains(method!.GetCustomAttributes<HttpPutAttribute>(), route => route.Template == "{personEid}/status");
    }

    [Fact]
    public async Task SetStatus_WhenLifecycleConflicts_ReturnsStableProblemDetails()
    {
        var controller = Controller(new FailingService(
            new PersonLifecycleException(PersonLifecycleFailureCodes.Conflict, new { revision = 5 })));

        var response = await controller.SetStatus(
            "Person_1",
            new SetPersonLifecycleStatusRequest(false, 4, "并发停用"));

        var conflict = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Contains("application/problem+json", conflict.ContentTypes);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(PersonLifecycleFailureCodes.Conflict, problem.Extensions["code"]);
        Assert.Equal("trace-lifecycle-controller", problem.Extensions["traceId"]);
        Assert.NotNull(problem.Extensions["current"]);
    }

    private static PersonLifecycleController Controller(IPersonLifecycleService service) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "trace-lifecycle-controller",
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "User_Admin")],
                    "test"))
            }
        }
    };

    private sealed class FailingService(Exception exception) : IPersonLifecycleService
    {
        public ValueTask<PersonLifecycleDto> SetStatusAsync(
            string personEid,
            SetPersonLifecycleStatusRequest request,
            PersonLifecycleActor actor,
            CancellationToken cancellationToken = default) => ValueTask.FromException<PersonLifecycleDto>(exception);
    }
}
