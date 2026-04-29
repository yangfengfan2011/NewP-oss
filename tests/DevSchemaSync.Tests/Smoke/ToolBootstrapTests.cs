using System.Text.Json;
using ICP.Platform.Tools.DevSchemaSync;
using ICP.Platform.Tools.DevSchemaSync.Configuration;
using ICP.Platform.Persistence.DevSync.Coordination;
using ICP.Platform.Persistence.DevSync.Execution;
using ICP.Platform.Persistence.DevSync.Planning;
using Xunit;

namespace ICP.Platform.DevSchemaSync.Tests.Smoke;

public class ToolBootstrapTests
{
    [Fact]
    public async Task DevSchemaSyncProgram_JsonMode_WhenCoordinatorAllowsStartup_ExitsZero()
    {
        var writer = new StringWriter();
        var repositoryRoot = Path.Combine(Path.GetTempPath(), "dev-schema-sync-fake-repo");
        var context = new DevSchemaSyncExecutionContext(
            "SqlServer",
            "Server=(local);Database=fake;",
            repositoryRoot,
            Path.Combine(repositoryRoot, "apps", "host-api"));
        using var _ = DevSchemaSyncCliBootstrap.OverrideCoordinatorFactoryForTests(_ =>
            DevelopmentSchemaSyncCoordinator.CreateForTests(
                ModelSourceInventory.Success(["Role"]),
                signaturesMatch: true,
                new SchemaPlan(
                    new SchemaPlanSummary(1, 0, 0, 0, 0),
                    ["Role"],
                    [],
                    [])));

        var exitCode = await DevSchemaSyncProgram.RunAsync(["--json"], context, writer);

        var output = writer.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("\"summary\"", output, StringComparison.Ordinal);
        Assert.Contains("\"managedObjects\"", output, StringComparison.Ordinal);
        Assert.Contains("\"operations\"", output, StringComparison.Ordinal);
        Assert.Contains("\"unmanagedTables\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DevSchemaSyncProgram_JsonMode_WhenCoordinatorBlocksStartup_ReturnsBlockedExitCode()
    {
        var writer = new StringWriter();
        var repositoryRoot = Path.Combine(Path.GetTempPath(), "dev-schema-sync-fake-repo");
        var context = new DevSchemaSyncExecutionContext(
            "SqlServer",
            "Server=(local);Database=fake;",
            repositoryRoot,
            Path.Combine(repositoryRoot, "apps", "host-api"));
        using var _ = DevSchemaSyncCliBootstrap.OverrideCoordinatorFactoryForTests(_ =>
            DevelopmentSchemaSyncCoordinator.CreateForTests(
                ModelSourceInventory.Success(["Role"]),
                signaturesMatch: true,
                new SchemaPlan(
                    new SchemaPlanSummary(1, 1, 0, 1, 0),
                    ["Role"],
                    [new SchemaOperation("DropColumn", "Confirm", "Role", "Description", null, "drop Description", [])],
                    [])));

        var exitCode = await DevSchemaSyncProgram.RunAsync(["--json"], context, writer);

        Assert.Equal(3, exitCode);
        Assert.Contains("\"confirmOperationCount\": 1", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DevSchemaSyncProgram_JsonMode_WhenCoordinatorBlocksBeforePlan_WritesFailureEnvelope()
    {
        var writer = new StringWriter();
        var repositoryRoot = Path.Combine(Path.GetTempPath(), "dev-schema-sync-fake-repo");
        var context = new DevSchemaSyncExecutionContext(
            "SqlServer",
            "Server=(local);Database=fake;",
            repositoryRoot,
            Path.Combine(repositoryRoot, "apps", "host-api"));
        using var _ = DevSchemaSyncCliBootstrap.OverrideCoordinatorFactoryForTests(_ =>
            DevelopmentSchemaSyncCoordinator.CreateForTests(
                ModelSourceInventory.Success(["Role"]),
                signaturesMatch: false,
                new SchemaPlan(
                    new SchemaPlanSummary(1, 0, 0, 0, 0),
                    ["Role"],
                    [],
                    [])));

        var exitCode = await DevSchemaSyncProgram.RunAsync(["--json"], context, writer);
        var output = writer.ToString();

        Assert.Equal(3, exitCode);
        Assert.Contains("\"startupAllowed\": false", output, StringComparison.Ordinal);
        Assert.Contains("\"failureReason\": \"BaseDtoSignaturesMismatch\"", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\"plan\": null", output, StringComparison.Ordinal);
        Assert.Contains("\"plan\": {", output, StringComparison.Ordinal);
        Assert.Contains("\"operationCount\": 0", output, StringComparison.Ordinal);
        Assert.Contains("\"missingModels\": [", output, StringComparison.Ordinal);
        Assert.Contains("\"signatureMismatches\": [", output, StringComparison.Ordinal);
        Assert.Contains("\"diagnostics\": [", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DevSchemaSyncProgram_ApplyMode_WhenUserRejectsConfirmation_WritesPreviewBeforePromptAndDoesNotExecute()
    {
        var writer = new StringWriter();
        var reader = new StringReader("n");
        var applyCalled = false;
        var repositoryRoot = Path.Combine(Path.GetTempPath(), "dev-schema-sync-fake-repo");
        var context = new DevSchemaSyncExecutionContext(
            "SqlServer",
            "Server=(local);Database=fake;",
            repositoryRoot,
            Path.Combine(repositoryRoot, "apps", "host-api"));
        using var _ = DevSchemaSyncCliBootstrap.OverrideCoordinatorFactoryForTests(options =>
            DevelopmentSchemaSyncCoordinator.CreateForTests(
                ModelSourceInventory.Success(["Role"]),
                signaturesMatch: true,
                new SchemaPlan(
                    new SchemaPlanSummary(1, 1, 1, 0, 0),
                    ["Role"],
                    [new SchemaOperation("AddColumn", "Safe", "Role", "Description", null, "add Description", ["ALTER TABLE [Role] ADD [Description] nvarchar(256) NULL"])],
                    []),
                applyConfirmation: options.ConfirmApply,
                applyAsync: (_, _) =>
                {
                    applyCalled = true;
                    return Task.FromResult(new ApplyResult(true, false, 1, 1));
                }));

        var exitCode = await DevSchemaSyncProgram.RunAsync(["--apply"], context, reader, writer);
        var output = writer.ToString();

        var summaryIndex = output.IndexOf("Summary", StringComparison.Ordinal);
        var sqlPreviewIndex = output.IndexOf("SQL Preview", StringComparison.Ordinal);
        var promptIndex = output.IndexOf("是否继续执行本次同步计划", StringComparison.Ordinal);

        Assert.Equal(4, exitCode);
        Assert.False(applyCalled);
        Assert.NotEqual(-1, summaryIndex);
        Assert.NotEqual(-1, sqlPreviewIndex);
        Assert.NotEqual(-1, promptIndex);
        Assert.True(summaryIndex < sqlPreviewIndex);
        Assert.True(sqlPreviewIndex < promptIndex);
        Assert.Contains("ALTER TABLE [Role] ADD [Description] nvarchar(256) NULL", output, StringComparison.Ordinal);
        Assert.Contains("结构同步执行已取消", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DevSchemaSyncProgram_SqlApplyMode_WhenUserRejectsConfirmation_WritesPreviewBeforePromptAndDoesNotExecute()
    {
        var writer = new StringWriter();
        var reader = new StringReader("n");
        var applyCalled = false;
        var repositoryRoot = Path.Combine(Path.GetTempPath(), "dev-schema-sync-fake-repo");
        var context = new DevSchemaSyncExecutionContext(
            "SqlServer",
            "Server=(local);Database=fake;",
            repositoryRoot,
            Path.Combine(repositoryRoot, "apps", "host-api"));
        using var _ = DevSchemaSyncCliBootstrap.OverrideCoordinatorFactoryForTests(options =>
            DevelopmentSchemaSyncCoordinator.CreateForTests(
                ModelSourceInventory.Success(["Role"]),
                signaturesMatch: true,
                new SchemaPlan(
                    new SchemaPlanSummary(1, 1, 1, 0, 0),
                    ["Role"],
                    [new SchemaOperation("AddColumn", "Safe", "Role", "Description", null, "add Description", ["ALTER TABLE [Role] ADD [Description] nvarchar(256) NULL"])],
                    []),
                applyConfirmation: options.ConfirmApply,
                applyAsync: (_, _) =>
                {
                    applyCalled = true;
                    return Task.FromResult(new ApplyResult(true, false, 1, 1));
                }));

        var exitCode = await DevSchemaSyncProgram.RunAsync(["--sql", "--apply"], context, reader, writer);
        var output = writer.ToString();

        var summaryIndex = output.IndexOf("Summary", StringComparison.Ordinal);
        var sqlPreviewIndex = output.IndexOf("SQL Preview", StringComparison.Ordinal);
        var promptIndex = output.IndexOf("是否继续执行本次同步计划", StringComparison.Ordinal);

        Assert.Equal(4, exitCode);
        Assert.False(applyCalled);
        Assert.NotEqual(-1, summaryIndex);
        Assert.NotEqual(-1, sqlPreviewIndex);
        Assert.NotEqual(-1, promptIndex);
        Assert.True(summaryIndex < sqlPreviewIndex);
        Assert.True(sqlPreviewIndex < promptIndex);
    }

    [Fact]
    public async Task DevSchemaSyncProgram_JsonApplyMode_ReturnsJsonFailureWithoutPromptOrExecution()
    {
        var writer = new StringWriter();
        var reader = new StringReader("y");
        var applyCalled = false;
        var repositoryRoot = Path.Combine(Path.GetTempPath(), "dev-schema-sync-fake-repo");
        var context = new DevSchemaSyncExecutionContext(
            "SqlServer",
            "Server=(local);Database=fake;",
            repositoryRoot,
            Path.Combine(repositoryRoot, "apps", "host-api"));
        using var _ = DevSchemaSyncCliBootstrap.OverrideCoordinatorFactoryForTests(options =>
            DevelopmentSchemaSyncCoordinator.CreateForTests(
                ModelSourceInventory.Success(["Role"]),
                signaturesMatch: true,
                new SchemaPlan(
                    new SchemaPlanSummary(1, 1, 1, 0, 0),
                    ["Role"],
                    [new SchemaOperation("AddColumn", "Safe", "Role", "Description", null, "add Description", ["ALTER TABLE [Role] ADD [Description] nvarchar(256) NULL"])],
                    []),
                applyConfirmation: options.ConfirmApply,
                applyAsync: (_, _) =>
                {
                    applyCalled = true;
                    return Task.FromResult(new ApplyResult(true, false, 1, 1));
                }));

        var exitCode = await DevSchemaSyncProgram.RunAsync(["--json", "--apply"], context, reader, writer);
        var output = writer.ToString();
        using var document = JsonDocument.Parse(output);

        Assert.Equal(3, exitCode);
        Assert.False(applyCalled);
        Assert.False(document.RootElement.GetProperty("startupAllowed").GetBoolean());
        Assert.Equal("JsonApplyNotSupported", document.RootElement.GetProperty("failureReason").GetString());
        Assert.DoesNotContain("是否继续执行本次同步计划", output, StringComparison.Ordinal);
    }
}
