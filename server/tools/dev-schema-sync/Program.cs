using System.Text.Json;
using System.Runtime.CompilerServices;
using ICP.Platform.Persistence.DevSync.Coordination;
using ICP.Platform.Persistence.DevSync.Planning;
using ICP.Platform.Tools.DevSchemaSync;
using ICP.Platform.Tools.DevSchemaSync.Configuration;
using ICP.Platform.Tools.DevSchemaSync.Execution;
using ICP.Platform.Tools.DevSchemaSync.Planning;

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
            var useJson = args.Any(static arg => string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase));
            var showSqlPreview = args.Any(static arg => string.Equals(arg, "--sql", StringComparison.OrdinalIgnoreCase));
            var applyChanges = args.Any(static arg => string.Equals(arg, "--apply", StringComparison.OrdinalIgnoreCase));
            var writeSqlPreview = showSqlPreview || applyChanges;
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            };

            if (useJson && applyChanges)
            {
                writer.WriteLine(JsonSerializer.Serialize(DevSchemaSyncJsonEnvelope.JsonApplyNotSupported(), jsonOptions));
                return 3;
            }

            var coordinator = DevSchemaSyncCliBootstrap.CreateCoordinator(
                new DevelopmentSchemaSyncOptions(
                    context.RepositoryRoot,
                    context.ConnectionString,
                    context.Provider,
                    applyChanges,
                    writeSqlPreview,
                    ConfirmApply: plan => ConfirmationPrompts.ConfirmApply(reader, writer, plan),
                    AllowConfirmOperations: applyChanges,
                    ConfirmRisk: operations => ConfirmationPrompts.ConfirmRisk(reader, writer, operations)));
            var result = await coordinator.PrepareAsync(CancellationToken.None);

            if (useJson)
            {
                writer.WriteLine(JsonSerializer.Serialize(DevSchemaSyncJsonEnvelope.FromResult(result), jsonOptions));
                return result.ExitCode;
            }

            writer.WriteLine($"Provider: {context.Provider}");
            writer.WriteLine($"HostApiRoot: {context.HostApiRoot}");
            writer.WriteLine($"Models: {result.Inventory?.LoadedModels.Count ?? 0}");
            writer.WriteLine($"Database Tables: {result.DatabaseTableCount}");
            writer.WriteLine();

            if (!result.StartupAllowed)
            {
                writer.WriteLine(result.BuildMessage());
                foreach (var diagnostic in result.Diagnostics)
                {
                    writer.WriteLine($"诊断: {diagnostic}");
                }

                if (result.Plan is not null)
                {
                    PlanConsoleWriter.Write(writer, result.Plan);
                    if (writeSqlPreview)
                    {
                        WriteSqlPreview(writer, result.Plan);
                    }
                }

                return result.ExitCode;
            }

            var effectivePlan = result.Plan
                ?? throw new InvalidOperationException("开发态结构同步未返回结构计划。");

            PlanConsoleWriter.Write(writer, effectivePlan);

            if (writeSqlPreview)
            {
                WriteSqlPreview(writer, effectivePlan);
            }

            if (!applyChanges)
            {
                return result.ExitCode;
            }

            if (effectivePlan.Operations.Count == 0)
            {
                writer.WriteLine();
                writer.WriteLine("当前没有需要执行的结构变更。");
                return result.ExitCode;
            }

            result = await coordinator.ExecutePreparedAsync(result, CancellationToken.None);
            if (!result.StartupAllowed)
            {
                writer.WriteLine();
                writer.WriteLine(result.BuildMessage());
                foreach (var diagnostic in result.Diagnostics)
                {
                    writer.WriteLine($"诊断: {diagnostic}");
                }

                return result.ExitCode;
            }

            writer.WriteLine();
            writer.WriteLine($"已执行 {result.ApplyResult?.ExecutedStatementCount ?? 0} 条 SQL，涉及 {result.ApplyResult?.ExecutedOperationCount ?? 0} 个计划操作。");
            return result.ExitCode;
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

        private sealed record DevSchemaSyncJsonEnvelope(
            bool StartupAllowed,
            int ExitCode,
            string? FailureReason,
            string Message,
            SchemaPlan Plan,
            IReadOnlyList<string> MissingModels,
            IReadOnlyList<string> SignatureMismatches,
            IReadOnlyList<string> Diagnostics)
        {
            public static DevSchemaSyncJsonEnvelope FromResult(DevelopmentSchemaSyncResult result)
            {
                return new DevSchemaSyncJsonEnvelope(
                    result.StartupAllowed,
                    result.ExitCode,
                    result.FailureReason,
                    result.BuildMessage(),
                    result.Plan ?? SchemaPlan.Empty,
                    result.Inventory?.MissingModels ?? [],
                    result.SignatureMismatches,
                    result.Diagnostics);
            }

            public static DevSchemaSyncJsonEnvelope JsonApplyNotSupported()
            {
                return new DevSchemaSyncJsonEnvelope(
                    false,
                    3,
                    "JsonApplyNotSupported",
                    "JSON 输出模式不支持交互执行 --apply；请改用文本模式确认执行。",
                    SchemaPlan.Empty,
                    [],
                    [],
                    []);
            }
        }
    }

    public static class DevSchemaSyncCliBootstrap
    {
        private static readonly AsyncLocal<Func<DevelopmentSchemaSyncOptions, DevelopmentSchemaSyncCoordinator>?> TestCoordinatorFactory = new();

        /// <summary>
        /// CLI 只负责把配置/参数转换为共享内核选项；同步顺序、阻断和执行都由 coordinator 统一处理。
        /// </summary>
        public static DevelopmentSchemaSyncCoordinator CreateCoordinator(string repositoryRoot, string[] args)
        {
            var configuration = DevSchemaSyncConfigurationBootstrap.BuildConfiguration(repositoryRoot, "Development");
            var context = DevSchemaSyncExecutionContext.FromConfiguration(configuration, repositoryRoot);
            var applyChanges = args.Any(static arg => string.Equals(arg, "--apply", StringComparison.OrdinalIgnoreCase));
            var showSqlPreview = args.Any(static arg => string.Equals(arg, "--sql", StringComparison.OrdinalIgnoreCase));

            return CreateCoordinator(new DevelopmentSchemaSyncOptions(
                context.RepositoryRoot,
                context.ConnectionString,
                context.Provider,
                applyChanges,
                showSqlPreview || applyChanges,
                AllowConfirmOperations: applyChanges));
        }

        internal static DevelopmentSchemaSyncCoordinator CreateCoordinator(DevelopmentSchemaSyncOptions options)
        {
            return TestCoordinatorFactory.Value?.Invoke(options)
                ?? DevelopmentSchemaSyncCoordinator.Create(options);
        }

        internal static IDisposable OverrideCoordinatorFactoryForTests(
            Func<DevelopmentSchemaSyncOptions, DevelopmentSchemaSyncCoordinator> coordinatorFactory)
        {
            ArgumentNullException.ThrowIfNull(coordinatorFactory);

            var previous = TestCoordinatorFactory.Value;
            TestCoordinatorFactory.Value = coordinatorFactory;
            return new RestoreCoordinatorFactoryScope(previous);
        }

        private sealed class RestoreCoordinatorFactoryScope : IDisposable
        {
            private readonly Func<DevelopmentSchemaSyncOptions, DevelopmentSchemaSyncCoordinator>? _previous;
            private bool _disposed;

            public RestoreCoordinatorFactoryScope(Func<DevelopmentSchemaSyncOptions, DevelopmentSchemaSyncCoordinator>? previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                TestCoordinatorFactory.Value = _previous;
                _disposed = true;
            }
        }
    }
}
