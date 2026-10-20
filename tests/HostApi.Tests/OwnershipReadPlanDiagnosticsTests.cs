using System.Data;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace ICP.Platform.HostApi.Tests;

public sealed class OwnershipReadPlanDiagnosticsTests(ITestOutputHelper output)
{
    [Fact]
    public void BeforeClonesActualParametersAndConcurrentQueriesWithoutInspectingTerminalEvents()
    {
        using var environment = new EnvironmentScope();
        using var capture = new OwnershipReadPlanDiagnostics(output, "read-plan-control");
        using var connection = new SqlConnection("Data Source=(local);Initial Catalog=OwnershipReadPlanOnly");
        capture.Database = connection.Database; capture.Begin("transfer-3");
        Parallel.For(0, 4096, new ParallelOptions { MaxDegreeOfParallelism = 8 }, index =>
        {
            using var command = connection.CreateCommand(); command.CommandText = "SELECT [EID] FROM [DataScopeRuleNode] WHERE [ResourceKey]=@resource";
            command.Parameters.Add("@resource", SqlDbType.NVarChar, 64).Value = "resource-" + index;
            capture.OnNext(new KeyValuePair<string, object?>("Microsoft.Data.SqlClient.WriteCommandBefore", new { Command = command }));
            command.Parameters.Clear(); command.CommandText = "SELECT 1";
        });
        Assert.Equal(4096, capture.Captured.Count);
        Assert.Equal(4096, capture.Captured.Select(sample => Assert.Single(sample.Parameters).Value).Distinct().Count());
        Assert.All(capture.Captured, sample => Assert.Contains("[DataScopeRuleNode]", sample.Sql));
    }

    [Fact]
    public void MutationReplayExcludesChangedBusinessRowsAndCredentialReads()
    {
        using var environment = new EnvironmentScope();
        using var capture = new OwnershipReadPlanDiagnostics(output, "read-plan-control");
        using var connection = new SqlConnection("Data Source=(local);Initial Catalog=OwnershipReadPlanOnly");
        capture.Database = connection.Database; capture.Begin("initialize-101");
        Deliver("SELECT [OwnerId] FROM [WorkspaceFolder] WHERE EXISTS (SELECT 1 FROM [DataScopeObjectSubject])");
        Deliver("SELECT [EID] FROM [AuthenticationSession]");
        Deliver("SELECT [EID] FROM [IdentityAccount]");
        Deliver("SELECT [EID] FROM [DataScopeRuleNode]");
        Assert.Contains("[DataScopeRuleNode]", Assert.Single(capture.Captured).Sql);
        capture.Begin("pending", currentReadOnly: true);
        Deliver("SELECT [OwnerId] FROM [WorkspaceFolder]");
        Deliver("SELECT [TokenHash] FROM [AuthenticationSession]");
        Assert.Contains("[WorkspaceFolder]", Assert.Single(capture.Captured).Sql);
        void Deliver(string sql)
        {
            using var command = connection.CreateCommand(); command.CommandText = sql;
            capture.OnNext(new KeyValuePair<string, object?>("Microsoft.Data.SqlClient.WriteCommandBefore", new { Command = command }));
        }
    }

    internal sealed class EnvironmentScope : IDisposable
    {
        private readonly string? stage = Environment.GetEnvironmentVariable("NEWP_OWNERSHIP_COST_STAGE");
        private readonly string? io = Environment.GetEnvironmentVariable("NEWP_OWNERSHIP_COST_IO");
        internal EnvironmentScope()
        {
            Environment.SetEnvironmentVariable("NEWP_OWNERSHIP_COST_STAGE", "baseline");
            Environment.SetEnvironmentVariable("NEWP_OWNERSHIP_COST_IO", "1");
        }
        public void Dispose()
        {
            Environment.SetEnvironmentVariable("NEWP_OWNERSHIP_COST_STAGE", stage);
            Environment.SetEnvironmentVariable("NEWP_OWNERSHIP_COST_IO", io);
        }
    }
}
