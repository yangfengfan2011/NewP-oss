using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;

namespace ICP.Platform.HostApi.Tests;

// 测量仅对本次一次性数据库启用短时事件；actual plans来自执行后事件，不使用缓存估算计划冒充。
internal static class FeatureSqlMeasurement
{
    internal static async Task<object> CaptureAsync(FeatureCatalogHostFixture fixture, string name, Func<Task> action)
    {
        var session = "NewPFeatureMeasure_" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(fixture.EvidenceRoot, name);
        Directory.CreateDirectory(directory);
        // SQL Server XE将路径中的反斜线点解释为转义；使用独立无点目录，结束后归档到证据目录。
        var eventDirectory = Path.Combine(Path.GetTempPath(), session);
        Directory.CreateDirectory(eventDirectory);
        var file = Path.Combine(eventDirectory, "events.xel");
        var master = new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var idCommand = new SqlCommand("SELECT DB_ID(@name)", connection);
        idCommand.Parameters.AddWithValue("@name", new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog);
        var databaseId = Convert.ToInt32(await idCommand.ExecuteScalarAsync());
        if (databaseId <= 4) throw new InvalidOperationException("Measurement requires its isolated user database.");
        var sql = $"""
            CREATE EVENT SESSION [{session}] ON SERVER
            ADD EVENT sqlserver.query_post_execution_showplan(ACTION(sqlserver.sql_text,sqlserver.session_id) WHERE ([sqlserver].[database_id]=({databaseId}))),
            ADD EVENT sqlserver.rpc_completed(WHERE ([sqlserver].[database_id]=({databaseId}))),
            ADD EVENT sqlserver.sql_batch_completed(WHERE ([sqlserver].[database_id]=({databaseId})))
            ADD TARGET package0.event_file(SET filename=N'{file.Replace("'", "''")}',max_file_size=(256),max_rollover_files=(4))
            WITH (MAX_MEMORY=16384 KB,EVENT_RETENTION_MODE=NO_EVENT_LOSS,MAX_DISPATCH_LATENCY=1 SECONDS,TRACK_CAUSALITY=OFF);
            """;
        var created = false;
        using var cancellation = new CancellationTokenSource();
        Task<LockObservation>? locks = null;
        try
        {
            await using (var command = new SqlCommand(sql, connection)) await command.ExecuteNonQueryAsync();
            created = true;
            await using (var start = new SqlCommand($"ALTER EVENT SESSION [{session}] ON SERVER STATE=START", connection)) await start.ExecuteNonQueryAsync();
            locks = ObserveLocksAsync(master, databaseId, cancellation.Token);
            var elapsed = Stopwatch.StartNew();
            await action();
            elapsed.Stop();
            cancellation.Cancel();
            var lockObservation = await locks;
            await using (var loss = new SqlCommand($"SELECT dropped_event_count FROM sys.dm_xe_sessions WHERE name=N'{session}'", connection))
                if (Convert.ToInt64(await loss.ExecuteScalarAsync()) != 0) throw new InvalidOperationException("SQL measurement lost events.");
            await using (var stop = new SqlCommand($"ALTER EVENT SESSION [{session}] ON SERVER STATE=STOP", connection)) await stop.ExecuteNonQueryAsync();
            await using var read = new SqlCommand("SELECT CAST(event_data AS nvarchar(max)) FROM sys.fn_xe_file_target_read_file(@file, NULL, NULL, NULL)", connection);
            read.Parameters.AddWithValue("@file", Path.Combine(eventDirectory, "events*.xel"));
            await using var reader = await read.ExecuteReaderAsync();
            var events = new List<XElement>();
            while (await reader.ReadAsync()) events.Add(XElement.Parse(reader.GetString(0)));
            await reader.DisposeAsync();
            await File.WriteAllTextAsync(Path.Combine(directory, "actual-events.xml"), new XElement("events", events).ToString());
            var completions = events.Where(item => (string?)item.Attribute("name") is "rpc_completed" or "sql_batch_completed")
                .Select(item => new
                {
                    sql = Value(item, "statement") is { Length: > 0 } statement ? statement : Value(item, "batch_text"),
                    durationMicroseconds = Number(item, "duration"), logicalReads = Number(item, "logical_reads"),
                    physicalReads = Number(item, "physical_reads"), writes = Number(item, "writes"), rows = Number(item, "row_count")
                }).ToArray();
            var plans = (from item in events
                where (string?)item.Attribute("name") == "query_post_execution_showplan"
                from statement in item.Descendants().Where(element => element.Name.LocalName == "StmtSimple")
                select new
                {
                    sql = (string?)statement.Attribute("StatementText") ?? item.Elements("action").SingleOrDefault(action => (string?)action.Attribute("name") == "sql_text")?.Element("value")?.Value,
                    statementType = (string?)statement.Attribute("StatementType"),
                    indexes = statement.Descendants().Where(element => element.Name.LocalName == "Object")
                        .Select(element => new { table = (string?)element.Attribute("Table"), index = (string?)element.Attribute("Index") }).ToArray(),
                    operators = statement.Descendants().Where(element => element.Name.LocalName == "RelOp")
                        .Select(element => new
                        {
                            physical = (string?)element.Attribute("PhysicalOp"), logical = (string?)element.Attribute("LogicalOp"),
                            indexes = element.Descendants().Where(child => child.Name.LocalName == "Object"
                                    && child.Ancestors().First(ancestor => ancestor.Name.LocalName == "RelOp") == element)
                                .Select(child => new { table = (string?)child.Attribute("Table"), index = (string?)child.Attribute("Index") }).ToArray(),
                            actualRows = element.Elements().Where(child => child.Name.LocalName == "RunTimeInformation")
                                .SelectMany(child => child.Elements()).Sum(child => (long?)child.Attribute("ActualRows") ?? 0)
                        }).ToArray()
                }).ToArray();
            if (plans.Length == 0 || completions.Length == 0) throw new InvalidOperationException("Actual SQL evidence was not captured.");
            var result = new
            {
                name, elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
                instrumentation = "query_post_execution_showplan + RPC/batch completion; elapsed includes instrumentation overhead",
                queryCount = completions.Length, logicalReads = completions.Sum(row => row.logicalReads),
                featureQueryCount = completions.Count(row => IsFeature(row.sql)),
                featureLogicalReads = completions.Where(row => IsFeature(row.sql)).Sum(row => row.logicalReads),
                lockObservation, completions, plans,
                rowSemantics = "Completion rows and runtime operator ActualRows are database outputs/affected rows; they are not a process memory measurement."
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "measurement.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return result;
        }
        finally
        {
            cancellation.Cancel();
            try { if (locks is not null) await locks; }
            finally
            {
                if (created)
                {
                    await using var drop = new SqlCommand($"IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name=N'{session}') DROP EVENT SESSION [{session}] ON SERVER", connection);
                    await drop.ExecuteNonQueryAsync();
                }
                foreach (var evidence in Directory.EnumerateFiles(eventDirectory)) File.Move(evidence, Path.Combine(directory, Path.GetFileName(evidence)), overwrite: true);
                Directory.Delete(eventDirectory, recursive: false);
            }
        }
    }

    private static bool IsFeature(string sql) => sql.Contains("FunctionPoint", StringComparison.Ordinal) || sql.Contains("RoleFunctionGrant", StringComparison.Ordinal);
    private static string Value(XElement item, string name) => item.Elements("data").SingleOrDefault(data => (string?)data.Attribute("name") == name)?.Element("value")?.Value ?? "";
    private static long Number(XElement item, string name) => long.TryParse(Value(item, name), out var value) ? value : 0;

    private static async Task<LockObservation> ObserveLocksAsync(string master, int databaseId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        var samples = 0;
        var maxLocks = 0;
        long maxWaitMilliseconds = 0;
        var waits = new HashSet<string>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using var command = new SqlCommand("SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_database_id=@id; SELECT wait_type, wait_time, blocking_session_id FROM sys.dm_exec_requests WHERE database_id=@id AND wait_type IS NOT NULL", connection);
                command.Parameters.AddWithValue("@id", databaseId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken)) maxLocks = Math.Max(maxLocks, reader.GetInt32(0));
                await reader.NextResultAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    waits.Add(reader.GetString(0) + ":blocker=" + reader.GetInt16(2));
                    maxWaitMilliseconds = Math.Max(maxWaitMilliseconds, reader.GetInt32(1));
                }
                samples++;
                await Task.Delay(100, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        return new(samples, 100, maxLocks, maxWaitMilliseconds, waits.Order().ToArray());
    }

    private sealed record LockObservation(int Samples, int RequestedIntervalMilliseconds, int MaxLocks, long MaxWaitMilliseconds, string[] ObservedWaits);
}
