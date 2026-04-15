using Microsoft.Extensions.Configuration;

namespace ICP.Platform.Tools.DevSchemaSync.Configuration;

public static class DevSchemaSyncConfigurationBootstrap
{
    /// <summary>
    /// 按 Host API 的配置优先级构建运行配置：基础文件、环境配置、环境变量。
    /// </summary>
    public static IConfigurationRoot BuildConfiguration(string repositoryRoot, string environmentName = "Development")
    {
        var hostApiRoot = Path.Combine(repositoryRoot, "apps", "host-api");

        return new ConfigurationBuilder()
            .SetBasePath(hostApiRoot)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }
}
