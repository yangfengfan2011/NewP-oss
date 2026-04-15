using ICP.Platform.Tools.DevSchemaSync.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ICP.Platform.DevSchemaSync.Tests.Configuration;

public class DevSchemaSyncConfigurationBootstrapTests
{
    [Fact]
    public void FromConfiguration_UsesSqlServerProviderAndConnectionStringFromMemoryConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "SqlServer",
                ["ConnectionStrings:SqlServer"] = "Server=(localdb)\\MSSQLLocalDB;Database=ICP_Test;Trusted_Connection=True;TrustServerCertificate=True;"
            })
            .Build();

        var context = DevSchemaSyncExecutionContext.FromConfiguration(configuration);

        Assert.Equal("SqlServer", context.Provider);
        Assert.Equal("Server=(localdb)\\MSSQLLocalDB;Database=ICP_Test;Trusted_Connection=True;TrustServerCertificate=True;", context.ConnectionString);
    }

    [Fact]
    public void FromConfiguration_RejectsUnsupportedProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "Oracle",
                ["ConnectionStrings:SqlServer"] = "Server=(localdb)\\MSSQLLocalDB;Database=ICP_Test;Trusted_Connection=True;TrustServerCertificate=True;"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => DevSchemaSyncExecutionContext.FromConfiguration(configuration));

        Assert.Contains("SqlServer", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildConfiguration_LoadsHostApiBaseAndDevelopmentConfigFromRepositoryRoot()
    {
        var repositoryRoot = RepositoryRootLocator.Find();
        var hostApiRoot = Path.Combine(repositoryRoot, "apps", "host-api");

        var baseConfiguration = new ConfigurationBuilder()
            .SetBasePath(hostApiRoot)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        var developmentConfiguration = new ConfigurationBuilder()
            .SetBasePath(hostApiRoot)
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false)
            .Build();

        var configuration = DevSchemaSyncConfigurationBootstrap.BuildConfiguration(repositoryRoot, "Development");

        var baseConnectionString = baseConfiguration.GetConnectionString("SqlServer");
        var developmentConnectionString = developmentConfiguration.GetConnectionString("SqlServer");

        Assert.False(string.IsNullOrWhiteSpace(baseConnectionString));
        Assert.False(string.IsNullOrWhiteSpace(developmentConnectionString));
        Assert.NotEqual(baseConnectionString, developmentConnectionString);
        Assert.Equal(developmentConnectionString, configuration.GetConnectionString("SqlServer"));
        Assert.Equal("SqlServer", configuration["Persistence:Provider"]);
    }
}
