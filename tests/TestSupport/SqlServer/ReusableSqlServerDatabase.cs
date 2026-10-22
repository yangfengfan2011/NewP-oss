using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace ICP.Platform.Testing;

// 固定两库各自持有跨进程会话锁；只有已隔离的 fixture 显式使用第二库。
internal sealed class ReusableSqlServerDatabase : IAsyncDisposable
{
    internal const string Catalog = "NewP_Test";
    private const string ParallelCatalog = "NewP_Test_2";
    internal const string ConnectionVariable = "NEWP_TEST_SQLSERVER_CONNECTION_STRING";
    private const string Marker = "NewP.AutomatedTests";
    private readonly SqlConnection _lease;
    private bool _disposed;
    internal string ConnectionString { get; }
    internal string CatalogName { get; }

    private ReusableSqlServerDatabase(SqlConnection lease, string connectionString, string catalog)
    {
        _lease = lease;
        CatalogName = catalog;
        ConnectionString = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = catalog }.ConnectionString;
    }

    internal static string ResolveConnection(string? repositoryRoot = null, Func<string, string?>? readEnvironment = null)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;
        var dedicated = readEnvironment(ConnectionVariable);
        var configured = dedicated;
        if (string.IsNullOrWhiteSpace(configured)) configured = readEnvironment("NEWP_DEV_SQLSERVER_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured))
        {
            var root = new DirectoryInfo(repositoryRoot ?? AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "ICP-Platform.sln"))) root = root.Parent;
            if (root is null) throw new InvalidOperationException("Test database repository root was not found.");
            using var configuration = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "apps", "host-api", "appsettings.Development.json")));
            configured = configuration.RootElement.GetProperty("ConnectionStrings").GetProperty("SqlServer").GetString();
        }
        return UseTestCatalog(configured!, !string.IsNullOrWhiteSpace(dedicated));
    }

    internal static string UseTestCatalog(string configured, bool dedicated)
    {
        var builder = new SqlConnectionStringBuilder(configured);
        if (dedicated && !string.Equals(builder.InitialCatalog, Catalog, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{ConnectionVariable} must select {Catalog}.");
        if (!string.IsNullOrEmpty(builder.AttachDBFilename))
            throw new InvalidOperationException("Test database connections cannot attach database files.");
        builder.InitialCatalog = Catalog;
        builder.Pooling = false;
        builder.ConnectRetryCount = 0;
        return builder.ConnectionString;
    }

    internal static async Task<ReusableSqlServerDatabase> AcquireAsync(string? repositoryRoot = null,
        CancellationToken cancellationToken = default, int lockTimeoutMilliseconds = 900_000, bool allowParallel = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lockTimeoutMilliseconds);
        var configured = ResolveConnection(repositoryRoot);
        var admin = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        var lease = new SqlConnection(admin.ConnectionString);
        try
        {
            await lease.OpenAsync(cancellationToken);
            var catalog = await AcquireCatalogAsync(lease, allowParallel, lockTimeoutMilliseconds, cancellationToken);
            var database = new ReusableSqlServerDatabase(lease, configured, catalog);
            await database.EnsureCatalogAsync(cancellationToken);
            await database.ResetSchemaAsync(cancellationToken);
            return database;
        }
        catch
        {
            await lease.DisposeAsync();
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }

    // 只对租约忙进行有截止时间的等待；SQL 错误、死锁和取消直接传播，不创建更多 catalog。
    private static async Task<string> AcquireCatalogAsync(SqlConnection lease, bool allowParallel, int timeout, CancellationToken token)
    {
        if (!allowParallel)
        {
            if (await TryAcquireAsync(Catalog, timeout)) return Catalog;
            throw new TimeoutException("NewP_Test is in use by another test fixture; the database was not changed.");
        }

        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            if (await TryAcquireAsync(Catalog, 0)) return Catalog;
            if (await TryAcquireAsync(ParallelCatalog, 0)) return ParallelCatalog;
            var remaining = timeout - elapsed.ElapsedMilliseconds;
            if (remaining <= 0)
                throw new TimeoutException("Both reusable SQL test databases are in use; no database was changed.");
            await Task.Delay((int)Math.Min(100, remaining), token);
        }

        async Task<bool> TryAcquireAsync(string catalog, int waitMilliseconds)
        {
            token.ThrowIfCancellationRequested();
            await using var command = lease.CreateCommand();
            command.CommandTimeout = Math.Max(30, waitMilliseconds / 1000 + 10);
            command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=@timeout; SELECT @result;";
            command.Parameters.AddWithValue("@resource", catalog + ".fixture");
            command.Parameters.AddWithValue("@timeout", waitMilliseconds);
            var result = Convert.ToInt32(await command.ExecuteScalarAsync(token));
            if (result >= 0) return true;
            if (result == -1) return false;
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException($"Test database lease acquisition failed with SQL application-lock result {result}.");
        }
    }

    // 只在不存在时建库。已有同名库必须具有专用测试标记，不能自动接管开发库。
    private async Task EnsureCatalogAsync(CancellationToken token)
    {
        await using var command = _lease.CreateCommand();
        command.CommandText = "SELECT DB_ID(@catalog);";
        command.Parameters.AddWithValue("@catalog", CatalogName);
        if (await command.ExecuteScalarAsync(token) is not DBNull) return;
        command.CommandText = $"CREATE DATABASE [{CatalogName}];";
        await command.ExecuteNonQueryAsync(token);
        command.CommandText = $"EXEC [{CatalogName}].sys.sp_addextendedproperty @name=N'NewP.AutomatedTests', @value=N'disposable-schema';";
        await command.ExecuteNonQueryAsync(token);
    }

    // 各 fixture 的模型子集不同，保留 catalog、重置 schema，继续由原 fixture/Host 建表与迁移。
    // 标记位于数据库扩展属性，不会混进 EF EnsureCreated 的用户表检测。
    private async Task ResetSchemaAsync(CancellationToken token)
    {
        await using var command = _lease.CreateCommand();
        command.CommandTimeout = 120;
        command.Parameters.AddWithValue("@resource", CatalogName + ".fixture");
        command.CommandText = $$"""
            IF DB_NAME() <> N'master' OR COALESCE(APPLOCK_MODE(N'public',@resource,N'Session'),N'NoLock') <> N'Exclusive'
                THROW 51000,N'Test fixture lease is not held; refusing cleanup.',1;
            USE [{{CatalogName}}];
            IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'{{Marker}}' AND CONVERT(nvarchar(128),value)=N'disposable-schema')
                THROW 51000, N'Test database is not marked as an automated test database; refusing cleanup.', 1;
            IF EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped=0 AND (temporal_type<>0 OR is_memory_optimized=1))
                THROW 51000, N'Unsupported test database table type; refusing cleanup.', 1;
            BEGIN TRANSACTION;
            DECLARE @sql nvarchar(max)=N'';
            SELECT @sql+=N'ALTER TABLE '+QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id))+N'.'+QUOTENAME(OBJECT_NAME(parent_object_id))+N' DROP CONSTRAINT '+QUOTENAME(name)+N';' FROM sys.foreign_keys;
            EXEC sys.sp_executesql @sql;
            SET @sql=N'';
            SELECT @sql+=N'DROP TABLE '+QUOTENAME(SCHEMA_NAME(schema_id))+N'.'+QUOTENAME(name)+N';' FROM sys.tables WHERE is_ms_shipped=0;
            EXEC sys.sp_executesql @sql;
            SET @sql=N'';
            SELECT @sql+=N'DROP SEQUENCE '+QUOTENAME(SCHEMA_NAME(schema_id))+N'.'+QUOTENAME(name)+N';' FROM sys.sequences;
            EXEC sys.sp_executesql @sql;
            COMMIT;
            USE [master];
            """;
        try { await command.ExecuteNonQueryAsync(token); }
        catch
        {
            command.CommandText = "IF @@TRANCOUNT > 0 ROLLBACK; USE [master];";
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            throw;
        }
    }

    // 只清理调用 fixture 明确登记的数据库级 trigger；原 master session 租约和 disposable 标记缺一不可。
    internal async Task CleanupDatabaseTriggersAsync(IReadOnlyCollection<string> triggerNames)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var name in triggerNames.Distinct(StringComparer.Ordinal))
        {
            await using var command = _lease.CreateCommand();
            command.CommandText = $$"""
                IF DB_NAME() <> N'master' OR COALESCE(APPLOCK_MODE(N'public',@resource,N'Session'),N'NoLock') <> N'Exclusive'
                    THROW 51000,N'Test fixture lease is not held; refusing trigger cleanup.',1;
                IF NOT EXISTS (SELECT 1 FROM [{{CatalogName}}].sys.extended_properties WHERE class=0
                    AND name=N'NewP.AutomatedTests' AND CONVERT(nvarchar(128),value)=N'disposable-schema')
                    THROW 51000,N'Test database marker missing; refusing trigger cleanup.',1;
                IF EXISTS (SELECT 1 FROM [{{CatalogName}}].sys.triggers WHERE parent_class=0 AND name=@name)
                BEGIN
                    DECLARE @sql nvarchar(max)=N'USE [{{CatalogName}}]; DROP TRIGGER '+QUOTENAME(@name)+N' ON DATABASE;';
                    EXEC sys.sp_executesql @sql;
                END;
                """;
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@resource", CatalogName + ".fixture");
            await command.ExecuteNonQueryAsync();
        }
    }

    // 归属检查或停止 Host 失败时仅结束持锁会话，保留 schema 供诊断；下一次租用仍执行正常初始化。
    internal async ValueTask ReleaseAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _lease.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try { await ResetSchemaAsync(CancellationToken.None); }
        finally { await ReleaseAsync(); }
    }
}
