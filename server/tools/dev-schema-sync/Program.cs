using System.Text.Json;
using System.Runtime.CompilerServices;
using ICP.Platform.Tools.DevSchemaSync;
using ICP.Platform.Tools.DevSchemaSync.Configuration;
using ICP.Platform.Tools.DevSchemaSync.Database;
using ICP.Platform.Tools.DevSchemaSync.Execution;
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

            return RunAsync(args, context, Console.In, Console.Out);
        }

        internal static async Task<int> RunAsync(string[] args, DevSchemaSyncExecutionContext context, TextWriter writer)
        {
            return await RunAsync(args, context, TextReader.Null, writer);
        }

        internal static async Task<int> RunAsync(string[] args, DevSchemaSyncExecutionContext context, TextReader reader, TextWriter writer)
        {
            var discoveredModels = new ModelDiscoveryService(new SingleRootModelDocumentLoader())
                .DiscoverAll(context.RepositoryRoot);
            var targetSchema = new TargetSchemaBuilder(context.RepositoryRoot).Build(discoveredModels);
            var databaseSchema = await new SqlServerSchemaReader().ReadAsync(context.ConnectionString);
            var plan = new SchemaDiffEngine(new RiskClassifier()).BuildPlan(targetSchema, databaseSchema.Tables);
            var useJson = args.Any(static arg => string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase));
            var showSqlPreview = args.Any(static arg => string.Equals(arg, "--sql", StringComparison.OrdinalIgnoreCase));
            var applyChanges = args.Any(static arg => string.Equals(arg, "--apply", StringComparison.OrdinalIgnoreCase));

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

            var effectivePlan = showSqlPreview || applyChanges
                ? new SchemaSqlGenerator().PopulateSqlPreview(plan, targetSchema, databaseSchema.Tables)
                : plan;

            PlanConsoleWriter.Write(writer, effectivePlan);

            if (showSqlPreview)
            {
                WriteSqlPreview(writer, effectivePlan);
            }

            if (!applyChanges)
            {
                return 0;
            }

            if (effectivePlan.Operations.Count == 0)
            {
                writer.WriteLine();
                writer.WriteLine("当前没有需要执行的结构变更。");
                return 0;
            }

            var executor = SchemaApplyExecutor.CreateForSqlServer(
                context.ConnectionString,
                currentPlan => ConfirmationPrompts.ConfirmApply(reader, writer, currentPlan),
                confirmOperations => ConfirmationPrompts.ConfirmHighRisk(reader, writer, confirmOperations));

            var applyResult = await executor.ApplyAsync(effectivePlan);
            writer.WriteLine();

            if (!applyResult.Applied)
            {
                writer.WriteLine("已取消本次同步，未执行任何数据库变更。");
                return 2;
            }

            writer.WriteLine($"已执行 {applyResult.ExecutedStatementCount} 条 SQL，涉及 {applyResult.ExecutedOperationCount} 个计划操作。");
            return 0;
        }

        private static void WriteSqlPreview(TextWriter writer, SchemaPlan plan)
        {
            writer.WriteLine();
            writer.WriteLine("SQL Preview");
            if (plan.Operations.Count == 0)
            {
                writer.WriteLine("(none)");
                return;
            }

            foreach (var operation in plan.Operations)
            {
                writer.WriteLine($"- [{operation.RiskLevel}] {operation.Kind}: {operation.Description}");
                if (operation.SqlPreview.Count == 0)
                {
                    writer.WriteLine("  (none)");
                    continue;
                }

                foreach (var sql in operation.SqlPreview)
                {
                    writer.WriteLine(sql);
                }

                writer.WriteLine();
            }
        }
    }
}
