using ICP.Platform.Persistence.Abstractions;

namespace ICP.Platform.Persistence.EfCore.Sql;

public sealed class MySqlDialect : ISqlDialect
{
    public string Name => "MySql";

    public string Parameter(string name) => $"@{name}";
}
