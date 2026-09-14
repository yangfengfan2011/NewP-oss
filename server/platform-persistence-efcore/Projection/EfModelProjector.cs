using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ICP.Platform.Persistence.EfCore.Projection;

public sealed class EfModelProjector
{
    /// <summary>
    /// 将规范模型投影为 EF shared-type entity；audit DateTime 字段在 materialize 时恢复 UTC Kind，其余映射保持现有存储语义。
    /// </summary>
    public void Project(ModelBuilder modelBuilder, IReadOnlyList<ModelDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            var entity = modelBuilder.SharedTypeEntity(GetEntityName(definition), typeof(Dictionary<string, object?>));
            entity.ToTable(
                definition.TableName,
                string.IsNullOrWhiteSpace(definition.SchemaName) ? null : definition.SchemaName);

            foreach (var field in definition.Fields)
            {
                var property = entity.IndexerProperty(MapClrType(field), field.Name)
                    .HasColumnName(field.StoreName);

                if (field.ValueType == FieldValueType.CalendarDate)
                {
                    property.HasColumnType("date");
                }

                if ((field.IsAuditField && field.ValueType == FieldValueType.DateTime)
                    || field.ValueType == FieldValueType.Instant)
                {
                    property.HasConversion(CreateUtcDateTimeConverter());
                }

                if (ShouldApplyMaxLength(field))
                {
                    property.HasMaxLength(GetMaxLength(field));
                }

                if (field.OrdinalKey)
                {
                    // 完整身份不截断；派生哈希只属于数据库索引，不用于授权身份或关联查询。
                    property.HasColumnType("nvarchar(max)").UseCollation("Latin1_General_100_BIN2");
                    var escapedColumn = field.StoreName.Replace("]", "]]", StringComparison.Ordinal);
                    entity.IndexerProperty<string>("__ordinal_" + field.Name).HasMaxLength(64).IsRequired()
                        .HasComputedColumnSql($"isnull(CONVERT([nvarchar](64),hashbytes('SHA2_256',CONVERT([varbinary](max),[{escapedColumn}])),(2)),N'')", stored: true);
                }

                if (field.Required)
                {
                    property.IsRequired();
                }
            }

            var primaryKeys = definition.Fields
                .Where(static field => field.IsPrimaryKey)
                .Select(static field => field.Name)
                .ToArray();

            entity.HasKey(primaryKeys);

            foreach (var index in definition.Indexes)
            {
                var indexBuilder = entity.HasIndex(index.Fields.Select(name => definition.Fields.Single(field => field.Name == name).OrdinalKey ? "__ordinal_" + name : name).ToArray())
                    .HasDatabaseName(index.Name);

                if (index.IsUnique)
                {
                    indexBuilder.IsUnique();
                    if (index.IncludeNullValues)
                    {
                        indexBuilder.HasFilter(null);
                    }
                }
            }
        }

        // 关系必须在全部 shared-type entity 已建立后投影，避免跨模块外键因输入顺序
        // 隐式创建一个错误 CLR 类型的 principal entity。
        foreach (var definition in definitions)
        {
            var entity = modelBuilder.SharedTypeEntity(GetEntityName(definition), typeof(Dictionary<string, object?>));
            foreach (var foreignKey in definition.ForeignKeys)
            {
                entity
                    .HasOne(foreignKey.PrincipalModelKey, navigationName: null)
                    .WithMany()
                    .HasForeignKey(foreignKey.Fields.ToArray())
                    .HasPrincipalKey(foreignKey.PrincipalFields.ToArray())
                    .OnDelete(MapDeleteBehavior(foreignKey.DeleteBehavior))
                    .HasConstraintName(foreignKey.Name);
            }
        }
    }

    // 将规范值类型映射到 EF shared-type property 的 CLR 类型，并保留可空性。
    private static Type MapClrType(FieldDefinition field)
    {
        var clrType = field.ValueType switch
        {
            FieldValueType.Boolean => typeof(bool),
            FieldValueType.CalendarDate => typeof(DateOnly),
            FieldValueType.DateTime => typeof(DateTime),
            FieldValueType.Instant => typeof(DateTime),
            FieldValueType.Integer => typeof(long),
            _ => typeof(string)
        };

        return field.Required || !clrType.IsValueType
            ? clrType
            : typeof(Nullable<>).MakeGenericType(clrType);
    }

    // EF 对 nullable value type 会直通 null；这里只把 provider 返回的 UTC-owned DateTime Kind 规范回 UTC。
    private static ValueConverter<DateTime, DateTime> CreateUtcDateTimeConverter() =>
        new(
            static value => value,
            static value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DeleteBehavior MapDeleteBehavior(ForeignKeyDeleteBehavior behavior) =>
        behavior switch
        {
            ForeignKeyDeleteBehavior.Restrict => DeleteBehavior.Restrict,
            ForeignKeyDeleteBehavior.Cascade => DeleteBehavior.Cascade,
            ForeignKeyDeleteBehavior.SetNull => DeleteBehavior.SetNull,
            ForeignKeyDeleteBehavior.NoAction => DeleteBehavior.NoAction,
            _ => throw new InvalidOperationException($"Unsupported foreign key delete behavior: {behavior}")
        };

    private static string GetEntityName(ModelDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.OwnerModuleId)
            ? definition.Name
            : definition.LogicalKey;

    private static bool ShouldApplyMaxLength(FieldDefinition field) =>
        MapClrType(field) == typeof(string) && field.ValueType != FieldValueType.Description && !field.OrdinalKey;

    private static int GetMaxLength(FieldDefinition field) =>
        field.ValueType switch
        {
            FieldValueType.ObjectId => 128,
            FieldValueType.User => 128,
            FieldValueType.Group => 128,
            _ => 256
        };
}
