namespace ICP.Platform.Persistence.Abstractions;

public interface ISqlDialect
{
    string Name { get; }

    string Parameter(string name);
}
