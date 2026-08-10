using ICP.Platform.HostApi.Authentication;
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class LocalAuthSeederTests
{
    [Fact]
    public async Task SeedCatalogAsync_ConvergesRoleWithoutAssigningConfiguredLegacyUser()
    {
        var permissionService = new RecordingPermissionService(converges: true);
        var roleService = new RecordingRoleService();
        var seeder = CreateSeeder(permissionService, roleService);

        await seeder.SeedCatalogAsync([Permission("Organization.Person.View")]);

        Assert.Equal(["Organization.Person.View"], roleService.AssignedPermissionCodes);
        Assert.Empty(roleService.AssignedRoleCodes);
    }

    [Fact]
    public async Task SeedAsync_AssignsExactlyTheCompiledActivePermissionCatalog()
    {
        var permissionService = new RecordingPermissionService(converges: true);
        var roleService = new RecordingRoleService();
        var seeder = CreateSeeder(permissionService, roleService);
        var activePermissions = new[]
        {
            Permission("Organization.Person.View"),
            Permission("Organization.Person.Update")
        };

        await seeder.SeedAsync(activePermissions);

        Assert.Equal(
            activePermissions.Select(static permission => permission.Code),
            permissionService.Synchronized.Select(static permission => permission.Code));
        Assert.Equal(
            ["Organization.Person.Update", "Organization.Person.View"],
            roleService.AssignedPermissionCodes.Order(StringComparer.Ordinal));
        Assert.Equal(["LocalAdmin"], roleService.AssignedRoleCodes);
    }

    [Fact]
    public async Task SeedAsync_WhenPermissionCatalogDoesNotConverge_FailsBeforeRoleAssignment()
    {
        var permissionService = new RecordingPermissionService(converges: false);
        var roleService = new RecordingRoleService();
        var seeder = CreateSeeder(permissionService, roleService);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            seeder.SeedAsync(
            [
                Permission("Organization.Person.View"),
                Permission("Organization.Person.Update")
            ]));

        Assert.Contains("AUTH_LOCAL_ADMIN_CATALOG_MISMATCH", exception.Message, StringComparison.Ordinal);
        Assert.Empty(roleService.AssignedPermissionCodes);
        Assert.Empty(roleService.AssignedRoleCodes);
    }

    [Fact]
    public async Task SeedAsync_WhenRolePermissionAssignmentDoesNotConverge_FailsBeforeUserRoleAssignment()
    {
        var permissionService = new RecordingPermissionService(converges: true);
        var roleService = new RecordingRoleService(rolePermissionsConverge: false);
        var seeder = CreateSeeder(permissionService, roleService);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            seeder.SeedAsync(
            [
                Permission("Organization.Person.View"),
                Permission("Organization.Person.Update")
            ]));

        Assert.Contains("AUTH_LOCAL_ADMIN_CATALOG_MISMATCH", exception.Message, StringComparison.Ordinal);
        Assert.Empty(roleService.AssignedRoleCodes);
    }

    [Fact]
    public async Task SeedAsync_WhenUserRoleAssignmentDoesNotConverge_Fails()
    {
        var permissionService = new RecordingPermissionService(converges: true);
        var roleService = new RecordingRoleService(userRolesConverge: false);
        var seeder = CreateSeeder(permissionService, roleService);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            seeder.SeedAsync([Permission("Organization.Person.View")]));

        Assert.Contains("AUTH_LOCAL_ADMIN_CATALOG_MISMATCH", exception.Message, StringComparison.Ordinal);
    }

    private static LocalAuthSeeder CreateSeeder(
        IPermissionService permissionService,
        IRoleService roleService) =>
        new(
            permissionService,
            roleService,
            Options.Create(new LocalAuthOptions()),
            NullLogger<LocalAuthSeeder>.Instance);

    private static PermissionDescriptor Permission(string code) => new()
    {
        Code = code,
        DisplayName = code,
        ModuleId = "Organization"
    };

    private sealed class RecordingPermissionService : IPermissionService
    {
        private readonly bool _converges;

        public RecordingPermissionService(bool converges)
        {
            _converges = converges;
        }

        public IReadOnlyList<PermissionDescriptor> Synchronized { get; private set; } = [];

        public Task<List<PermissionDto>> GetAllPermissionsAsync(CancellationToken cancellationToken = default)
        {
            var synchronized = _converges ? Synchronized : Synchronized.Take(1).ToArray();
            return Task.FromResult(synchronized.Select(static descriptor => new PermissionDto
            {
                Id = descriptor.Code,
                Code = descriptor.Code,
                DisplayName = descriptor.DisplayName,
                ModuleId = descriptor.ModuleId
            }).ToList());
        }

        public Task<List<PermissionGroupDto>> GetPermissionsByModuleAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SyncPermissionsAsync(
            IEnumerable<PermissionDescriptor> descriptors,
            CancellationToken cancellationToken = default)
        {
            Synchronized = descriptors.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRoleService : IRoleService
    {
        private readonly bool _rolePermissionsConverge;
        private readonly bool _userRolesConverge;

        public RecordingRoleService(
            bool rolePermissionsConverge = true,
            bool userRolesConverge = true)
        {
            _rolePermissionsConverge = rolePermissionsConverge;
            _userRolesConverge = userRolesConverge;
        }

        public IReadOnlyList<string> AssignedPermissionCodes { get; private set; } = [];

        public IReadOnlyList<string> AssignedRoleCodes { get; private set; } = [];

        public Task<RoleDto?> GetRoleByCodeAsync(
            string code,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RoleDto?>(new RoleDto
            {
                Id = "role-local-admin",
                Code = code,
                DisplayName = "Local Administrator"
            });

        public Task AssignRolePermissionsAsync(
            string roleId,
            AssignRolePermissionsRequest request,
            string? grantedBy = null,
            CancellationToken cancellationToken = default)
        {
            AssignedPermissionCodes = request.PermissionCodes.ToArray();
            return Task.CompletedTask;
        }

        public Task AssignUserRolesAsync(
            string userId,
            AssignUserRolesRequest request,
            string? assignedBy = null,
            CancellationToken cancellationToken = default)
        {
            AssignedRoleCodes = request.RoleCodes.ToArray();
            return Task.CompletedTask;
        }

        public Task<List<RoleDto>> GetAllRolesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RoleDto?> GetRoleByIdAsync(
            string roleId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RoleDto> CreateRoleAsync(
            RoleDto dto,
            string? createdBy = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RoleDto> UpdateRoleAsync(
            string roleId,
            RoleDto dto,
            string? updatedBy = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteRoleAsync(
            string roleId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RolePermissionsDto> GetRolePermissionsAsync(
            string roleId,
            CancellationToken cancellationToken = default)
        {
            var permissionCodes = _rolePermissionsConverge
                ? AssignedPermissionCodes
                : AssignedPermissionCodes.Take(1).ToArray();
            return Task.FromResult(new RolePermissionsDto
            {
                RoleId = roleId,
                PermissionCodes = permissionCodes.ToList()
            });
        }

        public Task<UserRoleAssignmentsDto> GetUserRolesAsync(
            string userId,
            CancellationToken cancellationToken = default)
        {
            var roleCodes = _userRolesConverge
                ? AssignedRoleCodes
                : Array.Empty<string>();
            return Task.FromResult(new UserRoleAssignmentsDto
            {
                UserId = userId,
                RoleCodes = roleCodes.ToList()
            });
        }
    }
}
