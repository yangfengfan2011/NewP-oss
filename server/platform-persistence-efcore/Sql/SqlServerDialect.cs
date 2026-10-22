using ICP.Platform.Persistence.Abstractions;

namespace ICP.Platform.Persistence.EfCore.Sql;

public sealed class SqlServerDialect : ISqlDialect
{
    public string Name => "SqlServer";

    public string Parameter(string name) => $"@{name}";
}
