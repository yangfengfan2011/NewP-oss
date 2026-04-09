using System.Data.Common;

namespace ICP.Platform.Persistence.Abstractions;

public interface ISqlQueryExecutor
{
    Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default);
}
