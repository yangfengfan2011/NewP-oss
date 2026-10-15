using System.Text.Json;
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Workspace;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Persistence.EfCore.Migrations;
using ICP.Platform.Persistence.EfCore.ProviderTargets;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Persistence.ProviderTargets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
        using var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>()
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().UseSqlServer("Server=invalid;Database=unused;Integrated Security=True").Options, plan);
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
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>().UseSqlServer("Server=invalid;Database=unused;Integrated Security=True").Options;
        // 同一 provider/options 先消费已有真实基础计划，Workspace 仍必须得到自己的模型；不依赖测试发现顺序。
        using var predecessor = new EfPlatformDbContext(options, TestPersistencePlan.CreateCurrentSqlServer());
        Assert.NotNull(predecessor.Model.FindEntityType("Person"));
        Assert.Null(predecessor.Model.FindEntityType("WorkspaceFolder"));
        using var db = new EfPlatformDbContext(options, plan);
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

    [Theory]
    [InlineData("link-kind")]
    [InlineData("entity-kind")]
    [InlineData("parent-field")]
    [InlineData("child-field")]
    [InlineData("discriminator-text")]
    [InlineData("discriminator-reference")]
    [InlineData("ordinal-audit")]
    [InlineData("name-audit")]
    [InlineData("parent-fk-missing")]
    [InlineData("parent-fk-owner")]
    [InlineData("parent-fk-key")]
    [InlineData("parent-fk-model")]
    [InlineData("provider-parent-fk-missing")]
    [InlineData("provider-parent-fk-owner")]
    [InlineData("provider-parent-fk-column")]
    [InlineData("provider-parent-fk-table")]
    [InlineData("provider-parent-fk-key")]
    [InlineData("provider-discriminator-nullable")]
    [InlineData("provider-child-type")]
    [InlineData("provider-ordinal-computed")]
    public void RecoveredRealWorkspaceBackfill_RejectsInvalidStorageDeclarationsAndMappings(string violation)
    {
        var models = JsonSerializer.Deserialize<ModelDefinition[]>(JsonSerializer.Serialize(new WorkspaceModelMetadataProvider().GetDefinitions()))!;
        var plan = new CompiledPersistencePlan("candidate", "snapshot", "module", PersistenceProvider.SqlServer, "digest", [], models, []);
        using var db = new EfPlatformDbContext(new DbContextOptionsBuilder<EfPlatformDbContext>()
            .ReplaceService<IModelCacheKeyFactory, EfPlatformModelCacheKeyFactory>()
            .UseSqlServer("Server=invalid;Database=unused;Integrated Security=True").Options, plan);
        var target = new EfProviderTargetSource(db, plan).Read();
        var link = Assert.Single(models, model => model.Name == "WorkspaceFolderContent");
        Assert.Null(link.LinkQuery);
        Assert.Empty(link.LinkedTypes);
        var entity = Assert.Single(models, model => model.Name == "WorkspaceFolder");
        var parentKey = Assert.Single(link.ForeignKeys);
        link = violation switch
        {
            "link-kind" => link with { Kind = ModelKind.Entity },
            "parent-field" => link with { Fields = link.Fields.Select(field => field.Name == "PEID" ? field with { ValueType = FieldValueType.Text } : field).ToArray() },
            "child-field" => link with { Fields = link.Fields.Select(field => field.Name == "CEID" ? field with { Kind = FieldKind.Scalar } : field).ToArray() },
            // 同一物理字符串列去掉稳定 ordinal 身份后，只是普通必填文本，不能充当 discriminator。
            "discriminator-text" => link with { Fields = link.Fields.Select(field => field.Name == "ChildModelKey" ? field with { OrdinalKey = false } : field).ToArray() },
            "discriminator-reference" => link with { Fields = link.Fields.Select(field => field.Name == "ChildModelKey" ? field with { Kind = FieldKind.Reference } : field).ToArray() },
            "ordinal-audit" => link with { Fields = link.Fields.Select(field => field.Name == "TreeOrdinal" ? field with { Kind = FieldKind.Audit, IsAuditField = true } : field).ToArray() },
            "parent-fk-missing" => link with { ForeignKeys = [] },
            "parent-fk-owner" => link with { ForeignKeys = [parentKey with { PrincipalOwnerModuleId = "Other" }] },
            "parent-fk-key" => link with { ForeignKeys = [parentKey with { PrincipalFields = ["ENAME"] }] },
            "parent-fk-model" => link with { ForeignKeys = [parentKey with { PrincipalModelKey = "WorkspaceSpace" }] },
            _ => link
        };
        entity = violation switch
        {
            "entity-kind" => entity with { Kind = ModelKind.Link },
            "name-audit" => entity with { Fields = entity.Fields.Select(field => field.Name == "ENAME" ? field with { Kind = FieldKind.Audit, IsAuditField = true } : field).ToArray() },
            _ => entity
        };
        plan = plan with { Models = models.Select(model => model.Name == link.Name ? link : model.Name == entity.Name ? entity : model).ToArray() };
        var source = Assert.Single(target.Tables, table => table.Name == link.TableName);
        var providerKey = Assert.Single(source.ForeignKeys);
        var modifiedKey = violation switch
        {
            "provider-parent-fk-owner" => providerKey with { PrincipalOwnerModuleId = "Other" },
            "provider-parent-fk-column" => providerKey with { Columns = ["CEID"] },
            "provider-parent-fk-table" => providerKey with { PrincipalTable = "WorkspaceSpace" },
            "provider-parent-fk-key" => providerKey with { PrincipalColumns = ["ENAME"] },
            _ => providerKey
        };
        source = source with
        {
            ForeignKeys = violation == "provider-parent-fk-missing" ? [] : [modifiedKey],
            Columns = source.Columns.Select(column => (violation, column.Name) switch
            {
                ("provider-discriminator-nullable", "ChildModelKey") => column with { IsNullable = true },
                ("provider-child-type", "CEID") => column with { ClrType = typeof(long).FullName! },
                ("provider-ordinal-computed", "TreeOrdinal") => column with { ComputedColumnSql = "1" },
                _ => column
            }).ToArray()
        };
        target = target with { Tables = target.Tables.Select(table => table.Name == source.Name ? source : table).ToArray() };
        const string id = "20261014000001_Workspace_000005_TreeOrderBackfill";
        var step = new CompiledMigrationStep("000005", id, null, new string('a', 64), new string('b', 64), new string('c', 64), "data-backfill");
        var stream = new ModuleMigrationStream("Workspace", PersistenceProvider.SqlServer, "unused", "unused", "bundle", "model", id, false, [step]);
        var builder = new MigrationBuilder("SqlServer");
        builder.BackfillRelationOrdinal("Workspace", "WorkspaceFolderContent", "TreeOrdinal", "PEID", "LID", "CEID", "ChildModelKey", "WorkspaceFolder", "WorkspaceFolder", "EID", "ENAME");
        var migration = new OperationsMigration(builder.Operations);
        Assert.Throws<ProviderOperationValidationException>(() => new ProviderMigrationOperationValidator(plan, target).Validate(stream, [new(step, migration)]));
    }

    private sealed class OperationsMigration(IReadOnlyList<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations) : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Operations.AddRange(operations);
        protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException();
    }
}
