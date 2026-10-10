using System.Security.Cryptography;
using System.Text.Json;
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.EfCore.ProviderTargets;
using ICP.Platform.Persistence.EfCore.Extensions;
using ICP.Platform.Persistence.EfCore.Migrations;
using ICP.Platform.Persistence.Ownership;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Persistence.ProviderTargets;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public sealed class ProviderTargetTests
{
    [Fact]
    public async Task Read_ProjectsProviderModelWithoutFormDefaultsOrChangingModelDigestV1()
    {
        var plan = TestPersistencePlan.Create();
        var modelDigestV1 = plan.PlanDigest;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Adapter"] = "EfCore",
                ["Persistence:Provider"] = "SqlServer",
                ["ConnectionStrings:SqlServer"] =
                    "Server=invalid.invalid,1;Database=never-connect;User Id=invalid;Password=REDACTED_FOR_OPEN_SOURCE;Connect Timeout=1;TrustServerCertificate=True;"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddEfCorePersistence(
            configuration,
            plan,
            WorktreeDatabaseIdentity.FromGitPaths(@"D:\repo\.git", @"D:\repo\.git"));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var target = scope.ServiceProvider.GetRequiredService<IProviderTargetSource>().Read();

        Assert.Equal(ProviderTargetSchemaVersions.TargetV1, target.SchemaVersion);
        Assert.Equal(64, ProviderTargetDigest.Compute(target).Length);
        var role = Assert.Single(target.Tables, static table => table.Name == "Role");
        Assert.Equal("Authorization", role.OwnerModuleId);
        Assert.Null(Assert.Single(role.Columns, static column => column.Name == "IsEnabled").DefaultValue);
        Assert.Null(Assert.Single(role.Columns, static column => column.Name == "CreatedAt").DefaultValue);
        Assert.Null(Assert.Single(role.Columns, static column => column.Name == "UpdatedAt").DefaultValue);
        Assert.Equal(modelDigestV1, plan.PlanDigest);
    }

    [Fact]
    public async Task Read_SqlServerOrdersEveryPhysicalTableLikeEfWithoutReorderingCompiledModels()
    {
        var plan = TestPersistencePlan.CreateCurrentSqlServer();
        var originalFieldOrder = plan.Models.ToDictionary(
            static model => model.LogicalKey,
            static model => model.Fields.Select(static field => field.Name).ToArray(),
            StringComparer.Ordinal);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Adapter"] = "EfCore",
                ["Persistence:Provider"] = "SqlServer",
                ["ConnectionStrings:SqlServer"] =
                    "Server=invalid.invalid,1;Database=never-connect;User Id=invalid;Password=REDACTED_FOR_OPEN_SOURCE;Connect Timeout=1;TrustServerCertificate=True;"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddEfCorePersistence(
            configuration,
            plan,
            WorktreeDatabaseIdentity.FromGitPaths(@"D:\repo\.git", @"D:\repo\.git"));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var target = scope.ServiceProvider.GetRequiredService<IProviderTargetSource>().Read();

        foreach (var model in plan.Models)
        {
            var table = Assert.Single(target.Tables, candidate =>
                string.Equals(candidate.OwnerModuleId, model.OwnerModuleId, StringComparison.Ordinal)
                && string.Equals(candidate.Name, model.TableName, StringComparison.Ordinal));
            var expectedProviderOrder = model.Fields
                .OrderByDescending(static field => field.IsPrimaryKey)
                .ThenBy(static field => field.Name, StringComparer.Ordinal)
                .Select(static field => field.Name)
                .Concat(model.Fields.Where(static field => field.OrdinalKey).Select(static field => "__ordinal_" + field.Name))
                .ToArray();

            Assert.Equal(expectedProviderOrder, table.Columns.Select(static column => column.Name));
            Assert.Equal(originalFieldOrder[model.LogicalKey], model.Fields.Select(static field => field.Name));
        }
    }

    [Fact]
    public void Digest_UsesExplicitVersionsAndCanonicalTableOrder()
    {
        var first = Target("B", "Second");
        var second = Target("A", "First");
        var ordered = new ProviderTargetSnapshot(
            ProviderTargetSchemaVersions.TargetV1,
            PersistenceProvider.SqlServer,
            [second, first]);
        var reversed = ordered with { Tables = [first, second] };

        Assert.Equal(ProviderTargetDigest.Compute(ordered), ProviderTargetDigest.Compute(reversed));
        var annotationReordered = ordered with
        {
            Tables = ordered.Tables.Select(table => table with
            {
                Columns = table.Columns.Select(column => column with
                {
                    ProviderAnnotations =
                    [
                        new ProviderAnnotationTarget("SqlServer:Z", "2"),
                        new ProviderAnnotationTarget("SqlServer:A", "1")
                    ]
                }).ToArray()
            }).ToArray()
        };
        var annotationCanonical = annotationReordered with
        {
            Tables = annotationReordered.Tables.Select(table => table with
            {
                Columns = table.Columns.Select(column => column with
                {
                    ProviderAnnotations = column.ProviderAnnotations.Reverse().ToArray()
                }).ToArray()
            }).ToArray()
        };
        Assert.Equal(
            ProviderTargetDigest.Compute(annotationReordered),
            ProviderTargetDigest.Compute(annotationCanonical));

        var transition = new ProviderTransitionSnapshot(
            ProviderTargetSchemaVersions.TransitionV1,
            "Example",
            "20260812000000_Example",
            ordered with { Tables = [second] },
            ordered);
        var legacyTransitionBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            transition.SchemaVersion,
            transition.ModuleId,
            transition.MigrationId,
            transition.From,
            transition.To
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
        var legacyTransitionDigest = Convert.ToHexStringLower(SHA256.HashData(legacyTransitionBytes));
        Assert.Equal(legacyTransitionDigest, ProviderTargetDigest.Compute(transition));
        Assert.Throws<InvalidOperationException>(() => ProviderTargetDigest.Compute(
            ordered with { SchemaVersion = "provider-target/v2" }));
        Assert.Throws<InvalidOperationException>(() => ProviderTargetDigest.Compute(
            transition with { SchemaVersion = "provider-transition/v2" }));
        Assert.Throws<InvalidOperationException>(() => ProviderTargetDigest.Compute(
            transition with { From = transition.From with { SchemaVersion = "provider-target/v2" } }));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("schema")]
    [InlineData("table")]
    [InlineData("column")]
    [InlineData("type")]
    [InlineData("literal")]
    [InlineData("order")]
    public void Digest_RequiredFieldBackfillFactMutationChangesTransitionAndFrozenHistory(
        string mutation)
    {
        var table = Target("Example", "ExampleRecord") with
        {
            Columns = [TargetColumn("Id"), TargetColumn("RecordSource"), TargetColumn("RecordCategory")]
        };
        var target = new ProviderTargetSnapshot(
            ProviderTargetSchemaVersions.TargetV1,
            PersistenceProvider.SqlServer,
            [table]);
        var sourceFact = new ProviderRequiredFieldBackfillFact(
            "Example",
            "dbo",
            "ExampleRecord",
            "RecordSource",
            typeof(string).FullName!,
            "Legacy");
        var secondFact = sourceFact with
        {
            Column = "RecordCategory",
            Literal = "Imported"
        };
        var transition = new ProviderTransitionSnapshot(
            ProviderTargetSchemaVersions.TransitionV1,
            "Example",
            "20260828000200_Example_000004_RecordSourceBackfill",
            target,
            target)
        {
            RequiredFieldBackfills = [sourceFact, secondFact]
        };
        IReadOnlyList<ProviderRequiredFieldBackfillFact> changedFacts = mutation switch
        {
            "owner" => [sourceFact with { OwnerModuleId = "Other" }, secondFact],
            "schema" => [sourceFact with { Schema = "other" }, secondFact],
            "table" => [sourceFact with { Table = "OtherRecord" }, secondFact],
            "column" => [sourceFact with { Column = "OtherSource" }, secondFact],
            "type" => [sourceFact with { ClrType = typeof(int).FullName! }, secondFact],
            "literal" => [sourceFact with { Literal = "Manual" }, secondFact],
            "order" => [secondFact, sourceFact],
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        var changed = transition with { RequiredFieldBackfills = changedFacts };
        var targetDigest = UnifiedMigrationHistoryDigest.ComputeProviderTargetFactDigest(transition);
        var originalTransitionDigest = UnifiedMigrationHistoryDigest.ComputeProviderTransitionFactDigest(transition);
        var changedTransitionDigest = UnifiedMigrationHistoryDigest.ComputeProviderTransitionFactDigest(changed);
        var originalHistory = BindSingleHistory(targetDigest, originalTransitionDigest);
        var changedHistory = BindSingleHistory(targetDigest, changedTransitionDigest);

        Assert.NotEqual(originalTransitionDigest, changedTransitionDigest);
        Assert.NotEqual(originalHistory[0].PlanDigest, changedHistory[0].PlanDigest);
        var exception = Assert.Throws<PersistenceHistoryException>(() =>
            UnifiedMigrationHistoryReconciler.Reconcile(
                PersistenceHistoryVersions.ReconciliationV1,
                originalHistory,
                changedHistory));
        Assert.Equal(PersistenceHistoryFailureCodes.PlanMismatch, exception.FailureCode);
    }

    [Fact]
    public void Validate_RequiredFieldBackfillLiteralPreservesExactTypedValueIdentity()
    {
        var firstValue = new DateTime(2026, 8, 28, 10, 11, 12, DateTimeKind.Utc).AddTicks(1);
        var secondValue = firstValue.AddTicks(1);

        var first = ValidateRequiredFieldBackfillTransition(firstValue);
        var second = ValidateRequiredFieldBackfillTransition(secondValue);

        var firstFact = Assert.Single(first.RequiredFieldBackfills);
        var secondFact = Assert.Single(second.RequiredFieldBackfills);
        Assert.NotEqual(firstFact.Literal, secondFact.Literal);
        Assert.NotEqual(ProviderTargetDigest.Compute(first), ProviderTargetDigest.Compute(second));
    }

    [Fact]
    public void Validate_ExactRenameAlterAndConstraintIndexOperations_ProduceCanonicalTransition()
    {
        var target = TransitionTarget();
        var migration = new ExactTransitionMigration();
        var result = Assert.Single(new ProviderMigrationOperationValidator(Plan(target), target).Validate(
            Stream(ExactTransitionMigration.MigrationId),
            [new ProviderMigrationCandidate(Step(ExactTransitionMigration.MigrationId), migration)]));

        Assert.Equal(ProviderTargetSchemaVersions.TransitionV1, result.Transition.SchemaVersion);
        Assert.Contains(result.Transition.From.Tables, static table => table.Name == "Asset");
        Assert.Contains(result.Transition.To.Tables, static table => table.Name == "RenamedAsset");
        Assert.Equal(64, result.TransitionDigest.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("rename-column")]
    [InlineData("rename-index")]
    public void Validate_RenameColumnAndIndex_ProveDependentFromShape(string? malformedPart)
    {
        var table = Target("Example", "Asset") with
        {
            Columns = [TargetColumn("Id"), TargetColumn("ParentId")],
            UniqueConstraints = [new ProviderKeyTarget("AK_Asset_ParentId", ["ParentId"])],
            ForeignKeys =
            [
                new ProviderForeignKeyTarget(
                    "FK_Asset_Self",
                    ["Id"],
                    "Example",
                    "dbo",
                    "Asset",
                    ["ParentId"],
                    "Restrict")
            ],
            Indexes =
            [
                new ProviderIndexTarget(
                    "IX_Asset_ParentId",
                    ["ParentId"],
                    IsUnique: false,
                    IsDescending: null,
                    Filter: null,
                    ProviderAnnotations: [])
            ]
        };
        var target = new ProviderTargetSnapshot(
            ProviderTargetSchemaVersions.TargetV1,
            PersistenceProvider.SqlServer,
            [table]);
        var migration = new RenameColumnAndIndexMigration(malformedPart);

        if (malformedPart is not null)
        {
            Assert.Throws<ProviderOperationValidationException>(() =>
                new ProviderMigrationOperationValidator(Plan(target), target).Validate(
                    Stream(RenameColumnAndIndexMigration.MigrationId),
                    [new ProviderMigrationCandidate(Step(RenameColumnAndIndexMigration.MigrationId), migration)]));
            return;
        }

        var result = Assert.Single(new ProviderMigrationOperationValidator(Plan(target), target).Validate(
            Stream(RenameColumnAndIndexMigration.MigrationId),
            [new ProviderMigrationCandidate(Step(RenameColumnAndIndexMigration.MigrationId), migration)]));
        var from = Assert.Single(result.Transition.From.Tables);
        Assert.Contains(from.Columns, static column => column.Name == "ParentKey");
        Assert.Equal("ParentKey", Assert.Single(from.UniqueConstraints).Columns.Single());
        Assert.Equal("ParentKey", Assert.Single(from.ForeignKeys).PrincipalColumns.Single());
        var fromIndex = Assert.Single(from.Indexes);
        Assert.Equal("IX_Asset_ParentKey", fromIndex.Name);
        Assert.Equal("ParentKey", fromIndex.Columns.Single());
    }

    [Fact]
    public void Validate_RenameTable_RewritesOwnedForeignKeyPrincipalInFromTarget()
    {
        var renamedParent = Target("Example", "RenamedParent");
        var child = Target("Example", "Child") with
        {
            Columns = [TargetColumn("Id"), TargetColumn("ParentId")],
            ForeignKeys =
            [
                new ProviderForeignKeyTarget(
                    "FK_Child_Parent",
                    ["ParentId"],
                    "Example",
                    "dbo",
                    "RenamedParent",
                    ["Id"],
                    "Restrict")
            ]
        };
        var target = new ProviderTargetSnapshot(
            ProviderTargetSchemaVersions.TargetV1,
            PersistenceProvider.SqlServer,
            [renamedParent, child]);
        var migration = new RenameParentTableMigration();

        var result = Assert.Single(new ProviderMigrationOperationValidator(Plan(target), target).Validate(
            Stream(RenameParentTableMigration.MigrationId),
            [new ProviderMigrationCandidate(Step(RenameParentTableMigration.MigrationId), migration)]));

        Assert.Contains(result.Transition.From.Tables, static table => table.Name == "Parent");
        var fromChild = Assert.Single(result.Transition.From.Tables, static table => table.Name == "Child");
        Assert.Equal("Parent", Assert.Single(fromChild.ForeignKeys).PrincipalTable);
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("alter")]
    [InlineData("primary-key")]
    [InlineData("unique")]
    [InlineData("foreign-key")]
    [InlineData("index")]
    [InlineData("drop-column")]
    [InlineData("drop-primary-key")]
    [InlineData("drop-primary-columns")]
    [InlineData("drop-unique")]
    [InlineData("drop-unique-columns")]
    [InlineData("drop-foreign-key")]
    [InlineData("drop-foreign-columns")]
    [InlineData("drop-index")]
    [InlineData("drop-index-columns")]
    [InlineData("drop-owner")]
    public void Validate_MalformedTransitionOperation_IsRejected(string malformedPart)
    {
        var target = TransitionTarget();
        var migration = new ExactTransitionMigration(malformedPart);

        Assert.Throws<ProviderOperationValidationException>(() =>
            new ProviderMigrationOperationValidator(Plan(target), target).Validate(
                Stream(ExactTransitionMigration.MigrationId),
                [new ProviderMigrationCandidate(Step(ExactTransitionMigration.MigrationId), migration)]));
    }

    [Fact]
    public void Validate_DropTableWithExactVersionedFromDescriptor_ProvesOldOwnerAndShape()
    {
        var oldTable = Target("Example", "ObsoleteAsset");
        var target = new ProviderTargetSnapshot(
            ProviderTargetSchemaVersions.TargetV1,
            PersistenceProvider.SqlServer,
            [Target("Example", "Parent")]);
        var migration = new DescriptorDropTableMigration(oldTable);

        var result = Assert.Single(new ProviderMigrationOperationValidator(Plan(target), target).Validate(
            Stream(DescriptorDropTableMigration.MigrationId),
            [new ProviderMigrationCandidate(Step(DescriptorDropTableMigration.MigrationId), migration)]));

        Assert.Contains(result.Transition.From.Tables, static table => table.Name == "ObsoleteAsset");
        Assert.DoesNotContain(result.Transition.To.Tables, static table => table.Name == "ObsoleteAsset");
    }

    [Fact]
    public void Validate_DropTableCannotClaimAnotherOwnersCurrentPhysicalTarget()
    {
        var claimed = Target("Example", "SharedAsset");
        var target = new ProviderTargetSnapshot(
            ProviderTargetSchemaVersions.TargetV1,
            PersistenceProvider.SqlServer,
            [Target("Other", "SharedAsset")]);
        var migration = new DescriptorDropTableMigration(claimed);

        Assert.Throws<ProviderOperationValidationException>(() =>
            new ProviderMigrationOperationValidator(Plan(target), target).Validate(
                Stream(DescriptorDropTableMigration.MigrationId),
                [new ProviderMigrationCandidate(Step(DescriptorDropTableMigration.MigrationId), migration)]));
    }

    [Fact]
    public void Validate_CreateTableWithRequiredColumns_DoesNotQueryAnAbsentFromTable()
    {
        var created = Target("Example", "CreatedAsset");
        var target = new ProviderTargetSnapshot(
            ProviderTargetSchemaVersions.TargetV1,
            PersistenceProvider.SqlServer,
            [created]);
        var migration = new CreateRequiredTableMigration(created);

        var result = Assert.Single(new ProviderMigrationOperationValidator(Plan(target), target).Validate(
            Stream(CreateRequiredTableMigration.MigrationId),
            [new ProviderMigrationCandidate(Step(CreateRequiredTableMigration.MigrationId), migration)]));

        Assert.Empty(result.Preconditions);
        Assert.DoesNotContain(result.Transition.From.Tables, static table => table.Name == "CreatedAsset");
    }

    [Fact]
    public void Validate_RequiredFieldThreeStepTransition_AcceptsStructuredOwnerBackfillBetweenExpandAndContract()
    {
        const string addId = "20260828000100_Example_000003_RecordSourceNullable";
        const string backfillId = "20260828000200_Example_000004_RecordSourceBackfill";
        const string contractId = "20260828000300_Example_000005_RecordSourceRequired";
        var table = Target("Example", "ExampleRecord") with
        {
            Columns =
            [
                TargetColumn("Id"),
                TargetColumn("RecordSource")
            ]
        };
        var target = new ProviderTargetSnapshot(
            ProviderTargetSchemaVersions.TargetV1,
            PersistenceProvider.SqlServer,
            [table]);
        var steps = new[]
        {
            new CompiledMigrationStep("000003", addId, null, new string('1', 64), new string('2', 64), new string('3', 64), "safe-expand"),
            new CompiledMigrationStep("000004", backfillId, new string('1', 64), new string('4', 64), new string('3', 64), new string('3', 64), "data-backfill"),
            new CompiledMigrationStep("000005", contractId, new string('4', 64), new string('5', 64), new string('3', 64), new string('6', 64), "contract")
        };
        var stream = new ModuleMigrationStream(
            "Example",
            PersistenceProvider.SqlServer,
            "unused.dll",
            "unused.json",
            new string('a', 64),
            new string('b', 64),
            contractId,
            IsPlatformStream: false,
            steps);
        var add = new ExplicitOperationsMigration(addId,
        [
            new AddColumnOperation
            {
                Name = "RecordSource",
                Table = "ExampleRecord",
                Schema = "dbo",
                ClrType = typeof(string),
                ColumnType = "nvarchar(128)",
                IsNullable = true,
                MaxLength = 128
            }
        ]);
        var backfill = new ExplicitOperationsMigration(backfillId,
        [
            new RequiredFieldBackfillOperation
            {
                OwnerModuleId = "Example",
                Schema = "dbo",
                Table = "ExampleRecord",
                Column = "RecordSource",
                Value = "Legacy"
            }
        ]);
        var contract = new ExplicitOperationsMigration(contractId,
        [
            new AlterColumnOperation
            {
                Name = "RecordSource",
                Table = "ExampleRecord",
                Schema = "dbo",
                ClrType = typeof(string),
                ColumnType = "nvarchar(128)",
                IsNullable = false,
                MaxLength = 128,
                OldColumn = new AddColumnOperation
                {
                    Name = "RecordSource",
                    Table = "ExampleRecord",
                    Schema = "dbo",
                    ClrType = typeof(string),
                    ColumnType = "nvarchar(128)",
                    IsNullable = true,
                    MaxLength = 128
                }
            }
        ]);

        var result = new ProviderMigrationOperationValidator(Plan(target), target).Validate(
            stream,
            [
                new ProviderMigrationCandidate(steps[0], add),
                new ProviderMigrationCandidate(steps[1], backfill),
                new ProviderMigrationCandidate(steps[2], contract)
            ]);

        Assert.Equal(3, result.Count);
        Assert.Equal(result[1].Transition.From, result[1].Transition.To);
        var backfillFact = Assert.Single(result[1].Transition.RequiredFieldBackfills);
        Assert.Equal(
            new ProviderRequiredFieldBackfillFact(
                "Example",
                "dbo",
                "ExampleRecord",
                "RecordSource",
                typeof(string).FullName!,
                "\"Legacy\""),
            backfillFact);
        Assert.Empty(result[1].Preconditions);
        var contractPrecondition = Assert.Single(result[2].Preconditions);
        Assert.Equal(PersistenceMigrationFailureCodes.NullValuesPresent, contractPrecondition.FailureCode);
        Assert.Equal("RecordSource", contractPrecondition.Column);
    }

    [Fact]
    public void Validate_RequiredFieldBackfillAndContractInSameMigration_IsRejected()
    {
        const string migrationId = "20260828000200_Example_000004_RecordSourceBackfillAndContract";
        var table = Target("Example", "ExampleRecord") with
        {
            Columns = [TargetColumn("Id"), TargetColumn("RecordSource")]
        };
        var target = new ProviderTargetSnapshot(
            ProviderTargetSchemaVersions.TargetV1,
            PersistenceProvider.SqlServer,
            [table]);
        var step = new CompiledMigrationStep(
            "000004",
            migrationId,
            new string('1', 64),
            new string('2', 64),
            new string('3', 64),
            new string('4', 64),
            "data-backfill");
        var stream = new ModuleMigrationStream(
            "Example",
            PersistenceProvider.SqlServer,
            "unused.dll",
            "unused.json",
            new string('a', 64),
            new string('b', 64),
            migrationId,
            IsPlatformStream: false,
            [step]);
        var migration = new ExplicitOperationsMigration(migrationId,
        [
            new RequiredFieldBackfillOperation
            {
                OwnerModuleId = "Example",
                Schema = "dbo",
                Table = "ExampleRecord",
                Column = "RecordSource",
                Value = "Legacy"
            },
            new AlterColumnOperation
            {
                Name = "RecordSource",
                Table = "ExampleRecord",
                Schema = "dbo",
                ClrType = typeof(string),
                ColumnType = "nvarchar(128)",
                IsNullable = false,
                MaxLength = 128,
                OldColumn = new AddColumnOperation
                {
                    Name = "RecordSource",
                    Table = "ExampleRecord",
                    Schema = "dbo",
                    ClrType = typeof(string),
                    ColumnType = "nvarchar(128)",
                    IsNullable = true,
                    MaxLength = 128
                }
            }
        ]);

        var exception = Assert.Throws<ProviderOperationValidationException>(() =>
            new ProviderMigrationOperationValidator(Plan(target), target).Validate(
                stream,
                [new ProviderMigrationCandidate(step, migration)]));

        Assert.Equal(migrationId, exception.MigrationId);
    }

    [Fact]
    public void Validate_DataBackfillStepWithoutStructuredBackfill_IsRejected()
    {
        const string migrationId = "20260828000200_Example_000004_UnstructuredDataBackfill";
        var table = Target("Example", "ExampleRecord") with
        {
            Columns = [TargetColumn("Id"), TargetColumn("RecordSource")]
        };
        var target = new ProviderTargetSnapshot(
            ProviderTargetSchemaVersions.TargetV1,
            PersistenceProvider.SqlServer,
            [table]);
        var step = new CompiledMigrationStep(
            "000004",
            migrationId,
            new string('1', 64),
            new string('2', 64),
            new string('3', 64),
            new string('4', 64),
            "data-backfill");
        var stream = new ModuleMigrationStream(
            "Example",
            PersistenceProvider.SqlServer,
            "unused.dll",
            "unused.json",
            new string('a', 64),
            new string('b', 64),
            migrationId,
            IsPlatformStream: false,
            [step]);
        var migration = new ExplicitOperationsMigration(migrationId,
        [
            new AlterColumnOperation
            {
                Name = "RecordSource",
                Table = "ExampleRecord",
                Schema = "dbo",
                ClrType = typeof(string),
                ColumnType = "nvarchar(128)",
                IsNullable = false,
                MaxLength = 128,
                OldColumn = new AddColumnOperation
                {
                    Name = "RecordSource",
                    Table = "ExampleRecord",
                    Schema = "dbo",
                    ClrType = typeof(string),
                    ColumnType = "nvarchar(128)",
                    IsNullable = true,
                    MaxLength = 128
                }
            }
        ]);

        var exception = Assert.Throws<ProviderOperationValidationException>(() =>
            new ProviderMigrationOperationValidator(Plan(target), target).Validate(
                stream,
                [new ProviderMigrationCandidate(step, migration)]));

        Assert.Equal(migrationId, exception.MigrationId);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("cross-owner-table")]
    [InlineData("schema")]
    [InlineData("table")]
    [InlineData("column")]
    [InlineData("type")]
    [InlineData("phase")]
    [InlineData("order")]
    [InlineData("compatibility")]
    public void Validate_RequiredFieldBackfillBoundaryViolation_IsRejected(string violation)
    {
        var validationCase = BuildRequiredFieldBackfillValidationCase(violation);

        var exception = Assert.Throws<ProviderOperationValidationException>(() =>
            new ProviderMigrationOperationValidator(validationCase.Plan, validationCase.Target).Validate(
                validationCase.Stream,
                validationCase.Candidates));

        Assert.Equal(validationCase.BackfillMigrationId, exception.MigrationId);
    }

    [Fact]
    public void Validate_MalformedFromDescriptor_IsRejectedWithValidationException()
    {
        var target = new ProviderTargetSnapshot(
            ProviderTargetSchemaVersions.TargetV1,
            PersistenceProvider.SqlServer,
            [Target("Example", "Parent")]);
        var operation = new DropTableOperation
        {
            Name = "ObsoleteAsset",
            Schema = "dbo"
        };
        operation.AddAnnotation(ProviderTransitionAnnotationNames.FromDescriptorV1, "{not-json");
        var migration = new ExplicitOperationsMigration("20260812000003_MalformedDescriptor", [operation]);

        Assert.Throws<ProviderOperationValidationException>(() =>
            new ProviderMigrationOperationValidator(Plan(target), target).Validate(
                Stream(migration.MigrationId),
                [new ProviderMigrationCandidate(Step(migration.MigrationId), migration)]));
    }

    private static CompiledPersistencePlan Plan(ProviderTargetSnapshot target)
    {
        var models = target.Tables.Select(table => new ModelDefinition(
            table.Name,
            table.Name,
            ModelKind.Entity,
            [new FieldDefinition(
                "Id",
                "Id",
                FieldKind.Scalar,
                FieldValueType.ObjectId,
                "string",
                Required: true,
                IsPrimaryKey: true,
                IsAuditField: false)],
            [],
            table.OwnerModuleId,
            table.Schema)).ToArray();
        var digest = PersistenceModelDigest.Compute(models);
        return new CompiledPersistencePlan(
            "provider-transition-test",
            digest,
            digest,
            PersistenceProvider.SqlServer,
            digest,
            [],
            models,
            [Stream(ExactTransitionMigration.MigrationId)]);
    }

    private static ProviderTargetSnapshot TransitionTarget()
    {
        var parent = Target("Example", "Parent");
        var asset = Target("Example", "RenamedAsset") with
        {
            Columns =
            [
                TargetColumn("Id"),
                TargetColumn("ParentId")
            ],
            PrimaryKey = new ProviderKeyTarget("PK_RenamedAsset", ["Id"]),
            UniqueConstraints = [new ProviderKeyTarget("AK_RenamedAsset_Id", ["Id"])],
            ForeignKeys =
            [
                new ProviderForeignKeyTarget(
                    "FK_RenamedAsset_Parent",
                    ["ParentId"],
                    "Example",
                    "dbo",
                    "Parent",
                    ["Id"],
                    "Restrict")
            ],
            Indexes =
            [
                new ProviderIndexTarget(
                    "IX_RenamedAsset_ParentId",
                    ["ParentId"],
                    IsUnique: false,
                    IsDescending: null,
                    Filter: null,
                    ProviderAnnotations: [])
            ]
        };
        return new ProviderTargetSnapshot(
            ProviderTargetSchemaVersions.TargetV1,
            PersistenceProvider.SqlServer,
            [parent, asset]);
    }

    private static ModuleMigrationStream Stream(string migrationId) => new(
        "Example",
        PersistenceProvider.SqlServer,
        "unused.dll",
        "unused.json",
        new string('a', 64),
        new string('b', 64),
        migrationId,
        IsPlatformStream: false,
        [Step(migrationId)]);

    private static CompiledMigrationStep Step(string migrationId) => new(
        "000001",
        migrationId,
        null,
        new string('c', 64),
        new string('d', 64),
        new string('e', 64),
        "safe-expand");

    private static ProviderTableTarget Target(string owner, string name) => new(
        owner,
        "dbo",
        name,
        [
            TargetColumn("Id")
        ],
        new ProviderKeyTarget($"PK_{name}", ["Id"]),
        [],
        [],
        [],
        []);

    private static ProviderColumnTarget TargetColumn(string name) => new(
        name,
        "nvarchar(128)",
        typeof(string).FullName!,
        IsNullable: false,
        MaxLength: 128,
        Precision: null,
        Scale: null,
        IsUnicode: null,
        IsFixedLength: null,
        Collation: null,
        ComputedColumnSql: null,
        IsStored: null,
        IsIdentity: false,
        IsRowVersion: false,
        DefaultValue: null,
        DefaultValueSql: null,
        ProviderAnnotations: []);

    private static IReadOnlyList<CompiledMigrationHistoryEntry> BindSingleHistory(
        string providerTargetDigest,
        string providerTransitionDigest) =>
        UnifiedMigrationHistoryDigest.BindPlanDigests(
        [
            new CompiledMigrationHistoryEntry(
                0,
                PersistenceHistoryVersions.ReconciliationV1,
                "20260828000200_Example_000004_RecordSourceBackfill",
                "Example",
                new string('1', 64),
                new string('2', 64),
                new string('3', 64),
                new string('3', 64),
                string.Empty,
                ProviderTargetSchemaVersions.TargetV1,
                providerTargetDigest,
                ProviderTargetSchemaVersions.TransitionV1,
                providerTransitionDigest)
        ]);

    private static ProviderTransitionSnapshot ValidateRequiredFieldBackfillTransition(DateTime value)
    {
        const string backfillId = "20260828000200_Example_000004_OccurredAtBackfill";
        const string contractId = "20260828000300_Example_000005_OccurredAtRequired";
        var occurredAt = new ProviderColumnTarget(
            "OccurredAt",
            "datetime2(7)",
            typeof(DateTime).FullName!,
            IsNullable: false,
            MaxLength: null,
            Precision: null,
            Scale: null,
            IsUnicode: null,
            IsFixedLength: null,
            Collation: null,
            ComputedColumnSql: null,
            IsStored: null,
            IsIdentity: false,
            IsRowVersion: false,
            DefaultValue: null,
            DefaultValueSql: null,
            ProviderAnnotations: []);
        var table = Target("Example", "ExampleRecord") with
        {
            Columns = [TargetColumn("Id"), occurredAt]
        };
        var target = new ProviderTargetSnapshot(
            ProviderTargetSchemaVersions.TargetV1,
            PersistenceProvider.SqlServer,
            [table]);
        var backfillStep = new CompiledMigrationStep(
            "000004",
            backfillId,
            new string('1', 64),
            new string('2', 64),
            new string('3', 64),
            new string('3', 64),
            "data-backfill");
        var contractStep = new CompiledMigrationStep(
            "000005",
            contractId,
            new string('2', 64),
            new string('4', 64),
            new string('3', 64),
            new string('5', 64),
            "contract");
        var backfill = new ExplicitOperationsMigration(backfillId,
        [
            new RequiredFieldBackfillOperation
            {
                OwnerModuleId = "Example",
                Schema = "dbo",
                Table = "ExampleRecord",
                Column = "OccurredAt",
                Value = value
            }
        ]);
        var contract = new ExplicitOperationsMigration(contractId,
        [
            new AlterColumnOperation
            {
                Name = "OccurredAt",
                Table = "ExampleRecord",
                Schema = "dbo",
                ClrType = typeof(DateTime),
                ColumnType = "datetime2(7)",
                IsNullable = false,
                OldColumn = new AddColumnOperation
                {
                    Name = "OccurredAt",
                    Table = "ExampleRecord",
                    Schema = "dbo",
                    ClrType = typeof(DateTime),
                    ColumnType = "datetime2(7)",
                    IsNullable = true
                }
            }
        ]);
        var stream = new ModuleMigrationStream(
            "Example",
            PersistenceProvider.SqlServer,
            "unused.dll",
            "unused.json",
            new string('a', 64),
            new string('b', 64),
            contractId,
            IsPlatformStream: false,
            [backfillStep, contractStep]);
        var result = new ProviderMigrationOperationValidator(Plan(target), target).Validate(
            stream,
            [
                new ProviderMigrationCandidate(backfillStep, backfill),
                new ProviderMigrationCandidate(contractStep, contract)
            ]);
        return result[0].Transition;
    }

    private static RequiredFieldBackfillValidationCase BuildRequiredFieldBackfillValidationCase(string violation)
    {
        const string addId = "20260828000100_Example_000003_RecordSourceNullable";
        const string backfillId = "20260828000200_Example_000004_RecordSourceBackfill";
        const string contractId = "20260828000300_Example_000005_RecordSourceRequired";
        var finalColumn = TargetColumn("RecordSource") with
        {
            IsNullable = string.Equals(violation, "phase", StringComparison.Ordinal)
        };
        var exampleTable = Target("Example", "ExampleRecord") with
        {
            Columns = [TargetColumn("Id"), finalColumn]
        };
        var tables = string.Equals(violation, "cross-owner-table", StringComparison.Ordinal)
            ? new[] { exampleTable, Target("Other", "OtherRecord") with { Columns = [TargetColumn("Id"), TargetColumn("RecordSource")] } }
            : [exampleTable];
        var target = new ProviderTargetSnapshot(
            ProviderTargetSchemaVersions.TargetV1,
            PersistenceProvider.SqlServer,
            tables);
        var addStep = new CompiledMigrationStep(
            "000003",
            addId,
            null,
            new string('1', 64),
            new string('2', 64),
            new string('3', 64),
            "safe-expand");
        var backfillStep = new CompiledMigrationStep(
            "000004",
            backfillId,
            new string('1', 64),
            new string('4', 64),
            new string('3', 64),
            new string('3', 64),
            string.Equals(violation, "compatibility", StringComparison.Ordinal)
                ? "safe-expand"
                : "data-backfill");
        var contractStep = new CompiledMigrationStep(
            "000005",
            contractId,
            new string('4', 64),
            new string('5', 64),
            new string('3', 64),
            new string('6', 64),
            "contract");
        var add = new ExplicitOperationsMigration(addId,
        [
            new AddColumnOperation
            {
                Name = "RecordSource",
                Table = "ExampleRecord",
                Schema = "dbo",
                ClrType = typeof(string),
                ColumnType = "nvarchar(128)",
                IsNullable = true,
                MaxLength = 128
            }
        ]);
        var backfill = new ExplicitOperationsMigration(backfillId,
        [
            new RequiredFieldBackfillOperation
            {
                OwnerModuleId = string.Equals(violation, "owner", StringComparison.Ordinal) ? "Other" : "Example",
                Schema = string.Equals(violation, "schema", StringComparison.Ordinal) ? "other" : "dbo",
                Table = violation switch
                {
                    "cross-owner-table" => "OtherRecord",
                    "table" => "MissingRecord",
                    _ => "ExampleRecord"
                },
                Column = string.Equals(violation, "column", StringComparison.Ordinal) ? "MissingSource" : "RecordSource",
                Value = string.Equals(violation, "type", StringComparison.Ordinal) ? 42 : "Legacy"
            }
        ]);
        var contract = new ExplicitOperationsMigration(contractId,
        [
            new AlterColumnOperation
            {
                Name = "RecordSource",
                Table = "ExampleRecord",
                Schema = "dbo",
                ClrType = typeof(string),
                ColumnType = "nvarchar(128)",
                IsNullable = false,
                MaxLength = 128,
                OldColumn = new AddColumnOperation
                {
                    Name = "RecordSource",
                    Table = "ExampleRecord",
                    Schema = "dbo",
                    ClrType = typeof(string),
                    ColumnType = "nvarchar(128)",
                    IsNullable = true,
                    MaxLength = 128
                }
            }
        ]);
        var steps = string.Equals(violation, "phase", StringComparison.Ordinal)
            ? new[] { addStep, backfillStep }
            : string.Equals(violation, "order", StringComparison.Ordinal)
                ? [addStep, contractStep, backfillStep]
                : [addStep, backfillStep, contractStep];
        var candidates = string.Equals(violation, "phase", StringComparison.Ordinal)
            ? new[]
            {
                new ProviderMigrationCandidate(addStep, add),
                new ProviderMigrationCandidate(backfillStep, backfill)
            }
            : string.Equals(violation, "order", StringComparison.Ordinal)
                ?
                [
                    new ProviderMigrationCandidate(addStep, add),
                    new ProviderMigrationCandidate(contractStep, contract),
                    new ProviderMigrationCandidate(backfillStep, backfill)
                ]
                :
                [
                    new ProviderMigrationCandidate(addStep, add),
                    new ProviderMigrationCandidate(backfillStep, backfill),
                    new ProviderMigrationCandidate(contractStep, contract)
                ];
        var stream = new ModuleMigrationStream(
            "Example",
            PersistenceProvider.SqlServer,
            "unused.dll",
            "unused.json",
            new string('a', 64),
            new string('b', 64),
            steps[^1].MigrationId,
            IsPlatformStream: false,
            steps);
        return new RequiredFieldBackfillValidationCase(
            Plan(target),
            target,
            stream,
            candidates,
            backfillId);
    }

    private sealed record RequiredFieldBackfillValidationCase(
        CompiledPersistencePlan Plan,
        ProviderTargetSnapshot Target,
        ModuleMigrationStream Stream,
        IReadOnlyList<ProviderMigrationCandidate> Candidates,
        string BackfillMigrationId);

    private sealed class ExactTransitionMigration : Migration
    {
        internal const string MigrationId = "20260812000000_ExactTransition";
        private readonly string? malformedPart;

        internal ExactTransitionMigration(string? malformedPart = null)
        {
            this.malformedPart = malformedPart;
        }

        protected override void Up(MigrationBuilder migrationBuilder) { }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }

        public override IReadOnlyList<MigrationOperation> UpOperations =>
        [
            Annotate(
                new DropIndexOperation
                {
                    Name = "IX_Asset_Old",
                    Table = "Asset",
                    Schema = "dbo"
                },
                ProviderTransitionDescriptorCodec.EncodeIndex(
                    DescriptorOwner,
                    new ProviderIndexTarget(
                        malformedPart == "drop-index" ? "IX_Unexpected" : "IX_Asset_Old",
                        [malformedPart == "drop-index-columns" ? "Missing" : "ParentId"],
                        IsUnique: false,
                        IsDescending: null,
                        Filter: null,
                        ProviderAnnotations: []))),
            Annotate(
                new DropForeignKeyOperation
                {
                    Name = "FK_Asset_Parent_Old",
                    Table = "Asset",
                    Schema = "dbo"
                },
                ProviderTransitionDescriptorCodec.EncodeForeignKey(
                    DescriptorOwner,
                    new ProviderForeignKeyTarget(
                        malformedPart == "drop-foreign-key" ? "FK_Unexpected" : "FK_Asset_Parent_Old",
                        [malformedPart == "drop-foreign-columns" ? "Missing" : "ParentId"],
                        "Example",
                        "dbo",
                        "Parent",
                        ["Id"],
                        "Restrict"))),
            Annotate(
                new DropUniqueConstraintOperation
                {
                    Name = "AK_Asset_Old",
                    Table = "Asset",
                    Schema = "dbo"
                },
                ProviderTransitionDescriptorCodec.EncodeKey(
                    DescriptorOwner,
                    new ProviderKeyTarget(
                        malformedPart == "drop-unique" ? "AK_Unexpected" : "AK_Asset_Old",
                        [malformedPart == "drop-unique-columns" ? "Missing" : "Id"]))),
            Annotate(
                new DropPrimaryKeyOperation
                {
                    Name = "PK_Asset_Old",
                    Table = "Asset",
                    Schema = "dbo"
                },
                ProviderTransitionDescriptorCodec.EncodeKey(
                    DescriptorOwner,
                    new ProviderKeyTarget(
                        malformedPart == "drop-primary-key" ? "PK_Unexpected" : "PK_Asset_Old",
                        [malformedPart == "drop-primary-columns" ? "Missing" : "Id"]))),
            Annotate(
                new DropColumnOperation
                {
                    Name = "Obsolete",
                    Table = "Asset",
                    Schema = "dbo"
                },
                ProviderTransitionDescriptorCodec.EncodeColumn(
                    DescriptorOwner,
                    2,
                    TargetColumn(malformedPart == "drop-column" ? "Unexpected" : "Obsolete"))),
            new RenameTableOperation
            {
                Name = "Asset",
                Schema = "dbo",
                NewName = malformedPart == "rename" ? "UnexpectedAsset" : "RenamedAsset",
                NewSchema = "dbo"
            },
            new AlterColumnOperation
            {
                Name = "Id",
                Table = "RenamedAsset",
                Schema = "dbo",
                ClrType = typeof(string),
                ColumnType = malformedPart == "alter" ? "nvarchar(64)" : "nvarchar(128)",
                IsNullable = false,
                MaxLength = 128,
                OldColumn = new AddColumnOperation
                {
                    Name = "Id",
                    Table = "RenamedAsset",
                    Schema = "dbo",
                    ClrType = typeof(string),
                    ColumnType = "nvarchar(64)",
                    IsNullable = false,
                    MaxLength = 64
                }
            },
            new AddPrimaryKeyOperation
            {
                Name = malformedPart == "primary-key" ? "PK_Unexpected" : "PK_RenamedAsset",
                Table = "RenamedAsset",
                Schema = "dbo",
                Columns = ["Id"]
            },
            new AddUniqueConstraintOperation
            {
                Name = malformedPart == "unique" ? "AK_Unexpected" : "AK_RenamedAsset_Id",
                Table = "RenamedAsset",
                Schema = "dbo",
                Columns = ["Id"]
            },
            new AddForeignKeyOperation
            {
                Name = malformedPart == "foreign-key" ? "FK_Unexpected" : "FK_RenamedAsset_Parent",
                Table = "RenamedAsset",
                Schema = "dbo",
                Columns = ["ParentId"],
                PrincipalSchema = "dbo",
                PrincipalTable = "Parent",
                PrincipalColumns = ["Id"],
                OnDelete = ReferentialAction.Restrict
            },
            new CreateIndexOperation
            {
                Name = malformedPart == "index" ? "IX_Unexpected" : "IX_RenamedAsset_ParentId",
                Table = "RenamedAsset",
                Schema = "dbo",
                Columns = ["ParentId"]
            }
        ];

        private string DescriptorOwner => malformedPart == "drop-owner" ? "Other" : "Example";

        private static T Annotate<T>(T operation, string descriptor)
            where T : MigrationOperation
        {
            operation.AddAnnotation(ProviderTransitionAnnotationNames.FromDescriptorV1, descriptor);
            return operation;
        }
    }

    private sealed class DescriptorDropTableMigration(ProviderTableTarget table) : Migration
    {
        internal const string MigrationId = "20260812000001_DescriptorDropTable";

        protected override void Up(MigrationBuilder migrationBuilder) { }

        protected override void Down(MigrationBuilder migrationBuilder) { }

        public override IReadOnlyList<MigrationOperation> UpOperations
        {
            get
            {
                var operation = new DropTableOperation
                {
                    Name = table.Name,
                    Schema = table.Schema
                };
                operation.AddAnnotation(
                    ProviderTransitionAnnotationNames.FromDescriptorV1,
                    ProviderTransitionDescriptorCodec.EncodeTable(table.OwnerModuleId, table));
                return [operation];
            }
        }
    }

    private sealed class RenameColumnAndIndexMigration(string? malformedPart) : Migration
    {
        internal const string MigrationId = "20260812000004_RenameColumnAndIndex";

        protected override void Up(MigrationBuilder migrationBuilder) { }

        protected override void Down(MigrationBuilder migrationBuilder) { }

        public override IReadOnlyList<MigrationOperation> UpOperations =>
        [
            new RenameColumnOperation
            {
                Name = "ParentKey",
                Table = "Asset",
                Schema = "dbo",
                NewName = malformedPart == "rename-column" ? "Unexpected" : "ParentId"
            },
            new RenameIndexOperation
            {
                Name = "IX_Asset_ParentKey",
                Table = "Asset",
                Schema = "dbo",
                NewName = malformedPart == "rename-index" ? "IX_Unexpected" : "IX_Asset_ParentId"
            }
        ];
    }

    private sealed class RenameParentTableMigration : Migration
    {
        internal const string MigrationId = "20260812000005_RenameParentTable";

        protected override void Up(MigrationBuilder migrationBuilder) { }

        protected override void Down(MigrationBuilder migrationBuilder) { }

        public override IReadOnlyList<MigrationOperation> UpOperations =>
        [
            new RenameTableOperation
            {
                Name = "Parent",
                Schema = "dbo",
                NewName = "RenamedParent",
                NewSchema = "dbo"
            }
        ];
    }

    private sealed class CreateRequiredTableMigration(ProviderTableTarget table) : Migration
    {
        internal const string MigrationId = "20260812000002_CreateRequiredTable";

        protected override void Up(MigrationBuilder migrationBuilder) { }

        protected override void Down(MigrationBuilder migrationBuilder) { }

        public override IReadOnlyList<MigrationOperation> UpOperations
        {
            get
            {
                var create = new CreateTableOperation
                {
                    Name = table.Name,
                    Schema = table.Schema,
                    PrimaryKey = new AddPrimaryKeyOperation
                    {
                        Name = table.PrimaryKey!.Name,
                        Table = table.Name,
                        Schema = table.Schema,
                        Columns = table.PrimaryKey.Columns.ToArray()
                    }
                };
                foreach (var column in table.Columns)
                {
                    create.Columns.Add(new AddColumnOperation
                    {
                        Name = column.Name,
                        Table = table.Name,
                        Schema = table.Schema,
                        ClrType = typeof(string),
                        ColumnType = column.StoreType,
                        IsNullable = column.IsNullable,
                        MaxLength = column.MaxLength
                    });
                }

                return [create];
            }
        }
    }

    private sealed class ExplicitOperationsMigration(
        string migrationId,
        IReadOnlyList<MigrationOperation> operations) : Migration
    {
        internal string MigrationId { get; } = migrationId;

        protected override void Up(MigrationBuilder migrationBuilder) { }

        protected override void Down(MigrationBuilder migrationBuilder) { }

        public override IReadOnlyList<MigrationOperation> UpOperations => operations;
    }
}
