using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.Authorization.Persistence.Ef;
using ICP.Platform.Modules.Authorization.Services;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public class EfAuthorizationRepositoryTests
{
    [Fact]
    public async Task CurrentRoleProjection_UsesEfAssignedEnabledRoles()
    {
        using var dbContext = CreateDbContext();
        var roles = new EfRoleRepository(dbContext);
        var members = new EfUserRoleRepository(dbContext);
        var role = new Role { Code = "Assigned", Name = "角色" };
        await roles.CreateAsync(role);
        await members.CreateBatchAsync([new UserRole { UserId = "actor", RoleId = role.Id }]);
        var service = new AuthorizationService(members, roles);
        Assert.Equal(["Assigned"], await service.GetUserRolesAsync("actor"));
        role.IsEnabled = false;
        await roles.UpdateAsync(role);
        Assert.Empty(await service.GetUserRolesAsync("actor"));
    }

    [Fact]
    public void MigrationSnapshots_UsePascalCaseTableAndColumnNames_ForAuthorizationModels()
    {
        var repositoryRoot = GetRepositoryRoot();
        var sqlServerSnapshot = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "server",
            "platform-persistence-efcore.migrations.sqlserver",
            "Migrations",
            "EfPlatformDbContextModelSnapshot.cs"));
        var mySqlSnapshot = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "server",
            "platform-persistence-efcore.migrations.mysql",
            "Migrations",
            "EfPlatformDbContextModelSnapshot.cs"));
        var oracleSnapshot = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "server",
            "platform-persistence-efcore.migrations.oracle",
            "Migrations",
            "EfPlatformDbContextModelSnapshot.cs"));

        Assert.Contains(".HasColumnName(\"ECODE\")", sqlServerSnapshot, StringComparison.Ordinal);

        foreach (var snapshot in new[] { mySqlSnapshot, oracleSnapshot })
        {
            Assert.Contains("b.ToTable(\"Role\"", snapshot, StringComparison.Ordinal);
            Assert.Contains("b.ToTable(\"Permission\"", snapshot, StringComparison.Ordinal);
            Assert.Contains("b.ToTable(\"RolePermission\"", snapshot, StringComparison.Ordinal);
            Assert.Contains("b.ToTable(\"UserRole\"", snapshot, StringComparison.Ordinal);
            Assert.Contains(".HasColumnName(\"Code\")", snapshot, StringComparison.Ordinal);
            Assert.Contains(".HasColumnName(\"RoleId\")", snapshot, StringComparison.Ordinal);
            AssertModelPrimaryKey(snapshot, "Role", "EID");
            AssertModelPrimaryKey(snapshot, "Permission", "EID");
            AssertModelPrimaryKey(snapshot, "RolePermission", "EID");
            AssertModelPrimaryKey(snapshot, "UserRole", "EID");
        }

        Assert.Contains("b.ToTable(\"Role\"", sqlServerSnapshot, StringComparison.Ordinal);
        Assert.Contains("b.ToTable(\"Permission\"", sqlServerSnapshot, StringComparison.Ordinal);
        Assert.Contains(".HasColumnName(\"RoleId\")", sqlServerSnapshot, StringComparison.Ordinal);
        AssertModelPrimaryKey(sqlServerSnapshot, "Role", "EID");
        AssertModelPrimaryKey(sqlServerSnapshot, "Permission", "EID");
        AssertModelPrimaryKey(sqlServerSnapshot, "RolePermission", "EID");
        AssertModelPrimaryKey(sqlServerSnapshot, "UserRole", "EID");
    }

    private static EfPlatformDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new EfPlatformDbContext(options, AuthorizationModelDefinitions.All);
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "server")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }

    private static void AssertModelPrimaryKey(string snapshot, string modelName, string primaryKey)
    {
        var section = ExtractModelSection(snapshot, modelName);

        Assert.Contains($"b.HasKey(\"{primaryKey}\")", section, StringComparison.Ordinal);
        Assert.DoesNotContain("b.HasKey(\"Id\")", section, StringComparison.Ordinal);
    }

    private static string ExtractModelSection(string snapshot, string modelName)
    {
        var marker = $"modelBuilder.Entity(\"{modelName}\", b =>";
        const string nextMarker = "modelBuilder.Entity(\"";

        var start = snapshot.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"快照中缺少 {modelName} 实体定义。");

        var next = snapshot.IndexOf(nextMarker, start + marker.Length, StringComparison.Ordinal);
        return next > start ? snapshot[start..next] : snapshot[start..];
    }
}
