namespace ICP.Platform.Crud.Persistence;

/// <summary>
/// 仅识别平台已记录的数据库唯一键与引用约束错误号，供持久化 owner 稳定翻译并发竞态。
/// </summary>
public static class EfPersistenceConstraintClassifier
{
    /// <summary>
    /// 判断异常链是否包含指定 provider 的已知约束错误；未知 provider 或错误保持 false。
    /// </summary>
    public static bool IsKnownConstraintConflict(string? providerName, Exception exception)
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
