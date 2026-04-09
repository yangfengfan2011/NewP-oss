using System.Data;
using System.Data.Common;
using ICP.Platform.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ICP.Platform.Persistence.EfCore.Sql;

public sealed class EfSqlQueryExecutor : ISqlQueryExecutor
{
    private readonly EfPlatformDbContext _dbContext;

    public EfSqlQueryExecutor(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            var currentTransaction = _dbContext.Database.CurrentTransaction;
            if (currentTransaction is not null)
            {
                command.Transaction = currentTransaction.GetDbTransaction();
            }

            foreach (var parameter in parameters)
            {
                var dbParameter = command.CreateParameter();
                dbParameter.ParameterName = parameter.Key;
                dbParameter.Value = parameter.Value ?? DBNull.Value;
                command.Parameters.Add(dbParameter);
            }

            var items = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(map(reader));
            }

            return items;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
