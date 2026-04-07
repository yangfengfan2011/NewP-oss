using ICP.Platform.Kernel.PluginPackages;

return PluginOpsApp.Run(args);

static class PluginOpsApp
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || IsHelpCommand(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            var command = args[0].ToLowerInvariant();
            var options = ParseOptions(args[1..]);

            return command switch
            {
                "install" => RunInstall(options),
                "enable" => RunEnable(options),
                "disable" => RunDisable(options),
                "rollback" => RunRollback(options),
                _ => Fail($"Unknown command '{args[0]}'.")
            };
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message, printUsage: true);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static int RunInstall(IReadOnlyDictionary<string, string> options)
    {
        var packagePath = RequireOption(options, "package");
        var installRoot = ResolveInstallRoot(RequireOption(options, "install-root"));
        var stateStore = new InstalledPluginStateStore(installRoot);
        var installer = new UnifiedPluginPackageInstaller(stateStore);

        var result = installer.Install(packagePath, installRoot);
        PrintInstallResult(result);
        return string.Equals(result.Status, "Installed", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    private static int RunEnable(IReadOnlyDictionary<string, string> options)
    {
        var moduleId = RequireOption(options, "module-id");
        var version = RequireOption(options, "version");
        var installRoot = ResolveInstallRoot(RequireOption(options, "install-root"));
        return RunLifecycleWithRefresh(installRoot, moduleId, stateStore => new UnifiedPluginPackageOperations(stateStore).Enable(moduleId, version));
    }

    private static int RunDisable(IReadOnlyDictionary<string, string> options)
    {
        var moduleId = RequireOption(options, "module-id");
        var installRoot = ResolveInstallRoot(RequireOption(options, "install-root"));
        return RunLifecycleWithRefresh(installRoot, moduleId, stateStore => new UnifiedPluginPackageOperations(stateStore).Disable(moduleId));
    }

    private static int RunRollback(IReadOnlyDictionary<string, string> options)
    {
        var moduleId = RequireOption(options, "module-id");
        var installRoot = ResolveInstallRoot(RequireOption(options, "install-root"));
        return RunLifecycleWithRefresh(installRoot, moduleId, stateStore => new UnifiedPluginPackageOperations(stateStore).Rollback(moduleId));
    }

    private static int RunLifecycleWithRefresh(
        string installRoot,
        string moduleId,
        Func<InstalledPluginStateStore, UnifiedPluginLifecycleResult> action)
    {
        var stateStore = new InstalledPluginStateStore(installRoot);
        if (!stateStore.TryLoad(moduleId, out var previousRecord, out var loadError))
        {
            return Fail(loadError ?? $"Failed to load plugin state for '{moduleId}'.");
        }

        var result = action(stateStore);
        if (!string.Equals(result.Status, "Enabled", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(result.Status, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            PrintLifecycleResult(result);
            return 1;
        }

        try
        {
            MaterializeRuntimeView(installRoot, stateStore);
            PrintLifecycleResult(result);
            return 0;
        }
        catch (Exception ex)
        {
            if (previousRecord is not null && !stateStore.TrySave(previousRecord, out var restoreStateError))
            {
                return Fail($"{ex.Message} Failed to restore previous plugin state: {restoreStateError}");
            }

            try
            {
                MaterializeRuntimeView(installRoot, stateStore);
            }
            catch (Exception restoreProjectionEx)
            {
                return Fail($"{ex.Message} Failed to restore previous runtime projection: {restoreProjectionEx.Message}");
            }

            return Fail($"{ex.Message} Plugin state was restored to the previous version.");
        }
    }

    private static void MaterializeRuntimeView(string installRoot, InstalledPluginStateStore stateStore)
    {
        var frontendPluginBaseUrl = Environment.GetEnvironmentVariable("Platform__FrontendPluginBaseUrl") ?? "/plugin-assets";
        var materializer = new RuntimeViewMaterializer(stateStore, new FrontendRuntimeManifestBuilder(frontendPluginBaseUrl));
        materializer.Materialize(installRoot);
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index += 2)
        {
            var optionName = args[index];
            if (!optionName.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected token '{optionName}'. Options must use the form --name value.");
            }

            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for option '{optionName}'.");
            }

            var optionValue = args[index + 1];
            if (optionValue.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Missing value for option '{optionName}'.");
            }

            options[optionName[2..]] = optionValue;
        }

        return options;
    }

    private static string RequireOption(IReadOnlyDictionary<string, string> options, string name)
    {
        if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing required option '--{name}'.");
        }

        return value;
    }

    private static string ResolveInstallRoot(string installRoot)
    {
        return Path.GetFullPath(installRoot);
    }

    private static bool IsHelpCommand(string value)
    {
        return value is "-h" or "--help" or "help";
    }

    private static void PrintInstallResult(UnifiedPluginInstallResult result)
    {
        if (string.Equals(result.Status, "Installed", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{result.ModuleId}:{result.Version}:{result.Status}");
            return;
        }

        Console.Error.WriteLine(result.ErrorMessage);
    }

    private static void PrintLifecycleResult(UnifiedPluginLifecycleResult result)
    {
        if (string.Equals(result.Status, "Enabled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result.Status, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{result.ModuleId}:{result.Version ?? string.Empty}:{result.Status}");
            return;
        }

        Console.Error.WriteLine(result.ErrorMessage);
    }

    private static int Fail(string message, bool printUsage = false)
    {
        Console.Error.WriteLine(message);
        if (printUsage)
        {
            Console.Error.WriteLine();
            PrintUsage();
        }

        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project .\\tools\\PluginOps\\PluginOps.csproj -- install --package <path> --install-root <path>");
        Console.WriteLine("  dotnet run --project .\\tools\\PluginOps\\PluginOps.csproj -- enable --module-id <id> --version <version> --install-root <path>");
        Console.WriteLine("  dotnet run --project .\\tools\\PluginOps\\PluginOps.csproj -- disable --module-id <id> --install-root <path>");
        Console.WriteLine("  dotnet run --project .\\tools\\PluginOps\\PluginOps.csproj -- rollback --module-id <id> --install-root <path>");
    }
}