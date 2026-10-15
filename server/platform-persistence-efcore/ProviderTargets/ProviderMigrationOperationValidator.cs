using System.Text.Json;
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.Planning;
using ICP.Platform.Persistence.ProviderTargets;
using ICP.Platform.Persistence.EfCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace ICP.Platform.Persistence.EfCore.ProviderTargets;

internal sealed record ProviderMigrationCandidate(
    CompiledMigrationStep Step,
    Migration Migration);

internal sealed record ProviderMigrationDataPrecondition(
    string FailureCode,
    string Schema,
    string Table,
    string? Column);

internal sealed record ValidatedProviderMigration(
    CompiledMigrationStep Step,
    string MigrationId,
    Migration Migration,
    ProviderTransitionSnapshot Transition,
    string TransitionDigest,
    IReadOnlyList<ProviderMigrationDataPrecondition> Preconditions);

internal sealed class ProviderOperationValidationException(
    string migrationId,
    string? operationType = null,
    string? detail = null)
    : InvalidOperationException(
        $"Migration '{migrationId}' does not match its provider transition target"
        + (operationType is null ? "." : $" at operation '{operationType}'")
        + (string.IsNullOrWhiteSpace(detail) ? "." : $": {detail}"))
{
    internal string MigrationId { get; } = migrationId;
}

