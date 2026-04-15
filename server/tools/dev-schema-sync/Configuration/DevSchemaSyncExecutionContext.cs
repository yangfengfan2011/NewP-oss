using Microsoft.Extensions.Configuration;

namespace ICP.Platform.Tools.DevSchemaSync.Configuration;

public sealed record DevSchemaSyncExecutionContext(
    string Provider,
    string ConnectionString,
    string RepositoryRoot,
    string HostApiRoot)
{
    /// <summary>
    /// 从配置读取运行上下文，只允许 SqlServer provider。
    /// </summary>
    public static DevSchemaSyncExecutionContext FromConfiguration(IConfiguration configuration, string repositoryRoot = "")
    {
        var provider = configuration["Persistence:Provider"]
            ?? throw new InvalidOperationException("缺少 Persistence:Provider 配置。");

        if (!string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"当前 provider '{provider}' 暂不支持，仅支持 SqlServer。");
        }

        var connectionString = configuration.GetConnectionString("SqlServer");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("缺少 ConnectionStrings:SqlServer 配置。");
        }

        var hostApiRoot = string.IsNullOrWhiteSpace(repositoryRoot)
            ? string.Empty
            : Path.Combine(repositoryRoot, "apps", "host-api");

        return new DevSchemaSyncExecutionContext("SqlServer", connectionString, repositoryRoot, hostApiRoot);
    }
}
