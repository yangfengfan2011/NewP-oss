using ICP.Platform.Modeling.Metadata;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class StorageTypeMapLoaderTests
{
    [Fact]
    public void Load_ReturnsAllConfiguredProvidersForDatetime()
    {
        var loader = new JsonStorageTypeMapLoader();

        var document = loader.LoadFromFile(
            Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "..",
                    "..",
                    "server",
                    "platform-modeling",
                    "metadata",
                    "storage-types.json")));

        Assert.True(document.Types.ContainsKey("datetime"));

        var datetime = document.Types["datetime"];

        Assert.True(datetime.Providers.ContainsKey("SqlServer"));
        Assert.True(datetime.Providers.ContainsKey("Oracle"));
        Assert.True(datetime.Providers.ContainsKey("MySql"));
    }
}