/// <summary>
/// 从最终 EF provider target 逆推每个 migration 的 from/to target，并逐项验证操作。
/// 逆推只描述已签名 migration 的结构过渡，不读取数据库，也不生成或执行 SQL。
/// </summary>
internal sealed class ProviderMigrationOperationValidator(
    CompiledPersistencePlan plan,
    ProviderTargetSnapshot providerTarget)
{
    // 从最终 target 逆推完整 stream，并在形成任何 transition 事实前关闭非法 operation 形状。
    internal IReadOnlyList<ValidatedProviderMigration> Validate(
        ModuleMigrationStream stream,
        IReadOnlyList<ProviderMigrationCandidate> candidates)
    {
        var current = providerTarget with
        {
            Tables = providerTarget.Tables
                .Where(table => OwnsTable(stream, table))
                .ToArray()
        };
        var results = new ValidatedProviderMigration[candidates.Count];

        for (var migrationIndex = candidates.Count - 1; migrationIndex >= 0; migrationIndex--)
        {
            var candidate = candidates[migrationIndex];
            ValidateBackfillStepShape(candidate);
            var to = current;
            var preconditions = new List<ProviderMigrationDataPrecondition>();
            for (var operationIndex = candidate.Migration.UpOperations.Count - 1; operationIndex >= 0; operationIndex--)
            {
                current = Reverse(
                    stream,
                    candidate.Step,
                    candidate.Migration.UpOperations[operationIndex],
                    current,
                    preconditions);
            }

            var applicablePreconditions = preconditions
                .Where(precondition => PreconditionAppliesToFromTarget(precondition, current))
                .Distinct()
                .Reverse()
                .ToArray();

            var transition = new ProviderTransitionSnapshot(
                ProviderTargetSchemaVersions.TransitionV1,
                stream.ModuleId,
                candidate.Step.MigrationId,
                current,
                to)
            {
                RequiredFieldBackfills = candidate.Migration.UpOperations
                    .OfType<RequiredFieldBackfillOperation>()
                    .Select(ProjectRequiredFieldBackfillFact)
                    .ToArray(),
                RelationOrdinalBackfills = candidate.Migration.UpOperations.OfType<RelationOrdinalBackfillOperation>().Select(operation => operation.Fact).ToArray()
            };
            results[migrationIndex] = new ValidatedProviderMigration(
                candidate.Step,
                candidate.Step.MigrationId,
                candidate.Migration,
                transition,
                ProviderTargetDigest.Compute(transition),
                applicablePreconditions);
        }

        return results;
    }

    // 受控回填必须独占 data-backfill step，避免把结构变化或任意命令藏进同一历史事务。
    private static void ValidateBackfillStepShape(ProviderMigrationCandidate candidate)
    {
        var backfillCount = candidate.Migration.UpOperations
            .Count(static operation => operation is RequiredFieldBackfillOperation or RelationOrdinalBackfillOperation);
        var isBackfillStep = string.Equals(
            candidate.Step.Compatibility,
            "data-backfill",
            StringComparison.Ordinal);
        if (backfillCount == 0 && !isBackfillStep)
        {
            return;
        }

        if (!isBackfillStep
            || backfillCount == 0
            || backfillCount != candidate.Migration.UpOperations.Count)
        {
            throw new ProviderOperationValidationException(
                candidate.Step.MigrationId,
                nameof(RequiredFieldBackfillOperation),
                "structured backfill must be the only operation kind in a data-backfill step");
        }
    }

    // 将已经通过 owner、目标和类型校验的 operation 固化为 Provider Transition 内容事实。
    private static ProviderRequiredFieldBackfillFact ProjectRequiredFieldBackfillFact(
        RequiredFieldBackfillOperation operation) =>
        new(
            operation.OwnerModuleId,
            operation.Schema,
            operation.Table,
            operation.Column,
            operation.Value.GetType().FullName!,
            JsonSerializer.Serialize(operation.Value, operation.Value.GetType()));

    private ProviderTargetSnapshot Reverse(
        ModuleMigrationStream stream,
        CompiledMigrationStep step,
        MigrationOperation operation,
        ProviderTargetSnapshot current,
        List<ProviderMigrationDataPrecondition> preconditions)
    {
        try
        {
            return operation switch
            {
                EnsureSchemaOperation ensureSchema => ReverseEnsureSchema(stream, ensureSchema, current),
                CreateTableOperation createTable => ReverseCreateTable(stream, createTable, current),
                DropTableOperation dropTable => ReverseDropTable(stream, step, dropTable, current),
                RenameTableOperation renameTable => ReverseRenameTable(stream, renameTable, current, preconditions),
                AddColumnOperation addColumn => ReverseAddColumn(stream, addColumn, current, preconditions),
                DropColumnOperation dropColumn => ReverseDropColumn(stream, dropColumn, current),
                RenameColumnOperation renameColumn => ReverseRenameColumn(stream, renameColumn, current, preconditions),
                AlterColumnOperation alterColumn => ReverseAlterColumn(stream, alterColumn, current, preconditions),
                AddPrimaryKeyOperation addPrimaryKey => ReverseAddPrimaryKey(stream, addPrimaryKey, current),
                DropPrimaryKeyOperation dropPrimaryKey => ReverseDropPrimaryKey(stream, dropPrimaryKey, current),
                AddUniqueConstraintOperation addUnique => ReverseAddUnique(stream, addUnique, current),
                DropUniqueConstraintOperation dropUnique => ReverseDropUnique(stream, dropUnique, current),
                AddForeignKeyOperation addForeignKey => ReverseAddForeignKey(stream, addForeignKey, current),
                DropForeignKeyOperation dropForeignKey => ReverseDropForeignKey(stream, dropForeignKey, current),
                CreateIndexOperation createIndex => ReverseCreateIndex(stream, createIndex, current),
                DropIndexOperation dropIndex => ReverseDropIndex(stream, dropIndex, current),
                RenameIndexOperation renameIndex => ReverseRenameIndex(stream, renameIndex, current),
                RequiredFieldBackfillOperation backfill => ReverseRequiredFieldBackfill(stream, backfill, current),
                RelationOrdinalBackfillOperation ordinal => ReverseRelationOrdinalBackfill(stream, ordinal, current),
                _ => throw Invalid(step)
            };
        }
        catch (ProviderOperationValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw new ProviderOperationValidationException(
                step.MigrationId,
                operation.GetType().Name,
                exception.Message);
        }
    }

    private ProviderTargetSnapshot ReverseEnsureSchema(
        ModuleMigrationStream stream,
        EnsureSchemaOperation operation,
        ProviderTargetSnapshot current)
    {
        if (!plan.Models.Any(model =>
                OwnsModel(stream, model.OwnerModuleId)
                && string.Equals(model.SchemaName, operation.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException();
        }

        return current;
    }

    private ProviderTargetSnapshot ReverseCreateTable(
        ModuleMigrationStream stream,
        CreateTableOperation operation,
        ProviderTargetSnapshot current)
    {
        var expected = FindOwnedTable(stream, current, operation.Schema, operation.Name);
        if (!CreateTableMatches(operation, expected))
        {
            throw new InvalidOperationException();
        }

        return RemoveTable(current, expected);
    }

    private ProviderTargetSnapshot ReverseDropTable(
        ModuleMigrationStream stream,
        CompiledMigrationStep step,
        DropTableOperation operation,
        ProviderTargetSnapshot current)
    {
        // Drop 的目标集合中不再存在旧表，但仍要用全局 Provider Target
        // 拒绝删除其他模块当前拥有的同名物理表。
        if (TryFindTable(current, operation.Schema, operation.Name) is not null
            || TryFindTable(providerTarget, operation.Schema, operation.Name) is not null)
        {
            throw new InvalidOperationException();
        }

        if (TryReadFromDescriptor(operation, stream, "table") is { } descriptor)
        {
            var table = descriptor.Table!;
            if (!string.Equals(table.OwnerModuleId, stream.ModuleId, StringComparison.Ordinal)
                || !string.Equals(
                    PhysicalKey(table.Schema, table.Name),
                    PhysicalKey(operation.Schema, operation.Name),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException();
            }

            return AddTable(current, table);
        }

        throw new InvalidOperationException();
    }

    private ProviderTargetSnapshot ReverseRenameTable(
        ModuleMigrationStream stream,
        RenameTableOperation operation,
        ProviderTargetSnapshot current,
        List<ProviderMigrationDataPrecondition> preconditions)
    {
        if (string.IsNullOrWhiteSpace(operation.NewName))
        {
            throw new InvalidOperationException();
        }

        var targetSchema = operation.NewSchema ?? operation.Schema ?? "dbo";
        var target = FindOwnedTable(stream, current, targetSchema, operation.NewName);
        if (TryFindTable(current, operation.Schema, operation.Name) is not null
            || TryFindTable(providerTarget, operation.Schema, operation.Name) is not null)
        {
            throw new InvalidOperationException();
        }

        var sourceSchema = operation.Schema ?? "dbo";
        for (var index = 0; index < preconditions.Count; index++)
        {
            var precondition = preconditions[index];
            if (string.Equals(precondition.Schema, target.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(precondition.Table, target.Name, StringComparison.OrdinalIgnoreCase))
            {
                preconditions[index] = precondition with
                {
                    Schema = sourceSchema,
                    Table = operation.Name
                };
            }
        }

        var reverted = ReplaceTable(current, target, target with
        {
            Schema = sourceSchema,
            Name = operation.Name
        });
        return reverted with
        {
            Tables = reverted.Tables.Select(table => table with
            {
                ForeignKeys = table.ForeignKeys.Select(foreignKey =>
                    string.Equals(foreignKey.PrincipalOwnerModuleId, stream.ModuleId, StringComparison.Ordinal)
                    && string.Equals(
                        PhysicalKey(foreignKey.PrincipalSchema, foreignKey.PrincipalTable),
                        PhysicalKey(target.Schema, target.Name),
                        StringComparison.OrdinalIgnoreCase)
                        ? foreignKey with
                        {
                            PrincipalSchema = sourceSchema,
                            PrincipalTable = operation.Name
                        }
                        : foreignKey).ToArray()
            }).ToArray()
        };
    }

    // 新增列按 canonical target 验证；显式空表标记仅复用 owner 自有表的既定数据前提。
    private ProviderTargetSnapshot ReverseAddColumn(
        ModuleMigrationStream stream,
        AddColumnOperation operation,
        ProviderTargetSnapshot current,
        List<ProviderMigrationDataPrecondition> preconditions)
    {
        var table = FindOwnedTable(stream, current, operation.Schema, operation.Table);
        var expected = table.Columns.SingleOrDefault(column =>
            string.Equals(column.Name, operation.Name, StringComparison.Ordinal));
        if (expected is null)
        {
            throw new InvalidOperationException($"target column '{operation.Name}' is absent");
        }

        if (!ColumnMatches(operation, expected, allowClrDefaultCompatibility: true))
        {
            throw new InvalidOperationException(
                $"column '{operation.Name}' differs ({DescribeColumnDiff(operation, expected)})");
        }

        var emptyTableMarker = operation.FindAnnotation(EmptyTableMigrationPrecondition.AnnotationName);
        if (emptyTableMarker is not null && emptyTableMarker.Value is not true)
            throw new InvalidOperationException("empty-table precondition marker must be true");
        if (!operation.IsNullable || emptyTableMarker is not null)
        {
            preconditions.Add(new ProviderMigrationDataPrecondition(
                PersistenceMigrationFailureCodes.TableNotEmpty,
                table.Schema,
                table.Name,
                null));
        }

        return ReplaceTable(current, table, table with
        {
            Columns = table.Columns.Where(column => column != expected).ToArray()
        });
    }

    private ProviderTargetSnapshot ReverseDropColumn(
        ModuleMigrationStream stream,
        DropColumnOperation operation,
        ProviderTargetSnapshot current)
    {
        if (string.IsNullOrWhiteSpace(operation.Table))
        {
            throw new InvalidOperationException();
        }

        var table = FindOwnedTable(stream, current, operation.Schema, operation.Table);
        var descriptor = ReadFromDescriptor(operation, stream, "column");
        var column = descriptor.Column!;
        var ordinal = descriptor.Ordinal!.Value;
        if (!string.Equals(column.Name, operation.Name, StringComparison.Ordinal)
            || ordinal > table.Columns.Count
            || table.Columns.Any(candidate => string.Equals(candidate.Name, column.Name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException();
        }

        var columns = table.Columns.ToList();
        columns.Insert(ordinal, column);
        return ReplaceTable(current, table, table with { Columns = columns });
    }

    private ProviderTargetSnapshot ReverseRenameColumn(
        ModuleMigrationStream stream,
        RenameColumnOperation operation,
        ProviderTargetSnapshot current,
        List<ProviderMigrationDataPrecondition> preconditions)
    {
        if (string.IsNullOrWhiteSpace(operation.Table))
        {
            throw new InvalidOperationException();
        }

        var table = FindOwnedTable(stream, current, operation.Schema, operation.Table);
        var target = table.Columns.SingleOrDefault(column =>
            string.Equals(column.Name, operation.NewName, StringComparison.Ordinal));
        if (target is null
            || table.Columns.Any(column => string.Equals(column.Name, operation.Name, StringComparison.Ordinal))
            || table.CheckConstraints.Count != 0
            || table.Indexes.Any(static index => !string.IsNullOrWhiteSpace(index.Filter)))
        {
            throw new InvalidOperationException();
        }

        for (var index = 0; index < preconditions.Count; index++)
        {
            var precondition = preconditions[index];
            if (string.Equals(precondition.Schema, table.Schema, StringComparison.OrdinalIgnoreCase)
                && string.Equals(precondition.Table, table.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(precondition.Column, operation.NewName, StringComparison.Ordinal))
            {
                preconditions[index] = precondition with { Column = operation.Name };
            }
        }

        var revertedTable = table with
        {
            Columns = table.Columns.Select(column =>
                column == target ? column with { Name = operation.Name } : column).ToArray(),
            PrimaryKey = table.PrimaryKey is null
                ? null
                : table.PrimaryKey with
                {
                    Columns = RenameColumn(table.PrimaryKey.Columns, target.Name, operation.Name)
                },
            UniqueConstraints = table.UniqueConstraints.Select(key => key with
            {
                Columns = RenameColumn(key.Columns, target.Name, operation.Name)
            }).ToArray(),
            ForeignKeys = table.ForeignKeys.Select(foreignKey => foreignKey with
            {
                Columns = RenameColumn(foreignKey.Columns, target.Name, operation.Name)
            }).ToArray(),
            Indexes = table.Indexes.Select(index => index with
            {
                Columns = RenameColumn(index.Columns, target.Name, operation.Name)
            }).ToArray()
        };
        var reverted = ReplaceTable(current, table, revertedTable);
        return reverted with
        {
            Tables = reverted.Tables.Select(candidate => candidate with
            {
                ForeignKeys = candidate.ForeignKeys.Select(foreignKey =>
                    string.Equals(foreignKey.PrincipalOwnerModuleId, stream.ModuleId, StringComparison.Ordinal)
                    && string.Equals(
                        PhysicalKey(foreignKey.PrincipalSchema, foreignKey.PrincipalTable),
                        PhysicalKey(table.Schema, table.Name),
                        StringComparison.OrdinalIgnoreCase)
                        ? foreignKey with
                        {
                            PrincipalColumns = RenameColumn(
                                foreignKey.PrincipalColumns,
                                target.Name,
                                operation.Name)
                        }
                        : foreignKey).ToArray()
            }).ToArray()
        };
    }

    private ProviderTargetSnapshot ReverseAlterColumn(
        ModuleMigrationStream stream,
        AlterColumnOperation operation,
        ProviderTargetSnapshot current,
        List<ProviderMigrationDataPrecondition> preconditions)
    {
        var table = FindOwnedTable(stream, current, operation.Schema, operation.Table);
        var expected = table.Columns.SingleOrDefault(column =>
            string.Equals(column.Name, operation.Name, StringComparison.Ordinal));
        if (expected is null || !ColumnMatches(operation, expected, allowClrDefaultCompatibility: false))
        {
            throw new InvalidOperationException();
        }

        var oldColumn = BuildColumnTarget(operation.OldColumn) with { Name = operation.Name };
        if (oldColumn.DefaultValue is not null || oldColumn.DefaultValueSql is not null)
        {
            throw new InvalidOperationException();
        }

        if (operation.OldColumn.IsNullable && !operation.IsNullable)
        {
            preconditions.Add(new ProviderMigrationDataPrecondition(
                PersistenceMigrationFailureCodes.NullValuesPresent,
                table.Schema,
                table.Name,
                operation.Name));
        }

        return ReplaceTable(current, table, table with
        {
            Columns = table.Columns.Select(column => column == expected ? oldColumn : column).ToArray()
        });
    }

    // 回填不改变 Provider Target，但必须位于 owner 自有 nullable 扩展与最终 required 目标之间，并携带匹配 CLR 类型的标量。
    private ProviderTargetSnapshot ReverseRequiredFieldBackfill(
        ModuleMigrationStream stream,
        RequiredFieldBackfillOperation operation,
        ProviderTargetSnapshot current)
    {
        if (!string.Equals(operation.OwnerModuleId, stream.ModuleId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("backfill owner differs from the migration stream owner");
        }

        var table = FindOwnedTable(stream, current, operation.Schema, operation.Table);
        var column = table.Columns.SingleOrDefault(candidate =>
            string.Equals(candidate.Name, operation.Column, StringComparison.Ordinal));
        var finalTable = FindOwnedTable(stream, providerTarget, operation.Schema, operation.Table);
        var finalColumn = finalTable.Columns.SingleOrDefault(candidate =>
            string.Equals(candidate.Name, operation.Column, StringComparison.Ordinal));
        if (column is null || finalColumn is null || !column.IsNullable || finalColumn.IsNullable)
        {
            throw new InvalidOperationException("backfill target is not between nullable expand and required contract");
        }

        if (operation.Value is null
            || !string.Equals(operation.Value.GetType().FullName, column.ClrType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("backfill literal type differs from the provider target column");
        }

        return current;
    }

    // 恢复资产只保留持久化声明；排名校验不能依赖停用 owner 已退出的活动查询目录。
    private ProviderTargetSnapshot ReverseRelationOrdinalBackfill(ModuleMigrationStream stream, RelationOrdinalBackfillOperation operation, ProviderTargetSnapshot current)
    {
        var fact = operation.Fact;
        if (plan.Provider != PersistenceProvider.SqlServer || fact.OwnerModuleId != stream.ModuleId)
            throw new InvalidOperationException("relation ordinal backfill owner/provider differs from the migration stream");
        var source = FindOwnedTable(stream, current, fact.Schema, fact.Table);
        var target = FindOwnedTable(stream, current, fact.Schema, fact.TargetTable);
        var link = plan.Models.Single(model => model.SchemaName == fact.Schema && model.TableName == fact.Table && model.OwnerModuleId == stream.ModuleId);
        var entity = plan.Models.Single(model => model.SchemaName == fact.Schema && model.TableName == fact.TargetTable && model.OwnerModuleId == stream.ModuleId);
        var parent = link.Fields.Single(field => field.Name == "PEID");
        var child = link.Fields.Single(field => field.Name == "CEID");
        var linkId = link.Fields.Single(field => field.Name == "LID");
        var entityId = entity.Fields.Single(field => field.Name == "EID");
        var discriminator = link.Fields.Single(field => field.StoreName == fact.TypeColumn);
        var ordinalField = link.Fields.Single(field => field.StoreName == fact.Column);
        var name = entity.Fields.Single(field => field.StoreName == fact.TargetNameColumn);
        if (link.Kind != ModelKind.Link || entity.Kind != ModelKind.Entity || entity.LogicalKey != fact.ChildModelKey
            || !IsRelationReference(parent) || parent.StoreName != fact.ParentColumn
            || !IsRelationReference(child) || child.StoreName != fact.ChildColumn
            || !IsObjectIdentity(linkId) || linkId.StoreName != fact.LinkIdColumn
            || !IsObjectIdentity(entityId) || entityId.StoreName != fact.TargetIdColumn
            || discriminator.Kind != FieldKind.Scalar || discriminator.ValueType != FieldValueType.Text
            || !discriminator.Required || !discriminator.OrdinalKey || discriminator.IsAuditField || discriminator.IsPrimaryKey
            || ordinalField.Kind != FieldKind.Scalar || ordinalField.ValueType != FieldValueType.Integer
            || ordinalField.Required || ordinalField.IsAuditField || ordinalField.IsPrimaryKey
            || name.Kind != FieldKind.Scalar || name.ValueType != FieldValueType.Text || !name.Required || name.IsAuditField || name.IsPrimaryKey
            || source.PrimaryKey?.Columns.Count != 1 || source.PrimaryKey.Columns[0] != fact.LinkIdColumn
            || target.PrimaryKey?.Columns.Count != 1 || target.PrimaryKey.Columns[0] != fact.TargetIdColumn)
            throw new InvalidOperationException("relation ordinal backfill does not match the owned Link/Entity storage declarations");
        ValidateRelationOrdinalParent(stream, current, source, link, parent);
        var ordinal = source.Columns.Single(column => column.Name == fact.Column);
        if (!ordinal.IsNullable || ordinal.ClrType != typeof(long).FullName || ordinal.ComputedColumnSql is not null
            || new[] { fact.ParentColumn, fact.ChildColumn, fact.LinkIdColumn, fact.TypeColumn }
                .Any(name => !IsRequiredStoredText(source.Columns.Single(column => column.Name == name)))
            || !IsRequiredStoredText(target.Columns.Single(column => column.Name == fact.TargetIdColumn))
            || !IsRequiredStoredText(target.Columns.Single(column => column.Name == fact.TargetNameColumn)))
            throw new InvalidOperationException("relation ordinal backfill column types or NULL-only phase are invalid");
        return current;
    }

    // 固定 Link 两端引用必须保持平台声明的身份类型，不能用普通文本或审计字段替代。
    private static bool IsRelationReference(FieldDefinition field) =>
        field.Kind == FieldKind.Reference && field.ValueType == FieldValueType.ObjectId
        && field.Required && !field.IsPrimaryKey && !field.IsAuditField;

    // Link/Entity 的逻辑身份和 provider 单列主键必须同时匹配。
    private static bool IsObjectIdentity(FieldDefinition field) =>
        field.Kind == FieldKind.Scalar && field.ValueType == FieldValueType.ObjectId
        && field.Required && field.IsPrimaryKey && !field.IsAuditField;

    // 身份、子类型和名称必须是实际存储的必填文本，不能由 NULL 或计算列替代。
    private static bool IsRequiredStoredText(ProviderColumnTarget column) =>
        column.ClrType == typeof(string).FullName && !column.IsNullable && column.ComputedColumnSql is null;

    // 父分组必须由同 owner Entity 的正式单列外键约束，声明与物理映射缺一不可。
    private void ValidateRelationOrdinalParent(ModuleMigrationStream stream, ProviderTargetSnapshot current, ProviderTableTarget source, ModelDefinition link, FieldDefinition parent)
    {
        var foreignKey = link.ForeignKeys.Single(key => key.Fields.SequenceEqual([parent.Name]));
        var principal = plan.Models.Single(model => model.LogicalKey == foreignKey.PrincipalModelKey && model.OwnerModuleId == stream.ModuleId);
        var principalId = principal.Fields.Single(field => field.Name == "EID");
        var target = FindOwnedTable(stream, current, principal.SchemaName, principal.TableName);
        if (principal.Kind != ModelKind.Entity || !IsObjectIdentity(principalId)
            || foreignKey.PrincipalOwnerModuleId is { } owner && owner != stream.ModuleId
            || !foreignKey.PrincipalFields.SequenceEqual([principalId.Name])
            || target.PrimaryKey?.Columns.Count != 1 || target.PrimaryKey.Columns[0] != principalId.StoreName
            || !IsRequiredStoredText(target.Columns.Single(column => column.Name == principalId.StoreName))
            || !source.ForeignKeys.Any(key => key.Name == foreignKey.Name
                && key.Columns.SequenceEqual([parent.StoreName]) && key.PrincipalOwnerModuleId == principal.OwnerModuleId
                && key.PrincipalSchema == principal.SchemaName && key.PrincipalTable == principal.TableName
                && key.PrincipalColumns.SequenceEqual([principalId.StoreName])))
            throw new InvalidOperationException("relation ordinal backfill parent foreign key differs from its owned Entity storage mapping");
    }

    private ProviderTargetSnapshot ReverseAddPrimaryKey(
        ModuleMigrationStream stream,
        AddPrimaryKeyOperation operation,
        ProviderTargetSnapshot current)
    {
        var table = FindOwnedTable(stream, current, operation.Schema, operation.Table);
        if (table.PrimaryKey is null || !KeyMatches(operation, table.PrimaryKey))
        {
            throw new InvalidOperationException();
        }

        return ReplaceTable(current, table, table with { PrimaryKey = null });
    }

    private ProviderTargetSnapshot ReverseDropPrimaryKey(
        ModuleMigrationStream stream,
        DropPrimaryKeyOperation operation,
        ProviderTargetSnapshot current)
    {
        var table = FindOwnedTable(stream, current, operation.Schema, operation.Table);
        var key = ReadFromDescriptor(operation, stream, "key").Key!;
        if (table.PrimaryKey is not null
            || !string.Equals(key.Name, operation.Name, StringComparison.Ordinal)
            || !ColumnsExist(table, key.Columns))
        {
            throw new InvalidOperationException();
        }

        return ReplaceTable(current, table, table with { PrimaryKey = key });
    }

    private ProviderTargetSnapshot ReverseAddUnique(
        ModuleMigrationStream stream,
        AddUniqueConstraintOperation operation,
        ProviderTargetSnapshot current)
    {
        var table = FindOwnedTable(stream, current, operation.Schema, operation.Table);
        var target = table.UniqueConstraints.SingleOrDefault(key =>
            string.Equals(key.Name, operation.Name, StringComparison.Ordinal));
        if (target is null || !KeyMatches(operation, target))
        {
            throw new InvalidOperationException();
        }

        return ReplaceTable(current, table, table with
        {
            UniqueConstraints = table.UniqueConstraints.Where(key => key != target).ToArray()
        });
    }

    private ProviderTargetSnapshot ReverseDropUnique(
        ModuleMigrationStream stream,
        DropUniqueConstraintOperation operation,
        ProviderTargetSnapshot current)
    {
        var table = FindOwnedTable(stream, current, operation.Schema, operation.Table);
        var key = ReadFromDescriptor(operation, stream, "key").Key!;
        if (!string.Equals(key.Name, operation.Name, StringComparison.Ordinal)
            || !ColumnsExist(table, key.Columns)
            || table.UniqueConstraints.Any(candidate => string.Equals(candidate.Name, key.Name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException();
        }

        return ReplaceTable(current, table, table with
        {
            UniqueConstraints = table.UniqueConstraints.Append(key).ToArray()
        });
    }

    private ProviderTargetSnapshot ReverseAddForeignKey(
        ModuleMigrationStream stream,
        AddForeignKeyOperation operation,
        ProviderTargetSnapshot current)
    {
        var table = FindOwnedTable(stream, current, operation.Schema, operation.Table);
        var target = table.ForeignKeys.SingleOrDefault(key =>
            string.Equals(key.Name, operation.Name, StringComparison.Ordinal));
        if (target is null || !ForeignKeyMatches(operation, target))
        {
            throw new InvalidOperationException();
        }

        return ReplaceTable(current, table, table with
        {
            ForeignKeys = table.ForeignKeys.Where(key => key != target).ToArray()
        });
    }

    private ProviderTargetSnapshot ReverseDropForeignKey(
        ModuleMigrationStream stream,
        DropForeignKeyOperation operation,
        ProviderTargetSnapshot current)
    {
        var table = FindOwnedTable(stream, current, operation.Schema, operation.Table);
        var foreignKey = ReadFromDescriptor(operation, stream, "foreign-key").ForeignKey!;
        var principal = current.Tables.Concat(providerTarget.Tables).FirstOrDefault(candidate =>
            string.Equals(
                PhysicalKey(candidate.Schema, candidate.Name),
                PhysicalKey(foreignKey.PrincipalSchema, foreignKey.PrincipalTable),
                StringComparison.OrdinalIgnoreCase));
        if (!string.Equals(foreignKey.Name, operation.Name, StringComparison.Ordinal)
            || principal is null
            || !string.Equals(principal.OwnerModuleId, foreignKey.PrincipalOwnerModuleId, StringComparison.Ordinal)
            || foreignKey.Columns.Count != foreignKey.PrincipalColumns.Count
            || !ColumnsExist(table, foreignKey.Columns)
            || !ColumnsExist(principal, foreignKey.PrincipalColumns)
            || table.ForeignKeys.Any(candidate => string.Equals(candidate.Name, foreignKey.Name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException();
        }

        return ReplaceTable(current, table, table with
        {
            ForeignKeys = table.ForeignKeys.Append(foreignKey).ToArray()
        });
    }

    private ProviderTargetSnapshot ReverseCreateIndex(
        ModuleMigrationStream stream,
        CreateIndexOperation operation,
        ProviderTargetSnapshot current)
    {
        var table = FindOwnedTable(stream, current, operation.Schema, operation.Table);
        var target = table.Indexes.SingleOrDefault(index =>
            string.Equals(index.Name, operation.Name, StringComparison.Ordinal));
        if (target is null)
        {
            throw new InvalidOperationException($"target index '{operation.Name}' is absent");
        }

        if (!IndexMatches(operation, target))
        {
            throw new InvalidOperationException(
                $"index '{operation.Name}' differs: columns={string.Join(',', operation.Columns)}"
                + $", unique={operation.IsUnique}, descending={Format(operation.IsDescending)}, filter={operation.Filter ?? "<null>"}"
                + $"; target columns={string.Join(',', target.Columns)}, unique={target.IsUnique}"
                + $", descending={Format(target.IsDescending)}, filter={target.Filter ?? "<null>"}");
        }

        return ReplaceTable(current, table, table with
        {
            Indexes = table.Indexes.Where(index => index != target).ToArray()
        });

        static string Format(IReadOnlyList<bool>? values) =>
            values is null ? "<null>" : string.Join(',', values);
    }

    private ProviderTargetSnapshot ReverseDropIndex(
        ModuleMigrationStream stream,
        DropIndexOperation operation,
        ProviderTargetSnapshot current)
    {
        if (string.IsNullOrWhiteSpace(operation.Table))
        {
            throw new InvalidOperationException();
        }

        var table = FindOwnedTable(stream, current, operation.Schema, operation.Table);
        var index = ReadFromDescriptor(operation, stream, "index").Index!;
        if (!string.Equals(index.Name, operation.Name, StringComparison.Ordinal)
            || !ColumnsExist(table, index.Columns)
            || table.Indexes.Any(candidate => string.Equals(candidate.Name, index.Name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException();
        }

        return ReplaceTable(current, table, table with
        {
            Indexes = table.Indexes.Append(index).ToArray()
        });
    }

    private ProviderTargetSnapshot ReverseRenameIndex(
        ModuleMigrationStream stream,
        RenameIndexOperation operation,
        ProviderTargetSnapshot current)
    {
        if (string.IsNullOrWhiteSpace(operation.Table))
        {
            throw new InvalidOperationException();
        }

        var table = FindOwnedTable(stream, current, operation.Schema, operation.Table);
        var target = table.Indexes.SingleOrDefault(index =>
            string.Equals(index.Name, operation.NewName, StringComparison.Ordinal));
        if (target is null
            || table.Indexes.Any(index => string.Equals(index.Name, operation.Name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException();
        }

        return ReplaceTable(current, table, table with
        {
            Indexes = table.Indexes.Select(index =>
                index == target ? index with { Name = operation.Name } : index).ToArray()
        });
    }

    private bool CreateTableMatches(CreateTableOperation operation, ProviderTableTarget expected)
    {
        if (operation.Columns.Count != expected.Columns.Count)
        {
            throw new InvalidOperationException("column count differs");
        }

        for (var index = 0; index < operation.Columns.Count; index++)
        {
            var actual = operation.Columns[index];
            var target = expected.Columns[index];
            if (!string.Equals(actual.Name, target.Name, StringComparison.Ordinal)
                || !ColumnMatches(actual, target, allowClrDefaultCompatibility: false))
            {
                throw new InvalidOperationException(
                    $"column '{actual.Name}' differs ({DescribeColumnDiff(actual, target)})");
            }
        }

        if (operation.PrimaryKey is null != (expected.PrimaryKey is null)
            || (operation.PrimaryKey is not null && !KeyMatches(operation.PrimaryKey, expected.PrimaryKey!)))
        {
            throw new InvalidOperationException("primary key differs");
        }

        if (operation.UniqueConstraints.Count != expected.UniqueConstraints.Count
            || operation.UniqueConstraints.Any(actual =>
                expected.UniqueConstraints.SingleOrDefault(target => target.Name == actual.Name) is not { } target
                || !KeyMatches(actual, target)))
        {
            throw new InvalidOperationException("unique constraints differ");
        }

        if (operation.ForeignKeys.Count != expected.ForeignKeys.Count
            || operation.ForeignKeys.Any(actual =>
                expected.ForeignKeys.SingleOrDefault(target => target.Name == actual.Name) is not { } target
                || !ForeignKeyMatches(actual, target)))
        {
            throw new InvalidOperationException("foreign keys differ");
        }

        if (operation.CheckConstraints.Count != expected.CheckConstraints.Count
            || operation.CheckConstraints.Any(actual =>
                expected.CheckConstraints.SingleOrDefault(target => target.Name == actual.Name) is not { } target
                || !string.Equals(actual.Sql, target.Sql, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("check constraints differ");
        }

        return true;
    }

    private static bool ColumnMatches(
        ColumnOperation operation,
        ProviderColumnTarget expected,
        bool allowClrDefaultCompatibility)
    {
        if (expected.DefaultValue is not null
            || expected.DefaultValueSql is not null
            || !string.Equals(NormalizeStoreType(operation.ColumnType), NormalizeStoreType(expected.StoreType), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(operation.ClrType.FullName, expected.ClrType, StringComparison.Ordinal)
            || operation.IsNullable != expected.IsNullable
            || operation.MaxLength != expected.MaxLength
            || operation.Precision != expected.Precision
            || operation.Scale != expected.Scale
            || operation.IsUnicode != expected.IsUnicode
            || operation.IsFixedLength != expected.IsFixedLength
            || !string.Equals(operation.Collation, expected.Collation, StringComparison.Ordinal)
            || !string.Equals(operation.ComputedColumnSql, expected.ComputedColumnSql, StringComparison.Ordinal)
            || operation.IsStored != expected.IsStored
            || IsIdentity(operation) != expected.IsIdentity
            || operation.IsRowVersion != expected.IsRowVersion
            || !string.Equals(operation.DefaultValueSql, expected.DefaultValueSql, StringComparison.Ordinal)
            || !ProviderAnnotationsMatch(operation, expected.ProviderAnnotations))
        {
            return false;
        }

        if (IsAbsentDefault(operation.DefaultValue))
        {
            return expected.DefaultValue is null;
        }

        if (!allowClrDefaultCompatibility
            || expected.DefaultValue is not null
            || operation.IsNullable
            || !Equals(operation.DefaultValue, GetClrDefault(operation.ClrType)))
        {
            return false;
        }

        operation.DefaultValue = null;
        return true;
    }

    private static ProviderColumnTarget BuildColumnTarget(ColumnOperation operation) => new(
        operation.Name,
        NormalizeStoreType(operation.ColumnType),
        operation.ClrType.FullName ?? operation.ClrType.Name,
        operation.IsNullable,
        operation.MaxLength,
        operation.Precision,
        operation.Scale,
        operation.IsUnicode,
        operation.IsFixedLength,
        operation.Collation,
        operation.ComputedColumnSql,
        operation.IsStored,
        IsIdentity(operation),
        operation.IsRowVersion,
        IsAbsentDefault(operation.DefaultValue) ? null : EfProviderTargetSource.CanonicalValue(operation.DefaultValue),
        operation.DefaultValueSql,
        ProjectProviderAnnotations(operation));

    private static string DescribeColumnDiff(ColumnOperation actual, ProviderColumnTarget expected)
    {
        var differences = new List<string>();
        Add("type", NormalizeStoreType(actual.ColumnType), NormalizeStoreType(expected.StoreType));
        Add("clr", actual.ClrType.FullName, expected.ClrType);
        Add("nullable", actual.IsNullable, expected.IsNullable);
        Add("maxLength", actual.MaxLength, expected.MaxLength);
        Add("precision", actual.Precision, expected.Precision);
        Add("scale", actual.Scale, expected.Scale);
        Add("unicode", actual.IsUnicode, expected.IsUnicode);
        Add("fixed", actual.IsFixedLength, expected.IsFixedLength);
        Add("collation", actual.Collation, expected.Collation);
        Add("computed", actual.ComputedColumnSql, expected.ComputedColumnSql);
        Add("stored", actual.IsStored, expected.IsStored);
        Add("identity", IsIdentity(actual), expected.IsIdentity);
        Add("rowVersion", actual.IsRowVersion, expected.IsRowVersion);
        Add("defaultSql", actual.DefaultValueSql, expected.DefaultValueSql);
        Add("annotationMatch", ProviderAnnotationsMatch(actual, expected.ProviderAnnotations), true);
        Add(
            "defaultMatch",
            IsAbsentDefault(actual.DefaultValue) ? expected.DefaultValue is null : false,
            true);
        Add(
            "annotations",
            string.Join(',', ProjectProviderAnnotations(actual)),
            string.Join(',', expected.ProviderAnnotations));
        if (differences.Count == 0)
        {
            differences.Add(
                $"default: '{actual.DefaultValue ?? "<null>"}' ({actual.DefaultValue?.GetType().FullName ?? "<null>"})"
                + $" != '{expected.DefaultValue ?? "<null>"}'");
        }

        return string.Join("; ", differences);

        void Add(string name, object? left, object? right)
        {
            if (!Equals(left, right))
            {
                differences.Add($"{name}: '{left ?? "<null>"}' != '{right ?? "<null>"}'");
            }
        }
    }

    private static bool KeyMatches(AddPrimaryKeyOperation operation, ProviderKeyTarget expected) =>
        string.Equals(operation.Name, expected.Name, StringComparison.Ordinal)
        && operation.Columns.SequenceEqual(expected.Columns, StringComparer.Ordinal);

    private static bool KeyMatches(AddUniqueConstraintOperation operation, ProviderKeyTarget expected) =>
        string.Equals(operation.Name, expected.Name, StringComparison.Ordinal)
        && operation.Columns.SequenceEqual(expected.Columns, StringComparer.Ordinal);

    private bool ForeignKeyMatches(AddForeignKeyOperation operation, ProviderForeignKeyTarget expected)
    {
        var principal = providerTarget.Tables.SingleOrDefault(table =>
            string.Equals(
                PhysicalKey(table.Schema, table.Name),
                PhysicalKey(operation.PrincipalSchema, operation.PrincipalTable),
                StringComparison.OrdinalIgnoreCase));
        return principal is not null
            && string.Equals(operation.Name, expected.Name, StringComparison.Ordinal)
            && operation.Columns.SequenceEqual(expected.Columns, StringComparer.Ordinal)
            && string.Equals(principal.OwnerModuleId, expected.PrincipalOwnerModuleId, StringComparison.Ordinal)
            && string.Equals(operation.PrincipalSchema ?? "dbo", expected.PrincipalSchema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(operation.PrincipalTable, expected.PrincipalTable, StringComparison.OrdinalIgnoreCase)
            && (operation.PrincipalColumns ?? []).SequenceEqual(expected.PrincipalColumns, StringComparer.Ordinal)
            && string.Equals(operation.OnDelete.ToString(), expected.OnDelete, StringComparison.Ordinal);
    }

    private static bool IndexMatches(CreateIndexOperation operation, ProviderIndexTarget expected) =>
        string.Equals(operation.Name, expected.Name, StringComparison.Ordinal)
        && operation.Columns.SequenceEqual(expected.Columns, StringComparer.Ordinal)
        && operation.IsUnique == expected.IsUnique
        && NullableSequenceEqual(operation.IsDescending, expected.IsDescending)
        && string.Equals(operation.Filter, expected.Filter, StringComparison.Ordinal)
        && ProviderAnnotationsMatch(operation, expected.ProviderAnnotations);

    private static bool ProviderAnnotationsMatch(
        MigrationOperation operation,
        IReadOnlyList<ProviderAnnotationTarget> expected) =>
        ProjectProviderAnnotations(operation).SequenceEqual(expected);

    private static ProviderAnnotationTarget[] ProjectProviderAnnotations(MigrationOperation operation) =>
        operation.GetAnnotations()
            .Where(static annotation => annotation.Name.StartsWith("SqlServer:", StringComparison.Ordinal))
            .OrderBy(static annotation => annotation.Name, StringComparer.Ordinal)
            .Select(annotation => new ProviderAnnotationTarget(
                annotation.Name,
                EfProviderTargetSource.CanonicalValue(annotation.Value)))
            .ToArray();

    private static bool IsIdentity(MigrationOperation operation) =>
        operation.FindAnnotation("SqlServer:ValueGenerationStrategy")?.Value?.ToString() is "IdentityColumn";

    private static bool IsAbsentDefault(object? value) => value is null or DBNull;

    private static object? GetClrDefault(Type clrType) =>
        clrType.IsValueType ? Activator.CreateInstance(clrType) : null;

    private static bool NullableSequenceEqual<T>(IReadOnlyList<T>? actual, IReadOnlyList<T>? expected) =>
        actual is null
            ? expected is null
            : expected is not null && actual.SequenceEqual(expected);

    private static IReadOnlyList<string> RenameColumn(
        IReadOnlyList<string> columns,
        string currentName,
        string previousName) =>
        columns.Select(column =>
            string.Equals(column, currentName, StringComparison.Ordinal)
                ? previousName
                : column).ToArray();

    private static bool ColumnsExist(ProviderTableTarget table, IReadOnlyList<string> columns)
    {
        var available = table.Columns
            .Select(static column => column.Name)
            .ToHashSet(StringComparer.Ordinal);
        return columns.All(available.Contains);
    }

    private static bool PreconditionAppliesToFromTarget(
        ProviderMigrationDataPrecondition precondition,
        ProviderTargetSnapshot fromTarget)
    {
        var table = TryFindTable(fromTarget, precondition.Schema, precondition.Table);
        return table is not null
            && (precondition.Column is null
                || table.Columns.Any(column =>
                    string.Equals(column.Name, precondition.Column, StringComparison.Ordinal)));
    }

    private static ProviderTransitionFromDescriptor ReadFromDescriptor(
        MigrationOperation operation,
        ModuleMigrationStream stream,
        string kind) =>
        TryReadFromDescriptor(operation, stream, kind)
        ?? throw new InvalidOperationException("A versioned from descriptor is required.");

    private static ProviderTransitionFromDescriptor? TryReadFromDescriptor(
        MigrationOperation operation,
        ModuleMigrationStream stream,
        string kind)
    {
        var annotation = operation.FindAnnotation(ProviderTransitionAnnotationNames.FromDescriptorV1);
        if (annotation is null)
        {
            return null;
        }

        if (annotation.Value is not string encoded)
        {
            throw new InvalidOperationException("Provider transition from descriptor must be encoded text.");
        }

        var descriptor = ProviderTransitionDescriptorCodec.Decode(encoded);
        if (!string.Equals(descriptor.OwnerModuleId, stream.ModuleId, StringComparison.Ordinal)
            || !string.Equals(descriptor.Kind, kind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Provider transition from descriptor owner or kind differs.");
        }

        return descriptor;
    }

    private ProviderTableTarget FindOwnedTable(
        ModuleMigrationStream stream,
        ProviderTargetSnapshot target,
        string? schema,
        string table)
    {
        var result = TryFindTable(target, schema, table);
        return result is not null && OwnsTable(stream, result)
            ? result
            : throw new InvalidOperationException();
    }

    private bool OwnsTable(ModuleMigrationStream stream, ProviderTableTarget table) =>
        OwnsModel(stream, table.OwnerModuleId);

    private bool OwnsModel(ModuleMigrationStream stream, string ownerModuleId) =>
        stream.IsPlatformStream
            ? plan.Streams.All(candidate =>
                candidate.IsPlatformStream
                || !string.Equals(candidate.ModuleId, ownerModuleId, StringComparison.Ordinal))
            : string.Equals(ownerModuleId, stream.ModuleId, StringComparison.Ordinal);

    private static ProviderTableTarget? TryFindTable(
        ProviderTargetSnapshot target,
        string? schema,
        string table) =>
        target.Tables.SingleOrDefault(candidate =>
            string.Equals(
                PhysicalKey(candidate.Schema, candidate.Name),
                PhysicalKey(schema, table),
                StringComparison.OrdinalIgnoreCase));

    private static ProviderTargetSnapshot AddTable(ProviderTargetSnapshot target, ProviderTableTarget table) =>
        target with { Tables = target.Tables.Append(table).ToArray() };

    private static ProviderTargetSnapshot RemoveTable(ProviderTargetSnapshot target, ProviderTableTarget table) =>
        target with { Tables = target.Tables.Where(candidate => candidate != table).ToArray() };

    private static ProviderTargetSnapshot ReplaceTable(
        ProviderTargetSnapshot target,
        ProviderTableTarget oldTable,
        ProviderTableTarget newTable) =>
        target with
        {
            Tables = target.Tables.Select(candidate => candidate == oldTable ? newTable : candidate).ToArray()
        };

    private static string NormalizeStoreType(string? storeType) =>
        string.Join(' ', (storeType ?? string.Empty).Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries));

    private static string PhysicalKey(string? schema, string table) => $"{schema ?? "dbo"}.{table}";

    private static ProviderOperationValidationException Invalid(CompiledMigrationStep step) =>
        new(step.MigrationId);
}
