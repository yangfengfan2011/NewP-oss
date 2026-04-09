namespace ICP.Platform.Modeling.Definitions;

public interface IModelDefinitionProvider
{
    IReadOnlyList<ModelDefinition> GetDefinitions();
}
