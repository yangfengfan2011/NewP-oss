using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using ICP.Platform.Modules.ConfigCenter.Persistence.Ef;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class EfConfigRepositoryTests
{
    [Fact]
    public async Task GetByKeyAsync_ReturnsTenantAndEnvironmentMatchFirst()
    {
        var repository = CreateRepository();

        await repository.CreateAsync(new ConfigItem
        {
            Key = "Test.Key",
            Value = "global",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "Global"
        });

        await repository.CreateAsync(new ConfigItem
        {
            Key = "Test.Key",
            Value = "tenant-prod",
            Type = "String",
            ModuleId = "Test",
            DisplayName = "Specific",
            Environment = "Prod",
            TenantId = "tenant-a"
        });

        var item = await repository.GetByKeyAsync("Test.Key", "Prod", "tenant-a");

        Assert.NotNull(item);
        Assert.Equal("tenant-prod", item!.Value);
    }

    [Fact]
    public void MigrationSnapshots_UseMetadataEntityPrimaryKey_ForConfigCenterModels()
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

        foreach (var snapshot in new[] { sqlServerSnapshot, mySqlSnapshot, oracleSnapshot })
        {
            AssertModelPrimaryKey(snapshot, "ConfigItem", "EID");
            AssertModelPrimaryKey(snapshot, "ValueListItem", "EID");
        }
    }

    private static EfConfigRepository CreateRepository()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dbContext = new EfPlatformDbContext(options, ConfigCenterModelDefinitions.All);
        return new EfConfigRepository(dbContext);
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
