using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class AcceptanceResourceRootTests : IDisposable
{
    private readonly string _runDirectory =
        Directory.CreateTempSubdirectory("newp-resource-root-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_runDirectory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // 测试自身已清理时忽略。
        }
    }

    private string CreateResource(string name)
    {
        var path = Path.Combine(_runDirectory, "resources", name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void EnsureOwnedResourceRoot_AcceptsDirectoryInsideTheRunResources()
    {
        var plugins = CreateResource("plugins");

        AcceptanceInfrastructure.EnsureOwnedResourceRoot(_runDirectory, plugins);
    }

    [Fact]
    public void EnsureOwnedResourceRoot_RejectsPathOutsideTheRunDirectory()
    {
        var outside = Directory.CreateTempSubdirectory("newp-outside-").FullName;
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => AcceptanceInfrastructure.EnsureOwnedResourceRoot(_runDirectory, outside));

            Assert.Contains("escaped", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void EnsureOwnedResourceRoot_RejectsTraversalThatResolvesOutsideTheRunDirectory()
    {
        var traversal = Path.Combine(_runDirectory, "resources", "..", "..", "elsewhere");

        Assert.Throws<InvalidOperationException>(
            () => AcceptanceInfrastructure.EnsureOwnedResourceRoot(_runDirectory, traversal));
    }

    [Fact]
    public void EnsureOwnedResourceRoot_RejectsRootContainingASymbolicLink()
    {
        var plugins = CreateResource("plugins");
        var target = CreateResource("link-target");
        var link = Path.Combine(plugins, "linked");
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (UnauthorizedAccessException)
        {
            return; // 未授予创建符号链接权限时跳过；边界本身由上面的用例覆盖。
        }
        catch (IOException)
        {
            return;
        }

        var exception = Assert.Throws<InvalidOperationException>(
            () => AcceptanceInfrastructure.EnsureOwnedResourceRoot(_runDirectory, plugins));

        Assert.Contains("reparse point", exception.Message, StringComparison.Ordinal);
    }
}
