using System.Diagnostics;

return await PluginOpsApp.RunAsync(args);

/// <summary>旧 CLI 名称只转发到正式发行 Host 的离线入口，不再持有安装、启用或运行时投影逻辑。</summary>
static class PluginOpsApp
{
    // 调用者明确指定发行物，确保卸载只作用于预期部署；信任签发和停服租约仍由发行 Host 持有。
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine("PluginOps <install|select|desired|enable|disable|uninstall|prune|rollback> --host-path <HostApi.dll> --install-root <root> [--module-id <id> --version <version> --enabled <true|false> | --file <desired.json> | --package <root>]");
            return args.Length == 0 ? 1 : 0;
        }
        try
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 1; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length || !args[index].StartsWith("--") || !options.TryAdd(args[index][2..], args[index + 1]))
                    throw new ArgumentException("PLUGIN_OFFLINE_ARGUMENT_INVALID");
            }
            if (!options.Remove("host-path", out var hostPath) || !File.Exists(hostPath)) throw new ArgumentException("A valid --host-path is required.");
            if (!options.Remove("install-root", out var installRoot)) throw new ArgumentException("--install-root is required.");
            var resolvedHost = Path.GetFullPath(hostPath);
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(resolvedHost), UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(resolvedHost);
            start.ArgumentList.Add("plugin-deploy");
            start.ArgumentList.Add(args[0]);
            start.ArgumentList.Add("--Platform:PluginInstallRoot");
            start.ArgumentList.Add(Path.GetFullPath(installRoot));
            foreach (var option in options)
            {
                start.ArgumentList.Add("--" + option.Key);
                start.ArgumentList.Add(option.Key is "package" or "file" ? Path.GetFullPath(option.Value) : option.Value);
            }
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start deployment Host.");
            // 无窗口子进程必须显式转发输出；否则 Windows 会丢弃下游的部署结果与诊断。
            var stdout = process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
            var stderr = process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(15));
            return process.ExitCode;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
