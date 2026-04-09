using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Persistence.EfCore.Projection;

public sealed class EfModelProjector
{
    public void Project(ModelBuilder modelBuilder, IReadOnlyList<ModelDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            var entity = modelBuilder.SharedTypeEntity(definition.Name, typeof(Dictionary<string, object?>));
            entity.ToTable(definition.TableName);

            foreach (var field in definition.Fields)
            {
                var property = entity.IndexerProperty(MapClrType(field), field.Name)
                    .HasColumnName(field.StoreName);

                if (ShouldApplyMaxLength(field))
                {
                    property.HasMaxLength(GetMaxLength(field));
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
                var indexBuilder = entity.HasIndex(index.Fields.ToArray())
                    .HasDatabaseName(index.Name);

                if (index.IsUnique)
                {
                    indexBuilder.IsUnique();
                }
            }
        }
    }

    private static Type MapClrType(FieldDefinition field) =>
        field.ValueType switch
        {
            FieldValueType.Boolean => typeof(bool),
            FieldValueType.DateTime => typeof(DateTime),
            _ => typeof(string)
        };

    private static bool ShouldApplyMaxLength(FieldDefinition field) =>
        MapClrType(field) == typeof(string) && field.ValueType != FieldValueType.Description;

    private static int GetMaxLength(FieldDefinition field) =>
        field.ValueType switch
        {
            FieldValueType.ObjectId => 128,
            FieldValueType.User => 128,
            FieldValueType.Group => 128,
            _ => 256
        };
}
