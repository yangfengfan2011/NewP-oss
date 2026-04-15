using ICP.Platform.Tools.DevSchemaSync;
using ICP.Platform.Tools.DevSchemaSync.Configuration;
using Xunit;

namespace ICP.Platform.DevSchemaSync.Tests.Smoke;

public class ToolBootstrapTests
{
    [Fact]
    public async Task DevSchemaSyncProgram_JsonMode_ExitsZero()
    {
        var writer = new StringWriter();
        var repositoryRoot = RepositoryRootLocator.Find();
        var configuration = DevSchemaSyncConfigurationBootstrap.BuildConfiguration(repositoryRoot, "Development");
        var context = DevSchemaSyncExecutionContext.FromConfiguration(configuration, repositoryRoot);

        var exitCode = await DevSchemaSyncProgram.RunAsync(["--json"], context, writer);

        var output = writer.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("\"summary\"", output, StringComparison.Ordinal);
        Assert.Contains("\"managedObjects\"", output, StringComparison.Ordinal);
        Assert.Contains("\"operations\"", output, StringComparison.Ordinal);
        Assert.Contains("\"unmanagedTables\"", output, StringComparison.Ordinal);
    }
}
