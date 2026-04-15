using System.Text.Json;
using System.Runtime.CompilerServices;
using ICP.Platform.Tools.DevSchemaSync;
using ICP.Platform.Tools.DevSchemaSync.Configuration;
using ICP.Platform.Tools.DevSchemaSync.Database;
using ICP.Platform.Tools.DevSchemaSync.Models;
using ICP.Platform.Tools.DevSchemaSync.Planning;
using ICP.Platform.Tools.DevSchemaSync.Schema;

[assembly: InternalsVisibleTo("DevSchemaSync.Tests")]

return await DevSchemaSyncProgram.RunAsync(args);

namespace ICP.Platform.Tools.DevSchemaSync
{
    public static class DevSchemaSyncProgram
    {
        /// <summary>
        /// 使用仓库内标准配置引导工具运行；默认输出文本计划，传入 <c>--json</c> 时输出结构化计划。
        /// </summary>
        public static Task<int> RunAsync(string[] args)
        {
            var repositoryRoot = RepositoryRootLocator.Find();
            var configuration = DevSchemaSyncConfigurationBootstrap.BuildConfiguration(repositoryRoot, "Development");
            var context = DevSchemaSyncExecutionContext.FromConfiguration(configuration, repositoryRoot);

            return RunAsync(args, context, Console.Out);
        }

        internal static async Task<int> RunAsync(string[] args, DevSchemaSyncExecutionContext context, TextWriter writer)
        {
            var discoveredModels = new ModelDiscoveryService(new SingleRootModelDocumentLoader())
                .DiscoverAll(context.RepositoryRoot);
            var targetSchema = new TargetSchemaBuilder(context.RepositoryRoot).Build(discoveredModels);
            var databaseSchema = await new SqlServerSchemaReader().ReadAsync(context.ConnectionString);
            var plan = new SchemaDiffEngine(new RiskClassifier()).BuildPlan(targetSchema, databaseSchema.Tables);
            var useJson = args.Any(static arg => string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase));

            if (useJson)
            {
                var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    WriteIndented = true
                };
                writer.WriteLine(JsonSerializer.Serialize(plan, options));
                return 0;
            }

            writer.WriteLine($"Provider: {context.Provider}");
            writer.WriteLine($"HostApiRoot: {context.HostApiRoot}");
            writer.WriteLine($"Models: {discoveredModels.Count}");
            writer.WriteLine($"Database Tables: {databaseSchema.Tables.Count}");
            writer.WriteLine();
            PlanConsoleWriter.Write(writer, plan);
            return 0;
        }
    }
}
