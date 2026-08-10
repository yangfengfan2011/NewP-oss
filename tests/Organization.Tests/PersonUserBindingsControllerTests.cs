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

public sealed class PersonUserBindingsControllerTests
{
    [Fact]
    public void Controller_DeclaresOnlyPermanentUserBindingRoutes()
    {
        var controllerType = typeof(PersonUserBindingsController);

        Assert.Contains(controllerType.GetCustomAttributes<RouteAttribute>(), route => route.Template == "api/organization/persons");
        Assert.Contains(
            controllerType.GetMethod(nameof(PersonUserBindingsController.SearchCandidates))!.GetCustomAttributes<HttpGetAttribute>(),
            route => route.Template == "{personEid}/user-binding/candidates");
        Assert.Contains(
            controllerType.GetMethod(nameof(PersonUserBindingsController.Bind))!.GetCustomAttributes<HttpPutAttribute>(),
            route => route.Template == "{personEid}/user-binding");
    }

    [Fact]
    public async Task Bind_WhenPersonAlreadyBound_ReturnsStableConflictProblemDetails()
    {
        var controller = Controller(new FailingBindingService(
            new PersonUserBindingException(PersonUserBindingFailureCodes.PersonAlreadyBound)));

        var response = await controller.Bind(
            "Person_1",
            new BindPersonUserRequest("User_2", 1, "改绑"));

        var conflict = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Contains("application/problem+json", conflict.ContentTypes);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(PersonUserBindingFailureCodes.PersonAlreadyBound, problem.Extensions["code"]);
        Assert.Equal("trace-controller", problem.Extensions["traceId"]);
    }

    private static PersonUserBindingsController Controller(IPersonUserBindingService service)
    {
        var controller = new PersonUserBindingsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    TraceIdentifier = "trace-controller",
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "User_Admin")],
                        "test"))
                }
            }
        };
        return controller;
    }

    private sealed class FailingBindingService(Exception exception) : IPersonUserBindingService
    {
        public ValueTask<IReadOnlyList<PersonUserBindingCandidateDto>> SearchCandidatesAsync(
            string personEid,
            string? query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<PersonUserBindingCandidateDto>>([]);

        public ValueTask<PersonUserBindingDto> BindAsync(
            string personEid,
            BindPersonUserRequest request,
            PersonUserBindingActor actor,
            CancellationToken cancellationToken = default) => ValueTask.FromException<PersonUserBindingDto>(exception);
    }
}
