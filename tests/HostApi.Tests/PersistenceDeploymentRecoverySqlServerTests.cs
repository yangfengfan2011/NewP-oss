using System.Text.Json;
using System.Text.Json.Nodes;
using ICP.Platform.HostApi.PluginActivation;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Composition;
using ICP.Platform.PluginPlatform.Lifecycle;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class PersistenceDeploymentRecoverySqlServerTests
{
    private const string PartTail = "20261008100000_Part_000002_NameIndex";
    private const string DocumentTail = "20261009130000_Document_000002_NameIndex";

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "新恢复夹具在准备回调或测试正文异常后精确归还自身数据库trigger并保留未登记对象")]
    public async Task RecoveryFixture_ExceptionsCleanOnlyRegisteredDatabaseTriggers()
    {
        const string own = "TR_RecoveryCleanupOwned";
        const string sentinel = "TR_RecoveryCleanupSentinel";
        try
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => FeatureCatalogHostFixture.CreateAsync([], async prepared =>
            {
                await Execute(prepared, $"CREATE TRIGGER {sentinel} ON DATABASE FOR CREATE_INDEX AS RETURN;");
                await prepared.CreateDatabaseTriggerAsync(own, $"CREATE TRIGGER {own} ON DATABASE FOR CREATE_INDEX AS RETURN;");
                throw new InvalidOperationException("RECOVERY_SETUP_CONTROLLED_FAILURE");
            }));
            Assert.Equal("RECOVERY_SETUP_CONTROLLED_FAILURE", failure.Message);
            await using var observer = await ReusableSqlServerDatabase.AcquireAsync();
            Assert.Equal(0, await TriggerCount(observer.ConnectionString, own));
            Assert.Equal(1, await TriggerCount(observer.ConnectionString, sentinel));
            await observer.CleanupDatabaseTriggersAsync([sentinel]);
            Assert.Equal(0, await TriggerCount(observer.ConnectionString, sentinel));
        }
        finally
        {
            await using var cleanup = await ReusableSqlServerDatabase.AcquireAsync();
            await cleanup.CleanupDatabaseTriggersAsync([own, sentinel]);
        }
        var bodyFailure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var fixture = await FeatureCatalogHostFixture.CreateAsync([], async prepared =>
            {
                await prepared.CreateDatabaseTriggerAsync(own, $"CREATE TRIGGER {own} ON DATABASE FOR CREATE_INDEX AS RETURN;");
                File.WriteAllText(prepared.CapsulePath, "{}");
            }, expectInitialStartupFailure: true);
            throw new InvalidOperationException("RECOVERY_BODY_CONTROLLED_FAILURE");
        });
        Assert.Equal("RECOVERY_BODY_CONTROLLED_FAILURE", bodyFailure.Message);
        await using var finalObserver = await ReusableSqlServerDatabase.AcquireAsync();
        Assert.Equal(0, await TriggerCount(finalObserver.ConnectionString, own));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "首次部署部分提交保留身份，拒绝缺失资产与未完成owner停用，再按真实历史恢复")]
    public async Task FirstDeployment_PartialUpReservesIdentityAndRecoversWithoutSuccessfulSnapshot()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([], async prepared =>
        {
            await DeferredModuleMigrationSqlServerTests.PrepareUpgrade(prepared, "Document", DocumentTail);
            Select(prepared, "Document", true);
            await prepared.CreateDatabaseTriggerAsync("TR_RecoveryIndexFailure", """
                CREATE TRIGGER TR_RecoveryIndexFailure ON DATABASE FOR CREATE_INDEX AS
                IF EVENTDATA().value('(/EVENT_INSTANCE/ObjectName)[1]', 'nvarchar(256)') = N'IX_Document_ENAME'
                    THROW 51065, 'RECOVERY_INDEX_FAILURE', 1;
                """);
        }, expectInitialStartupFailure: true);
        Assert.Contains("RECOVERY_INDEX_FAILURE", fixture.LastError);
        var before = await History(fixture);
        Assert.Equal(1, await Scalar(fixture, "SELECT COUNT(*) FROM dbo.__NewPMigrationHistory WHERE OwnerModuleId=N'Document';"));
        Assert.Equal(1, await Scalar(fixture, "SELECT COUNT(*) FROM sys.tables WHERE name=N'Part';"));
        var state = State(fixture);
        Assert.Null(state["successful"]);
        Assert.NotNull(state["pending"]);
        using (var store = Store(fixture))
        {
            var recovery = store.ReadRecoveryCandidate()!;
            Assert.Contains(recovery.Models, model => model.Name == "Part" && model.OwnerModuleId == "Part" && model.TableName == "Part");
            Assert.All(recovery.Streams, stream => Assert.NotNull(stream.AssemblyImage));
        }

        Select(fixture, "Workspace", false);
        Select(fixture, "Document", false);
        await fixture.StartAsync(expectFailure: true);
        Assert.Contains("PERSISTENCE_MIGRATION_FRONTIER_MISMATCH", fixture.LastError);
        Assert.Contains(DocumentTail, fixture.LastError);
        Assert.Equal(before, await History(fixture));

        Select(fixture, "Part", false);
        Select(fixture, "Document", true);
        var modelPath = Path.Combine(fixture.HostRoot, "modules/Document/metadata/models/Document.model.json");
        var capsulePath = Path.Combine(fixture.HostRoot, "modules/Document/plugin.json");
        var model = File.ReadAllBytes(modelPath);
        var capsule = File.ReadAllBytes(capsulePath);
        RenameDocumentToPart(fixture);
        await fixture.StartAsync(expectFailure: true);
        Assert.Contains("PERSISTENCE_DEPLOYED_IDENTITY_CONFLICT", fixture.LastError);
        Assert.Equal(before, await History(fixture));
        File.Delete(Path.Combine(Path.GetDirectoryName(modelPath)!, "Part.model.json"));
        File.WriteAllBytes(modelPath, model);
        File.WriteAllBytes(capsulePath, capsule);
        Select(fixture, "Part", true);

        var assetPath = Path.Combine(Path.GetDirectoryName(StatePath(fixture))!, State(fixture)["pending"]!.GetValue<string>() + ".json");
        File.Move(assetPath, assetPath + ".missing");
        await fixture.StartAsync(expectFailure: true);
        Assert.Contains("PERSISTENCE_DEPLOYMENT_ASSET_MISSING", fixture.LastError);
        Assert.Equal(before, await History(fixture));
        File.Move(assetPath + ".missing", assetPath);
        await Execute(fixture, "DROP TRIGGER TR_RecoveryIndexFailure ON DATABASE;");
        await fixture.StartAsync();
        var recovered = await History(fixture);
        Assert.Equal(before, recovered.Take(before.Length));
        Assert.Equal(before.Length + 1, recovered.Length);
        Assert.Equal(DocumentTail, JsonNode.Parse(recovered[^1])!["MigrationId"]!.GetValue<string>());
        Assert.Null(State(fixture)["pending"]);
        Assert.NotNull(State(fixture)["successful"]);
        await fixture.StopAsync();
        await fixture.StartAsync();
        Assert.Equal(recovered, await History(fixture));
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "迁移后实际schema drift阻断活动和成功记录，恢复后不重放已提交Up")]
    public async Task PostMigrationConsistencyFailure_PreservesCommittedHistoryUntilRealRetry()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        await fixture.StopAsync();
        await DeferredModuleMigrationSqlServerTests.PrepareUpgrade(fixture, "Document", DocumentTail);
        Select(fixture, "Document", true);
        var successful = State(fixture)["successful"]!.GetValue<string>();
        var active = LifecycleState(fixture)["activeSnapshotId"]!.GetValue<string>();
        var before = await History(fixture);
        await fixture.CreateDatabaseTriggerAsync("TR_RecoverySchemaDrift", """
            CREATE TRIGGER TR_RecoverySchemaDrift ON DATABASE FOR CREATE_INDEX AS
            IF EVENTDATA().value('(/EVENT_INSTANCE/ObjectName)[1]', 'nvarchar(256)') = N'IX_Document_ENAME'
                ALTER TABLE dbo.Document ADD RecoveryUnexpected bit NULL;
            """);
        await fixture.StartAsync(expectFailure: true);
        Assert.Contains("PERSISTENCE_SCHEMA_TARGET_MISMATCH", fixture.LastError);
        var committed = await History(fixture);
        Assert.Equal(before, committed.Take(before.Length));
        Assert.Equal(before.Length + 1, committed.Length);
        Assert.Equal(1, await Scalar(fixture, $"SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'{DocumentTail}';"));
        Assert.Equal(1, await Scalar(fixture, "SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Document') AND name=N'RecoveryUnexpected';"));
        Assert.Equal(successful, State(fixture)["successful"]!.GetValue<string>());
        Assert.NotNull(State(fixture)["pending"]);
        Assert.Equal(active, LifecycleState(fixture)["activeSnapshotId"]!.GetValue<string>());
        // 只清理由夹具注入的 drift；产品从未生成修复 DDL 或 Down。
        await Execute(fixture, "DROP TRIGGER TR_RecoverySchemaDrift ON DATABASE; ALTER TABLE dbo.Document DROP COLUMN RecoveryUnexpected;");
        await fixture.StartAsync();
        Assert.Equal(committed, await History(fixture));
        Assert.Null(State(fixture)["pending"]);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "活动指针提交失败保持旧活动与success，实际已提交步骤由新进程继续核对")]
    public async Task ActiveCommitFailure_DoesNotPublishCandidateAndRetryDoesNotReplayDdl()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        await fixture.StopAsync();
        await DeferredModuleMigrationSqlServerTests.PrepareUpgrade(fixture, "Document", DocumentTail);
        Select(fixture, "Document", true);
        var successful = State(fixture)["successful"]!.GetValue<string>();
        var oldState = File.ReadAllBytes(LifecycleStatePath(fixture));
        using (var locked = new FileStream(LifecycleStatePath(fixture), FileMode.Open, FileAccess.Read, FileShare.Read))
            await fixture.StartAsync(expectFailure: true);
        Assert.Contains("PLUGIN_ACTIVATION_ACTIVE_COMMIT_FAILED", fixture.LastError);
        Assert.Equal(oldState, File.ReadAllBytes(LifecycleStatePath(fixture)));
        Assert.Equal(successful, State(fixture)["successful"]!.GetValue<string>());
        Assert.NotNull(State(fixture)["pending"]);
        var committed = await History(fixture);
        Assert.Equal(DocumentTail, JsonNode.Parse(committed[^1])!["MigrationId"]!.GetValue<string>());
        // 旧已确认 LKG 仍可用于发行 Host 的离线选择；这里选未升级的真实 Audit，不受 builtin 旧版本库存限制。
        var rollback = await Rollback(fixture, "Audit");
        Assert.Equal(0, rollback.ExitCode);
        Assert.Contains("rollback:PendingRestart", rollback.Output);
        var selection = Assert.Single(LifecycleState(fixture)["desiredModules"]!.AsArray(), item => item!["moduleId"]!.GetValue<string>() == "Audit");
        Assert.Equal("1.0.0", selection!["version"]!.GetValue<string>());
        await fixture.StartAsync();
        Assert.Equal(committed, await History(fixture));
        Assert.Null(State(fixture)["pending"]);
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "raw active已写但最终success失败时真实生命周期和发行离线rollback均不能接受未确认候选")]
    public async Task FinalConfirmationFailure_HidesRawActiveAndOfflineRollbackUntilRecovered()
    {
        FileStream? stateLock = null;
        Task? preparation = null;
        try
        {
            await using var fixture = await FeatureCatalogHostFixture.CreateAsync([], async prepared =>
            {
                var connection = new SqlConnection(prepared.ConnectionString);
                await connection.OpenAsync();
                var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
                await Lock(connection, transaction);
                preparation = Task.Run(async () =>
                {
                    try
                    {
                        await Until(() => Directory.Exists(Path.Combine(prepared.PluginRoot, "persistence"))
                            && Directory.GetFiles(Path.Combine(prepared.PluginRoot, "persistence"), "state.json", SearchOption.AllDirectories).Length == 1);
                        stateLock = new FileStream(StatePath(prepared), FileMode.Open, FileAccess.Read, FileShare.Read);
                        await transaction.CommitAsync();
                    }
                    finally { await transaction.DisposeAsync(); await connection.DisposeAsync(); }
                });
            }, expectInitialStartupFailure: true);
            await preparation!;
            Assert.Contains("CompleteCandidate", fixture.LastError);
            Assert.Null(State(fixture)["successful"]);
            Assert.NotNull(LifecycleState(fixture)["activeSnapshotId"]);
            using (var store = Store(fixture))
            {
                var manager = new PluginLifecycleManager(new PluginLifecycleStateStore(LifecycleRoot(fixture),
                    HostPluginActivationDeployment.WithDeploymentStore(
                        Path.Combine(fixture.HostRoot, "server/platform-modeling/metadata/storage-types.json"), PersistenceProvider.SqlServer, store)));
                Assert.Null(manager.Resolve([]).ActiveSnapshot);
                Assert.Null(manager.Resolve([]).LastKnownGoodSnapshot);
            }
            stateLock!.Dispose(); stateLock = null;
            var offline = await Rollback(fixture, "Document");
            Assert.NotEqual(0, offline.ExitCode);
            Assert.Contains("PLUGIN_LAST_KNOWN_GOOD_MISSING", offline.Output);
            var committed = await History(fixture);
            await fixture.StartAsync();
            Assert.Equal(committed, await History(fixture));
            Assert.Null(State(fixture)["pending"]);
            await fixture.StopAsync();
            using var confirmedStore = Store(fixture);
            var confirmedManager = new PluginLifecycleManager(new PluginLifecycleStateStore(LifecycleRoot(fixture),
                HostPluginActivationDeployment.WithDeploymentStore(
                    Path.Combine(fixture.HostRoot, "server/platform-modeling/metadata/storage-types.json"), PersistenceProvider.SqlServer, confirmedStore)));
            Assert.NotNull(confirmedManager.Resolve([]).ActiveSnapshot);
            Assert.NotNull(confirmedManager.Resolve([]).LastKnownGoodSnapshot);
        }
        finally { stateLock?.Dispose(); if (preparation is not null) await preparation; }
    }

    [EnvironmentGatedFact("NEWP_RUN_WORKSPACE_SQLSERVER_INTEGRATION", "真实步骤提交后强制终止所属Host树，跨进程恢复保留原Position与摘要且不重放步骤")]
    public async Task ForcedProcessInterruption_AfterCommittedStepRecoversRealOwnerPrefix()
    {
        await using var fixture = await FeatureCatalogHostFixture.CreateAsync([]);
        await fixture.StopAsync();
        await DeferredModuleMigrationSqlServerTests.PrepareUpgrade(fixture, "Part", PartTail);
        await DeferredModuleMigrationSqlServerTests.PrepareUpgrade(fixture, "Document", DocumentTail);
        Select(fixture, "Part", true);
        Select(fixture, "Document", true);
        var successful = State(fixture)["successful"]!.GetValue<string>();
        var active = LifecycleState(fixture)["activeSnapshotId"]!.GetValue<string>();
        await fixture.CreateDatabaseTriggerAsync("TR_RecoveryPause", """
            CREATE TRIGGER TR_RecoveryPause ON DATABASE FOR CREATE_INDEX AS
            IF EVENTDATA().value('(/EVENT_INSTANCE/ObjectName)[1]', 'nvarchar(256)') = N'IX_Document_ENAME'
                WAITFOR DELAY '00:00:20';
            """);
        var startup = fixture.StartAsync(expectFailure: true);
        await Until(async () => await Scalar(fixture, $"SELECT COUNT(*) FROM dbo.__NewPMigrationHistory WHERE MigrationId=N'{PartTail}';") == 1);
        var prefix = await History(fixture);
        Assert.Equal(PartTail, JsonNode.Parse(prefix[^1])!["MigrationId"]!.GetValue<string>());
        await fixture.InterruptStartupAsync();
        await startup;
        Assert.Equal(prefix, await History(fixture));
        Assert.Equal(successful, State(fixture)["successful"]!.GetValue<string>());
        Assert.Equal(active, LifecycleState(fixture)["activeSnapshotId"]!.GetValue<string>());
        Assert.NotNull(State(fixture)["pending"]);
        // pending 和旧 successful 都不能掩盖实际数据库超出候选：只注入并归还这一条夹具未知 EF 成员。
        await Execute(fixture, "INSERT INTO dbo.__EFMigrationsHistory(MigrationId,ProductVersion) VALUES(N'99991231235959_RecoveryUnknown',N'10.0.1');");
        await fixture.StartAsync(expectFailure: true);
        Assert.Contains("PERSISTENCE_MIGRATION_HISTORY_UNKNOWN", fixture.LastError);
        Assert.Equal(prefix, await History(fixture));
        Assert.Equal(1, await Scalar(fixture, "SELECT COUNT(*) FROM sys.indexes WHERE name=N'IX_Part_ENAME';"));
        Assert.Equal(0, await Scalar(fixture, "SELECT COUNT(*) FROM sys.indexes WHERE name=N'IX_Document_ENAME';"));
        await Execute(fixture, "DELETE FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'99991231235959_RecoveryUnknown';");
        await Execute(fixture, "DROP TRIGGER TR_RecoveryPause ON DATABASE;");
        await fixture.StartAsync();
        var recovered = await History(fixture);
        Assert.Equal(prefix, recovered.Take(prefix.Length));
        Assert.Equal(prefix.Length + 1, recovered.Length);
        Assert.Equal(DocumentTail, JsonNode.Parse(recovered[^1])!["MigrationId"]!.GetValue<string>());
        await fixture.StopAsync();
        await fixture.StartAsync();
        Assert.Equal(recovered, await History(fixture));
    }

    private static string LifecycleRoot(FeatureCatalogHostFixture fixture) => fixture.PluginRoot;
    private static string LifecycleStatePath(FeatureCatalogHostFixture fixture) => Path.Combine(LifecycleRoot(fixture), "lifecycle/v2/state.json");
    private static JsonNode LifecycleState(FeatureCatalogHostFixture fixture) => JsonNode.Parse(File.ReadAllText(LifecycleStatePath(fixture)))!;
    private static string StatePath(FeatureCatalogHostFixture fixture) => Assert.Single(Directory.GetFiles(Path.Combine(fixture.PluginRoot, "persistence"), "state.json", SearchOption.AllDirectories));
    private static JsonNode State(FeatureCatalogHostFixture fixture) => JsonNode.Parse(File.ReadAllText(StatePath(fixture)))!;
    private static PersistenceDeploymentStore Store(FeatureCatalogHostFixture fixture)
    {
        var connection = new SqlConnectionStringBuilder(fixture.ConnectionString);
        return new PersistenceDeploymentStore(Path.Combine(fixture.PluginRoot, "persistence"), PersistenceProvider.SqlServer,
            connection.DataSource.ToUpperInvariant() + "|" + connection.InitialCatalog.ToUpperInvariant());
    }
    private static void RenameDocumentToPart(FeatureCatalogHostFixture fixture)
    {
        var root = Path.Combine(fixture.HostRoot, "modules", "Document");
        var path = Path.Combine(root, "metadata/models/Document.model.json");
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        document["Part"] = document["Document"]!.DeepClone();
        document.Remove("Document");
        File.WriteAllText(Path.Combine(root, "metadata/models/Part.model.json"), document.ToJsonString());
        File.Delete(path);
        var capsulePath = Path.Combine(root, "plugin.json");
        var capsule = JsonNode.Parse(File.ReadAllText(capsulePath))!;
        capsule["persistence"]!["models"] = new JsonArray("metadata/models/Part.model.json");
        File.WriteAllText(capsulePath, capsule.ToJsonString());
    }
    private static async Task<(int ExitCode, string Output)> Rollback(FeatureCatalogHostFixture fixture, string module)
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        { WorkingDirectory = fixture.HostRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { Path.Combine(fixture.HostRoot, "HostApi.dll"), "plugin-deploy", "rollback", "--module-id", module }) start.ArgumentList.Add(argument);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["Platform__PluginInstallRoot"] = fixture.PluginRoot;
        start.Environment["Platform__ExternalPluginsEnabled"] = "false";
        start.Environment["ConnectionStrings__SqlServer"] = fixture.ConnectionString;
        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        var output = (await stdout + await stderr).Replace(fixture.ConnectionString, "[connection]").Replace(fixture.AdministratorPassword, "[credential]");
        await File.WriteAllTextAsync(Path.Combine(fixture.EvidenceRoot, "rollback-" + Guid.NewGuid().ToString("N") + ".log"), output);
        return (process.ExitCode, output);
    }
    private static void Select(FeatureCatalogHostFixture fixture, string module, bool enabled) => OptionalComponentLifecycleSqlServerTests.SetEnabled(fixture, module, enabled);
    private static Task Until(Func<bool> predicate) => Until(() => Task.FromResult(predicate()));
    private static async Task Until(Func<Task<bool>> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline) { if (await predicate()) return; await Task.Delay(50); }
        throw new TimeoutException("Real Host did not reach the recovery checkpoint.");
    }
    private static async Task Lock(SqlConnection connection, SqlTransaction transaction)
    {
        await using var command = new SqlCommand("DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=N'NewP.PersistenceBootstrap', @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=0; SELECT @result;", connection, transaction);
        Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync()) >= 0);
    }
    private static async Task Execute(FeatureCatalogHostFixture fixture, string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<int> Scalar(FeatureCatalogHostFixture fixture, string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
    private static async Task<int> TriggerCount(string connectionString, string name)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(*) FROM sys.triggers WHERE parent_class=0 AND name=@name;", connection);
        command.Parameters.AddWithValue("@name", name);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
    private static async Task<string[]> History(FeatureCatalogHostFixture fixture)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT * FROM dbo.__NewPMigrationHistory ORDER BY Position FOR JSON PATH;", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var text = new System.Text.StringBuilder();
        while (await reader.ReadAsync()) text.Append(reader.GetString(0));
        return JsonSerializer.Deserialize<JsonElement[]>(text.ToString())!.Select(row => row.GetRawText()).ToArray();
    }
}
