using ICP.Platform.Modeling;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Migrations;
using ICP.Platform.Persistence.EfCore.ProviderTargets;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Persistence.ProviderTargets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class RelationOrdinalBackfillTests
{
    [Fact]
    public void TransitionV1_EmptyAndExistingScalarBackfillKeepTheirFrozenDigests()
    {
        var target = new ProviderTargetSnapshot(ProviderTargetSchemaVersions.TargetV1, PersistenceProvider.SqlServer, []);
        var empty = new ProviderTransitionSnapshot(ProviderTargetSchemaVersions.TransitionV1, "Workspace", "legacy", target, target);
        Assert.Equal("534fa3d5af941b7e39ce961612d0b91ad47637d19a3c6e709b44d962b22fac36", ProviderTargetDigest.Compute(empty));
        var scalar = empty with { RequiredFieldBackfills = [new("Workspace", "dbo", "Legacy", "Count", "System.Int64", "1")] };
        Assert.Equal("5f0d5143b665496ca30a055ef27e5273cc7fee8d68b9e3e445b11e77b160e2f9", ProviderTargetDigest.Compute(scalar));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("table")]
    [InlineData("target")]
    [InlineData("parent")]
    [InlineData("type")]
    [InlineData("child")]
    [InlineData("ordinal")]
    [InlineData("name")]
    [InlineData("key")]
    [InlineData("step")]
    public void RealWorkspaceBackfill_RejectsUnownedUnmappedOrNonRankingInputs(string violation)
    {
        var models = new WorkspaceModelMetadataProvider().GetDefinitions();
        var plan = new CompiledPersistencePlan("candidate", "snapshot", "module", PersistenceProvider.SqlServer, "digest", [], models, []);
        using var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>().UseSqlServer("Server=invalid;Database=unused;Integrated Security=True").Options, plan);
        var target = new EfProviderTargetSource(db, plan).Read();
        const string id = "20261014000001_Workspace_000005_TreeOrderBackfill";
        var step = new CompiledMigrationStep("000005", id, null, new string('a', 64), new string('b', 64), new string('c', 64), violation == "step" ? "safe-expand" : "data-backfill");
        var stream = new ModuleMigrationStream("Workspace", PersistenceProvider.SqlServer, "unused", "unused", "bundle", "model", id, false, [step]);
        var fact = new ProviderRelationOrdinalBackfillFact("Workspace", "dbo", "WorkspaceFolderContent", "TreeOrdinal", "PEID", "LID", "CEID", "ChildModelKey", "WorkspaceFolder", "WorkspaceFolder", "EID", "ENAME");
        fact = violation switch
        {
            "owner" => fact with { OwnerModuleId = "Other" }, "table" => fact with { Table = "OtherTable" },
            "target" => fact with { TargetTable = "WorkspaceSpace" }, "parent" => fact with { ParentColumn = "CEID" },
            "type" => fact with { TypeColumn = "PEID" }, "child" => fact with { ChildModelKey = "Part" },
            "ordinal" => fact with { Column = "LUpdatedAt" }, "name" => fact with { TargetNameColumn = "Description" },
            "key" => fact with { LinkIdColumn = "CEID" }, _ => fact
        };
        var migration = new OperationsMigration([new RelationOrdinalBackfillOperation { Fact = fact }]);
        Assert.Throws<ProviderOperationValidationException>(() => new ProviderMigrationOperationValidator(plan, target).Validate(stream, [new(step, migration)]));
    }

    [Fact]
    public void RealWorkspaceBackfill_ClosesOwnerTargetsAndSignsAllInputs()
    {
        var models = new WorkspaceModelMetadataProvider().GetDefinitions();
        var plan = new CompiledPersistencePlan("candidate", "snapshot", "module", PersistenceProvider.SqlServer, "digest", [], models, []);
        using var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>().UseSqlServer("Server=invalid;Database=unused;Integrated Security=True").Options, plan);
        var target = new EfProviderTargetSource(db, plan).Read();
        const string id = "20261014000001_Workspace_000005_TreeOrderBackfill";
        var step = new CompiledMigrationStep("000005", id, null, new string('a', 64), new string('b', 64), new string('c', 64), "data-backfill");
        var stream = new ModuleMigrationStream("Workspace", PersistenceProvider.SqlServer, "unused", "unused", "bundle", "model", id, false, [step]);
        var builder = new MigrationBuilder("SqlServer");
        builder.BackfillRelationOrdinal("Workspace", "WorkspaceFolderContent", "TreeOrdinal", "PEID", "LID", "CEID", "ChildModelKey", "WorkspaceFolder", "WorkspaceFolder", "EID", "ENAME");
        var migration = new OperationsMigration(builder.Operations);
        var result = Assert.Single(new ProviderMigrationOperationValidator(plan, target).Validate(stream, [new(step, migration)]));
        var fact = Assert.Single(result.Transition.RelationOrdinalBackfills);
        Assert.Equal("WorkspaceFolder", fact.ChildModelKey);
        Assert.Equal("ENAME", fact.TargetNameColumn);
        Assert.Equal(result.TransitionDigest, ProviderTargetDigest.Compute(result.Transition));
        Assert.NotEqual(result.TransitionDigest, ProviderTargetDigest.Compute(result.Transition with
        { RelationOrdinalBackfills = [fact with { TargetNameColumn = "Description" }] }));
    }

    private sealed class OperationsMigration(IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations) : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Operations.AddRange(operations);
        protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException();
    }
}
