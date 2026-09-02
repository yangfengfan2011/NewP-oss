namespace ICP.Platform.Crud.Persistence;

internal static class EfPersistenceConstraintClassifier
{
    // provider 名与其稳定错误号共同限定可翻译范围，避免把 mapping、schema 或连接故障误报为 409。
    internal static bool IsKnownConstraintConflict(string? providerName, Exception exception)
    {
        var knownNumbers = providerName switch
        {
            "Microsoft.EntityFrameworkCore.SqlServer" => new HashSet<int> { 547, 2601, 2627 },
            "MySql.EntityFrameworkCore" => new HashSet<int> { 1062, 1451, 1452 },
            "Oracle.EntityFrameworkCore" => new HashSet<int> { 1, 2291, 2292 },
            _ => null
        };
        if (knownNumbers is null)
        {
            return false;
        }

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var number = current.GetType().GetProperty("Number")?.GetValue(current);
            if (number is int providerNumber && knownNumbers.Contains(providerNumber))
            {
                return true;
            }
        }

        return false;
    }
}
