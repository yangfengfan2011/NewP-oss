using System.Reflection;
using ICP.Platform.Modules.Organization.Controllers;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Modules.Organization.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public sealed class PersonUserLinksControllerTests
{
    [Fact]
    public void Controller_DeclaresOnlyPersonUserLinkRoutes()
    {
        var controllerType = typeof(PersonUserLinksController);

        Assert.Contains(controllerType.GetCustomAttributes<RouteAttribute>(), route => route.Template == "api/organization/persons");
        Assert.Contains(controllerType.GetMethod(nameof(PersonUserLinksController.SearchUserOptions))!.GetCustomAttributes<HttpGetAttribute>(), route => route.Template == "user-options");
        Assert.Contains(controllerType.GetMethod(nameof(PersonUserLinksController.LinkUser))!.GetCustomAttributes<HttpPutAttribute>(), route => route.Template == "{id}/user-link");
    }

    [Fact]
    public async Task LinkUser_WhenServiceThrows_ReturnsBadRequest()
    {
        var controller = new PersonUserLinksController(
            new EmptyIdentityUserDirectory(),
            new FailingPersonUserLinkService());

        var response = await controller.LinkUser(
            "Person_missing",
            new LinkPersonUserRequest { UserId = "user-1" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
        Assert.NotNull(badRequest.Value);
    }

    private sealed class EmptyIdentityUserDirectory : IIdentityUserDirectory
    {
        public Task EnsureUserExistsAsync(string userId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<IdentityUserOptionDto>> SearchAsync(string keyword, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IdentityUserOptionDto>>([]);
    }

    private sealed class FailingPersonUserLinkService : IPersonUserLinkService
    {
        public Task<PersonUserLinkDto> LinkUserAsync(string personId, LinkPersonUserRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("人员不存在。");
    }
}
