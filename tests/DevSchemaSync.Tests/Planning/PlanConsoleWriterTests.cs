using ICP.Platform.Tools.DevSchemaSync.Planning;
using Xunit;

namespace ICP.Platform.DevSchemaSync.Tests.Planning;

public sealed class PlanConsoleWriterTests
{
    [Fact]
    public void Write_IncludesRequiredSections()
    {
        var plan = new SchemaPlan(
            new SchemaPlanSummary(
                ManagedTableCount: 1,
                OperationCount: 1,
                SafeOperationCount: 1,
                ConfirmOperationCount: 0,
                UnmanagedTableCount: 1),
            ["dbo.ConfigItem"],
            [
                new SchemaOperation(
                    SchemaOperationKind.CreateTable,
                    RiskLevel.Safe,
                    "ConfigItem",
                    null,
                    null,
                    "创建表 ConfigItem。",
                    [])
            ],
            ["dbo.legacy_table"]);

        var writer = new StringWriter();

        PlanConsoleWriter.Write(writer, plan);

        var output = writer.ToString();
        Assert.Contains("Summary", output, StringComparison.Ordinal);
        Assert.Contains("Managed Objects", output, StringComparison.Ordinal);
        Assert.Contains("Planned Operations", output, StringComparison.Ordinal);
        Assert.Contains("Unmanaged Objects Summary", output, StringComparison.Ordinal);
    }
}
