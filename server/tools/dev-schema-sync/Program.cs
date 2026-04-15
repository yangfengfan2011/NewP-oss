using System.Text.Json;
using System.Runtime.CompilerServices;
using ICP.Platform.Tools.DevSchemaSync;
using ICP.Platform.Tools.DevSchemaSync.Configuration;

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

        internal static Task<int> RunAsync(string[] args, DevSchemaSyncExecutionContext context, TextWriter writer)
        {
            writer.WriteLine($"Provider: {context.Provider}");
            writer.WriteLine($"HostApiRoot: {context.HostApiRoot}");

            if (args.Any(static arg => string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase)))
            {
                writer.WriteLine(JsonSerializer.Serialize(new { status = "ok" }));
                return Task.FromResult(0);
            }

            writer.WriteLine("DevSchemaSync ready.");
            return Task.FromResult(0);
        }
    }
}
