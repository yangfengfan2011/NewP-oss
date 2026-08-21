using System.Reflection;
using ICP.Platform.Authorization.Http;
using ICP.Platform.Modules.Example.Controllers;
using ICP.Platform.Modules.Example.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ICP.Platform.Modules.Example.Tests;

public sealed class RuntimeWorkloadServiceTests
{
    [Fact]
    public void GetColumns_UsesStableSemanticKeysAndTitlesForTheCurrentAndNextWeek()
    {
        var service = new RuntimeWorkloadService(new FixedTimeProvider(new DateTimeOffset(2026, 8, 19, 8, 0, 0, TimeSpan.Zero)));

        var result = service.GetColumns();

        Assert.Equal("runtime-workload:2026-08-17", result.ContextToken);
        Assert.Equal(15, result.Columns.Count);
        Assert.Equal(("member", "memberName", "成员"),
            (result.Columns[0].Key, result.Columns[0].Field, result.Columns[0].Title));
        Assert.Equal(("current-week.day-1", "currentWeekDay1", "8月17日"),
            (result.Columns[1].Key, result.Columns[1].Field, result.Columns[1].Title));
        Assert.Equal(("next-week.day-7", "nextWeekDay7", "8月30日"),
            (result.Columns[^1].Key, result.Columns[^1].Field, result.Columns[^1].Title));
    }

    [Fact]
    public void GetRows_ComputesRowsFromTheOpaqueColumnContextWithoutASecondListEntry()
    {
        var service = new RuntimeWorkloadService(new FixedTimeProvider(new DateTimeOffset(2026, 8, 19, 8, 0, 0, TimeSpan.Zero)));

        var result = service.GetRows("runtime-workload:2026-08-17", keyword: "林", pageIndex: 1, pageSize: 20);

        var row = Assert.Single(result.Items);
        Assert.Equal("member-2", row["id"]);
        Assert.Equal("林示例", row["memberName"]);
        Assert.Equal(2, row["currentWeekDay1"]);
        Assert.Equal(6, row["nextWeekDay7"]);
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public void GetRows_RejectsAnInvalidColumnContextToken()
    {
        var service = new RuntimeWorkloadService(new FixedTimeProvider(DateTimeOffset.UtcNow));

        var error = Assert.Throws<ArgumentException>(() => service.GetRows("not-a-runtime-token", null, 1, 20));

        Assert.Equal("contextToken", error.ParamName);
    }

    [Fact]
    public void GetRows_ControllerBindsTheOpaqueContextFromTheModulePrivateHeader()
    {
        var method = typeof(RuntimeWorkloadController).GetMethod(nameof(RuntimeWorkloadController.GetRows));
        var parameter = Assert.Single(method!.GetParameters(), item => item.Name == "contextToken");

        var binding = parameter.GetCustomAttribute<FromHeaderAttribute>();

        Assert.NotNull(binding);
        Assert.Equal("X-Example-Column-Context", binding.Name);
    }

    [Fact]
    public void Controller_DeclaresAnEndpointOwnerMatchingItsPermissionNamespace()
    {
        var controller = typeof(RuntimeWorkloadController);

        var owner = controller.GetCustomAttribute<EndpointModuleAttribute>();
        var permission = controller.GetCustomAttribute<RequirePermissionAttribute>();

        Assert.NotNull(owner);
        Assert.NotNull(permission);
        Assert.Equal("Example", owner.ModuleId);
        Assert.Equal("Example.View", permission.PermissionCode);
        Assert.StartsWith($"{owner.ModuleId}.", permission.PermissionCode, StringComparison.Ordinal);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
