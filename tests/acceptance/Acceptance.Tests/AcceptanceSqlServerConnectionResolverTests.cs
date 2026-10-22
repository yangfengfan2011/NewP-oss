using ICP.Platform.Acceptance;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class AcceptanceSqlServerConnectionResolverTests
{
    [Fact]
    public void Resolve_PrefersAcceptanceEnvironmentAndNormalizesAdminCatalog()
    {
        using var repository = TemporaryRepository.Create(
            "Server=configured;Database=configured-db;Integrated Security=true;TrustServerCertificate=true");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [AcceptanceSqlServerConnectionResolver.AcceptanceEnvironmentVariable] =
                "Server=acceptance;Database=acceptance-db;Integrated Security=true;TrustServerCertificate=true",
            [AcceptanceSqlServerConnectionResolver.DevelopmentEnvironmentVariable] =
                "Server=development;Database=development-db;Integrated Security=true;TrustServerCertificate=true"
        };

        var result = AcceptanceSqlServerConnectionResolver.Resolve(repository.RootPath, environment.GetValueOrDefault);

        var builder = new SqlConnectionStringBuilder(result.AdminConnectionString);
        Assert.Equal(AcceptanceSqlServerConnectionResolver.AcceptanceEnvironmentVariable, result.SourceName);
        Assert.Equal("acceptance", builder.DataSource);
        Assert.Equal("master", builder.InitialCatalog);
        Assert.False(builder.Pooling);
    }

    [Fact]
    public void Resolve_FallsBackToDevelopmentEnvironmentThenLocalConfiguration()
    {
        using var repository = TemporaryRepository.Create(
            "Server=configured;Database=configured-db;Integrated Security=true;TrustServerCertificate=true");
        var developmentEnvironment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [AcceptanceSqlServerConnectionResolver.DevelopmentEnvironmentVariable] =
                "Server=development;Database=development-db;Integrated Security=true;TrustServerCertificate=true"
        };

        var fromEnvironment = AcceptanceSqlServerConnectionResolver.Resolve(
            repository.RootPath,
            developmentEnvironment.GetValueOrDefault);
        var fromConfiguration = AcceptanceSqlServerConnectionResolver.Resolve(repository.RootPath, _ => null);

        Assert.Equal(AcceptanceSqlServerConnectionResolver.DevelopmentEnvironmentVariable, fromEnvironment.SourceName);
        Assert.Equal("development", new SqlConnectionStringBuilder(fromEnvironment.AdminConnectionString).DataSource);
        Assert.Equal(AcceptanceSqlServerConnectionResolver.DevelopmentConfigurationSource, fromConfiguration.SourceName);
        Assert.Equal("configured", new SqlConnectionStringBuilder(fromConfiguration.AdminConnectionString).DataSource);
    }

    [Fact]
    public void Resolve_WhenNoSourceExists_FailsBeforeGateWorkStarts()
    {
        using var repository = TemporaryRepository.Create(null);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AcceptanceSqlServerConnectionResolver.Resolve(repository.RootPath, _ => null));

        Assert.StartsWith("ACC_SQLSERVER_ADMIN_CONNECTION_MISSING", exception.Message, StringComparison.Ordinal);
        Assert.Contains(AcceptanceSqlServerConnectionResolver.AcceptanceEnvironmentVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains(AcceptanceSqlServerConnectionResolver.DevelopmentEnvironmentVariable, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WhenSourceIsInvalid_DoesNotExposeConnectionSecret()
    {
        using var repository = TemporaryRepository.Create(null);
        const string secret = "not-for-output";
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [AcceptanceSqlServerConnectionResolver.AcceptanceEnvironmentVariable] =
                $"Server=local;Password={secret};Unsupported Keyword=value"
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AcceptanceSqlServerConnectionResolver.Resolve(repository.RootPath, environment.GetValueOrDefault));

        Assert.StartsWith("ACC_SQLSERVER_ADMIN_CONNECTION_INVALID", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ResultToString_DoesNotExposeConnectionSecret()
    {
        using var repository = TemporaryRepository.Create(null);
        const string secret = "not-for-output";
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [AcceptanceSqlServerConnectionResolver.AcceptanceEnvironmentVariable] =
                $"Server=local;Database=acceptance;User ID=sa;Password={secret};TrustServerCertificate=true"
        };

        var result = AcceptanceSqlServerConnectionResolver.Resolve(repository.RootPath, environment.GetValueOrDefault);

        Assert.DoesNotContain(secret, result.ToString(), StringComparison.Ordinal);
    }

    private sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(string rootPath)
        {
            RootPath = rootPath;
        }

        internal string RootPath { get; }

        internal static TemporaryRepository Create(string? sqlServerConnection)
        {
            var rootPath = Path.Combine(
                Path.GetTempPath(),
                "newp-acceptance-connection-tests",
                Guid.NewGuid().ToString("N"));
            var hostPath = Path.Combine(rootPath, "apps", "host-api");
            Directory.CreateDirectory(hostPath);
            if (sqlServerConnection is not null)
            {
                File.WriteAllText(
                    Path.Combine(hostPath, "appsettings.Development.json"),
                    JsonSerializer.Serialize(new
                    {
                        ConnectionStrings = new
                        {
                            SqlServer = sqlServerConnection
                        }
                    }));
            }

            return new TemporaryRepository(rootPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
