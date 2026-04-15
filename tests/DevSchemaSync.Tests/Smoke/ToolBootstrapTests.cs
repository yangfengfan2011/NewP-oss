using ICP.Platform.Tools.DevSchemaSync;
using Xunit;

namespace ICP.Platform.DevSchemaSync.Tests.Smoke;

public class ToolBootstrapTests
{
    [Fact]
    public async Task DevSchemaSyncProgram_JsonMode_ExitsZero()
    {
        var exitCode = await DevSchemaSyncProgram.RunAsync(["--json"]);

        Assert.Equal(0, exitCode);
    }
}
