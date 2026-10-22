using System.Data;
using System.Text.Json;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.HostApi.Tests;

public sealed class OwnershipReadPlanDiagnosticsSqlServerTests(ITestOutputHelper output)
{
    [EnvironmentGatedFact("NEWP_RUN_OWNERSHIP_IO_CONTROL", "真实实际参数SELECT复用已有只读Explain工具，原Queue数和结果规模独立核对")]
    public async Task ExistingExplainProfilesTheCapturedActualReadAndPreservesItsStatementAndParameters()
    {
        await using var database = await ReusableSqlServerDatabase.AcquireAsync(allowParallel: true);
        var table = "OwnershipReadPlanControl_" + Guid.NewGuid().ToString("N");
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        using var setting = connection.CreateCommand();
        try
        {
            setting.CommandText = "CREATE TABLE [dbo].[" + table + "] ([Value] int NOT NULL); INSERT INTO [dbo].[" + table + "] VALUES (1), (2);";
            await setting.ExecuteNonQueryAsync();
            using var environment = new OwnershipReadPlanDiagnosticsTests.EnvironmentScope();
            using var capture = new OwnershipReadPlanDiagnostics(output, "read-plan-sql-control") { Database = connection.Database };
            using var original = new EntityOwnershipTransferCapacitySqlServerHttpApiTests.TransferCostObservation { Database = connection.Database };
            using var command = connection.CreateCommand();
            var sql = "SELECT [Value] FROM [dbo].[" + table + "] WHERE [Value] > @minimum";
            command.CommandText = sql; command.Parameters.Add("@minimum", SqlDbType.Int).Value = 0;
            original.Begin(); capture.Begin("actual-two-row-read", currentReadOnly: true);
            await using (var reader = await command.ExecuteReaderAsync())
            {
                var rows = new List<int>();
                while (await reader.ReadAsync()) rows.Add(reader.GetInt32(0));
                Assert.Equal(new[] { 1, 2 }, rows);
            }
            original.End();
            Assert.Equal(1, original.CommandCount);
            var sample = Assert.Single(capture.Captured);
            Assert.Equal(sql, sample.Sql); Assert.Equal(0, Assert.Single(sample.Parameters).Value);
            await capture.EndAsync(0, original.CommandCount, database.ConnectionString);
            Assert.Equal(sql, command.CommandText); Assert.Equal(0, command.Parameters[0].Value);
            using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(capture.OutputDirectory, "actual-two-row-read.json")));
            Assert.Equal(1, result.RootElement.GetProperty("successfulSqlCommands").GetInt32());
            var plan = Assert.Single(result.RootElement.GetProperty("plans").EnumerateArray());
            Assert.Equal(2, plan.GetProperty("ReturnedRows").GetInt32());
            Assert.NotEmpty(plan.GetProperty("Statistics").EnumerateArray());
            Assert.NotEmpty(plan.GetProperty("Plans").EnumerateArray());
            output.WriteLine("Original Queue SQL1; actual parameters unchanged; independent SELECT IO/XML/rows2 captured; diagnostics excluded from request count.");
        }
        finally
        {
            setting.CommandText = "DROP TABLE IF EXISTS [dbo].[" + table + "];";
            await setting.ExecuteNonQueryAsync();
        }
    }
}
