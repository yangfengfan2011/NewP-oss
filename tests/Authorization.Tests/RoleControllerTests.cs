using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Authorization.Controllers;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public sealed class RoleControllerTests
{
    [Fact]
    public async Task AssignmentInputs_ExposeSpecificFactsWithoutChangingRejectionStatus()
    {
        var workbench = new FeatureWorkbenchController(null!, null!, null!, null!, null!, null!);
        var featureFailure = Assert.IsType<BadRequestObjectResult>(await workbench.Save("role", new FeatureGrantRequest(null), CancellationToken.None));
        Assert.Equal("AUTHORIZATION_FEATURE_SET_REQUIRED", Assert.IsType<ProblemDetails>(featureFailure.Value).Extensions["code"]);
        var members = new UserRoleController(null!, null!, new DirectCommandExecutor(), new UserRolesButtonHandler(new UserRoleAssignments(null!, null!, null!, null!, null!, null!)));
        var memberFailure = Assert.IsType<BadRequestObjectResult>(await members.AssignUserRoles("account", new AssignUserRolesRequest { RoleCodes = null! }, CancellationToken.None));
        Assert.Equal("AUTHORIZATION_ROLE_SET_REQUIRED", Assert.IsType<ProblemDetails>(memberFailure.Value).Extensions["code"]);
    }
    [Theory]
    [InlineData("create", "dependency")]
    [InlineData("update", "dependency")]
    [InlineData("delete", "dependency")]
    [InlineData("create", "audit")]
    [InlineData("update", "audit")]
    [InlineData("delete", "audit")]
    [InlineData("create", "cancel")]
    [InlineData("update", "cancel")]
    [InlineData("delete", "cancel")]
    public async Task Write_DependencyFailurePropagatesWithoutBecomingBadRequest(string operation, string kind)
    {
        Exception failure = kind switch
        {
            "audit" => new EntityAuditStampUnavailableException(),
            "cancel" => new OperationCanceledException(),
            _ => new InvalidOperationException("repository-unavailable")
        };
        var controller = CreateController(new FailingWriteRepository(failure));

        var actual = await Record.ExceptionAsync(() => InvokeAsync(controller, operation, "Role_1",
            new RoleDto { Code = "Ordinary", Name = "普通角色" }));

        Assert.Same(failure, actual);
    }

    [Theory]
    [InlineData("create", "LocalAdmin", false, "受保护的角色身份不能通过普通管理创建。")]
    [InlineData("create", "Existing", false, "角色代码 'Existing' 已存在")]
    [InlineData("update", "Missing", false, "角色 'Missing' 不存在")]
    [InlineData("delete", "Missing", false, "角色 'Missing' 不存在")]
    [InlineData("update", "Role_1", true, "系统内置角色不允许修改")]
    [InlineData("delete", "Role_1", true, "系统内置角色不允许删除")]
    public async Task Write_KnownRejectionPreservesProblemDetails(string operation, string target, bool builtIn, string title)
    {
        var repository = new RoleRepository();
        await repository.CreateAsync(new Role { Id = "Role_1", Code = "Existing", Name = "已有角色", IsBuiltIn = builtIn });
        var controller = CreateController(repository);

        var result = await InvokeAsync(controller, operation, target, new RoleDto { Code = target, Name = "请求名称" });

        var rejected = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(rejected.Value);
        Assert.Equal(400, rejected.StatusCode);
        Assert.Equal(400, problem.Status);
        Assert.Equal(title, problem.Title);
        Assert.Equal(operation == "create" ? target == "LocalAdmin" ? "AUTHORIZATION_ROLE_PROTECTED_CREATE" : "AUTHORIZATION_ROLE_CODE_EXISTS" : target == "Missing" ? "AUTHORIZATION_ROLE_NOT_FOUND" : operation == "update" ? "AUTHORIZATION_ROLE_PROTECTED_UPDATE" : "AUTHORIZATION_ROLE_PROTECTED_DELETE", problem.Extensions["code"]);
        Assert.Equal("AuthorizationErrors." + problem.Extensions["code"], problem.Extensions["reasonKey"]);
    }

    private static RoleController CreateController(IRoleRepository repository)
    {
        var service = new RoleService(repository, new RolePermissionRepository(), new UserRoleRepository(), new PlatformObjectIdGenerator());
        // 写入不消费查询协作者；使用真实业务服务验证异常从持久化一路到 HTTP adapter 的传播。
        return new RoleController(service, null!, new DirectCommandExecutor(), new RoleCreateButtonHandler(service), new RoleUpdateButtonHandler(service)) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    }

    private static async Task<IActionResult> InvokeAsync(RoleController controller, string operation, string target, RoleDto request) =>
        operation switch
        {
            "create" => (await controller.CreateRole(request, CancellationToken.None)).Result!,
            "update" => (await controller.UpdateRole(target, request, CancellationToken.None)).Result!,
            "delete" => await controller.DeleteRole(target, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private sealed class FailingWriteRepository(Exception failure) : IRoleRepository
    {
        public Task<Role?> GetByIdAsync(string roleId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Role?>(new Role { Id = roleId, Code = "Ordinary", Name = "普通角色" });
        public Task<Role?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult<Role?>(null);
        public Task CreateAsync(Role role, CancellationToken cancellationToken = default) => throw failure;
        public Task UpdateAsync(Role role, CancellationToken cancellationToken = default) => throw failure;
        public Task DeleteAsync(string roleId, CancellationToken cancellationToken = default) => throw failure;
        public Task<List<Role>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Role>> GetByIdsAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

internal sealed class DirectCommandExecutor : ICP.Platform.Authorization.Contracts.IButtonBusinessExecutor
{
    public Task<TResult> ExecuteAsync<TInput, TState, TResult>(ICP.Platform.Authorization.Contracts.IButtonBusinessHandler<TInput, TState, TResult> handler,
        TInput input, CancellationToken token = default) => handler.WithinBoundaryAsync(input, "", (state, inner) => handler.ExecuteAsync(state, inner), token);
}
