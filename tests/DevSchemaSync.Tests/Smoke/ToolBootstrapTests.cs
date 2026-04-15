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
        Assert.Contains("Provider: SqlServer", output, StringComparison.Ordinal);
        Assert.Contains("HostApiRoot:", output, StringComparison.Ordinal);
        Assert.Contains("{\"status\":\"ok\"}", output, StringComparison.Ordinal);
        Assert.True(output.IndexOf("Provider: SqlServer", StringComparison.Ordinal) < output.IndexOf("{\"status\":\"ok\"}", StringComparison.Ordinal));
    }
}
