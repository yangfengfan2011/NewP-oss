using ICP.Platform.Persistence.Abstractions;

namespace ICP.Platform.Persistence.EfCore.Sql;

public sealed class OracleDialect : ISqlDialect
{
    public string Name => "Oracle";

    public string Parameter(string name) => $":{name}";
}
