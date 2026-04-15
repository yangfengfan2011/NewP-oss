using System.Text.Json;
using System.Runtime.CompilerServices;
using ICP.Platform.Tools.DevSchemaSync;
using ICP.Platform.Tools.DevSchemaSync.Configuration;
using ICP.Platform.Tools.DevSchemaSync.Database;
using ICP.Platform.Tools.DevSchemaSync.Models;
using ICP.Platform.Tools.DevSchemaSync.Schema;

[assembly: InternalsVisibleTo("DevSchemaSync.Tests")]

return await DevSchemaSyncProgram.RunAsync(args);

namespace ICP.Platform.Tools.DevSchemaSync
{
    public static class DevSchemaSyncProgram
    {
        public static Task<int> RunAsync(string[] args)
        {
            var repositoryRoot = RepositoryRootLocator.Find();
            var configuration = DevSchemaSyncConfigurationBootstrap.BuildConfiguration(repositoryRoot, "Development");
            var context = DevSchemaSyncExecutionContext.FromConfiguration(configuration, repositoryRoot);

            return RunAsync(args, context, Console.Out);
        }

        internal static async Task<int> RunAsync(string[] args, DevSchemaSyncExecutionContext context, TextWriter writer)
        {
            writer.WriteLine($"Provider: {context.Provider}");
            writer.WriteLine($"HostApiRoot: {context.HostApiRoot}");

            if (args.Any(static arg => string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase)))
            {
                writer.WriteLine(JsonSerializer.Serialize(new { status = "ok" }));
                return 0;
            }

            // 默认路径直接串起模型发现、目标 schema 投影与数据库摘要读取，先提供最小可验证计数。
            var discoveredModels = new ModelDiscoveryService(new SingleRootModelDocumentLoader())
                .DiscoverAll(context.RepositoryRoot);
            var targetSchema = new TargetSchemaBuilder(context.RepositoryRoot).Build(discoveredModels);
            var databaseSchema = await new SqlServerSchemaReader().ReadAsync(context.ConnectionString);

            writer.WriteLine($"Models: {discoveredModels.Count}");
            writer.WriteLine($"Database Tables: {databaseSchema.Tables.Count}");
            writer.WriteLine("DevSchemaSync ready.");
            return 0;
        }
    }
}
