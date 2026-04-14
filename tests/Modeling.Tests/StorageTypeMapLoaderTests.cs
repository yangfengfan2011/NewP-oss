using ICP.Platform.Modeling.Metadata;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class StorageTypeMapLoaderTests
{
    [Fact]
    public void Load_ParsesBuiltInStorageTypes()
    {
        var loader = new JsonStorageTypeMapLoader();
        var path = Path.Combine(
            FindRepositoryRoot(),
            "server",
            "platform-modeling",
            "metadata",
            "storage-types.json");

        var document = loader.Load(path);

        Assert.True(document.Types.ContainsKey("datetime"));
        Assert.True(document.Types["datetime"].Providers.ContainsKey("SqlServer"));
        Assert.True(document.Types["datetime"].Providers.ContainsKey("Oracle"));
        Assert.True(document.Types["datetime"].Providers.ContainsKey("MySql"));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "server")) &&
                Directory.Exists(Path.Combine(current.FullName, "tests")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("找不到仓库根目录。");
    }
}
