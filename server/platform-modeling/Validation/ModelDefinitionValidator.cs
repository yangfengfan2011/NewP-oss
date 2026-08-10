using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modeling.Validation;

public sealed class ModelDefinitionValidator
{
    public void Validate(ModelDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var duplicateField = definition.Fields
            .GroupBy(static field => field.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicateField is not null)
        {
            throw new InvalidOperationException($"Model '{definition.Name}' has duplicate field '{duplicateField.Key}'.");
        }

        if (!definition.Fields.Any(static field => field.IsPrimaryKey))
        {
            throw new InvalidOperationException($"Model '{definition.Name}' must define at least one primary key field.");
        }

        var fieldNames = definition.Fields
            .Select(static field => field.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var invalidIndex = definition.Indexes.FirstOrDefault(index => index.Fields.Any(field => !fieldNames.Contains(field)));
        if (invalidIndex is not null)
        {
            throw new InvalidOperationException($"Model '{definition.Name}' index '{invalidIndex.Name}' references undefined field.");
        }

        var invalidForeignKey = definition.ForeignKeys.FirstOrDefault(foreignKey =>
            string.IsNullOrWhiteSpace(foreignKey.Name)
            || foreignKey.Fields.Count == 0
            || foreignKey.Fields.Any(field => !fieldNames.Contains(field))
            || string.IsNullOrWhiteSpace(foreignKey.PrincipalModelKey)
            || foreignKey.PrincipalFields.Count == 0
            || foreignKey.Fields.Count != foreignKey.PrincipalFields.Count);
        if (invalidForeignKey is not null)
        {
            throw new InvalidOperationException(
                $"Model '{definition.Name}' foreign key '{invalidForeignKey.Name}' is invalid.");
        }
    }
}
