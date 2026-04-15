using System.Text.Json;
using ICP.Platform.Tools.DevSchemaSync;

return await DevSchemaSyncProgram.RunAsync(args);

namespace ICP.Platform.Tools.DevSchemaSync
{
    public static class DevSchemaSyncProgram
    {
        public static Task<int> RunAsync(string[] args)
        {
            if (args.Any(static arg => string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine(JsonSerializer.Serialize(new { status = "ok" }));
                return Task.FromResult(0);
            }

            Console.WriteLine("DevSchemaSync ready.");
            return Task.FromResult(0);
        }
    }
}
