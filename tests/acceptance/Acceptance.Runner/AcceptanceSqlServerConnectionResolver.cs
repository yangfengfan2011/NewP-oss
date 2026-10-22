using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace ICP.Platform.Acceptance;

// 该类型承载凭据，刻意不用 record，避免编译器生成的 ToString() 展开连接字符串。
internal sealed class AcceptanceSqlServerAdminConnection
{
    internal AcceptanceSqlServerAdminConnection(
        string configuredConnectionString,
        string adminConnectionString,
        string password,
        string sourceName)
    {
        ConfiguredConnectionString = configuredConnectionString;
        AdminConnectionString = adminConnectionString;
        Password = password;
        SourceName = sourceName;
    }

    internal string ConfiguredConnectionString { get; }

    internal string AdminConnectionString { get; }

    internal string Password { get; }

    internal string SourceName { get; }
}

internal static class AcceptanceSqlServerConnectionResolver
{
    internal const string AcceptanceEnvironmentVariable = "NEWP_ACCEPTANCE_SQLSERVER_ADMIN_CONNECTION";
    internal const string DevelopmentEnvironmentVariable = "NEWP_DEV_SQLSERVER_CONNECTION_STRING";
    internal const string DevelopmentConfigurationSource = "apps/host-api/appsettings.Development.json:ConnectionStrings:SqlServer";

    internal static AcceptanceSqlServerAdminConnection Resolve(string repositoryRoot)
    {
        return Resolve(repositoryRoot, Environment.GetEnvironmentVariable);
    }

    internal static AcceptanceSqlServerAdminConnection Resolve(
        string repositoryRoot,
        Func<string, string?> readEnvironmentVariable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);

        var configured = FirstConfigured(
            (AcceptanceEnvironmentVariable, readEnvironmentVariable(AcceptanceEnvironmentVariable)),
            (DevelopmentEnvironmentVariable, readEnvironmentVariable(DevelopmentEnvironmentVariable)));

        if (configured is null)
        {
            configured = ReadDevelopmentConfiguration(repositoryRoot);
        }

        if (configured is null)
        {
            throw new InvalidOperationException(
                $"ACC_SQLSERVER_ADMIN_CONNECTION_MISSING: set {AcceptanceEnvironmentVariable}, " +
                $"set {DevelopmentEnvironmentVariable}, or configure {DevelopmentConfigurationSource}.");
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(configured.Value.ConnectionString)
            {
                InitialCatalog = "master",
                Pooling = false
            };

            return new AcceptanceSqlServerAdminConnection(
                configured.Value.ConnectionString,
                builder.ConnectionString,
                builder.Password,
                configured.Value.SourceName);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"ACC_SQLSERVER_ADMIN_CONNECTION_INVALID: connection source '{configured.Value.SourceName}' is invalid.",
                exception);
        }
    }

    private static (string SourceName, string ConnectionString)? FirstConfigured(
        params (string SourceName, string? ConnectionString)[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate.ConnectionString))
            {
                return (candidate.SourceName, candidate.ConnectionString);
            }
        }

        return null;
    }

    private static (string SourceName, string ConnectionString)? ReadDevelopmentConfiguration(string repositoryRoot)
    {
        var configurationPath = Path.Combine(
            Path.GetFullPath(repositoryRoot),
            "apps",
            "host-api",
            "appsettings.Development.json");
        if (!File.Exists(configurationPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configurationPath));
            if (!document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings)
                || !connectionStrings.TryGetProperty("SqlServer", out var sqlServer)
                || sqlServer.ValueKind is not JsonValueKind.String
                || string.IsNullOrWhiteSpace(sqlServer.GetString()))
            {
                return null;
            }

            return (DevelopmentConfigurationSource, sqlServer.GetString()!);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"ACC_SQLSERVER_ADMIN_CONNECTION_INVALID: connection source '{DevelopmentConfigurationSource}' is not valid JSON.",
                exception);
        }
    }
}
