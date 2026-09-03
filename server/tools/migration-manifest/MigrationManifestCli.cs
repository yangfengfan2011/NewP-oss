namespace ICP.Platform.Tools.MigrationManifest;

/// <summary>
/// migration-manifest 命令行参数与退出码 owner。
/// </summary>
public static class MigrationManifestCli
{
    /// <summary>
    /// 执行 finalize 或 check，并把可定位诊断写入 stderr。
    /// </summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        try
        {
            if (args.Length == 0)
            {
                throw Usage("missing command.");
            }

            var command = args[0];
            var options = ParseOptions(args[1..]);
            var repositoryRoot = options.Remove("--repository-root", out var configuredRoot)
                ? Path.GetFullPath(configuredRoot!)
                : FindRepositoryRoot();
            var application = new MigrationManifestApplication(repositoryRoot);
            switch (command)
            {
                case "finalize":
                    {
                        var module = TakeRequired(options, "--module");
                        var provider = TakeRequired(options, "--provider");
                        var migration = TakeRequired(options, "--migration");
                        options.Remove("--compatibility", out var compatibility);
                        RejectUnknown(options);
                        var result = application.Finalize(module, provider, migration, compatibility);
                        output.WriteLine(
                            result.Changed
                                ? $"finalized {result.ModuleId}/{result.Provider}/{result.MigrationId}: {result.ManifestPath}"
                                : $"already finalized {result.ModuleId}/{result.Provider}/{result.MigrationId}: {result.ManifestPath}");
                        return 0;
                    }
                case "check":
                    {
                        var all = TakeFlag(options, "--all");
                        var noBuild = TakeFlag(options, "--no-build");
                        RejectUnknown(options);
                        if (!all || !noBuild)
                        {
                            throw Usage("check requires --all --no-build.");
                        }

                        var result = application.CheckAll(noBuild: true);
                        foreach (var providerResult in result.ProviderResults)
                        {
                            output.WriteLine(providerResult);
                        }

                        output.WriteLine($"migration-manifest check passed: {result.CheckedBundles} bundle(s).");
                        return 0;
                    }
                default:
                    throw Usage($"unknown command '{command}'.");
            }
        }
        catch (MigrationManifestToolException exception)
        {
            error.WriteLine(exception.Message);
            return 1;
        }
        catch (Exception exception)
        {
            error.WriteLine("MIGRATION_MANIFEST_UNEXPECTED: " + exception.Message);
            return 1;
        }
    }

    // 选项使用显式 key/value；--all 与 --no-build 是唯一无值 flag。
    private static Dictionary<string, string?> ParseOptions(IEnumerable<string> arguments)
    {
        var values = arguments.ToArray();
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var index = 0; index < values.Length; index++)
        {
            var key = values[index];
            if (!key.StartsWith("--", StringComparison.Ordinal))
            {
                throw Usage($"unexpected argument '{key}'.");
            }

            if (!options.TryAdd(key, null))
            {
                throw Usage($"option '{key}' is repeated.");
            }

            if (key is "--all" or "--no-build")
            {
                continue;
            }

            if (index + 1 >= values.Length || values[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw Usage($"option '{key}' requires a value.");
            }

            options[key] = values[++index];
        }

        return options;
    }

    // 取出一个必需的有值选项，避免调用者遗漏清理已消费参数。
    private static string TakeRequired(Dictionary<string, string?> options, string key)
    {
        if (!options.Remove(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw Usage($"option '{key}' is required.");
        }

        return value;
    }

    // 取出 flag 并拒绝意外值。
    private static bool TakeFlag(Dictionary<string, string?> options, string key)
    {
        if (!options.Remove(key, out var value))
        {
            return false;
        }

        if (value is not null)
        {
            throw Usage($"flag '{key}' does not accept a value.");
        }

        return true;
    }

    // 所有命令都 fail closed，未知选项不能被静默忽略。
    private static void RejectUnknown(IReadOnlyDictionary<string, string?> options)
    {
        if (options.Count > 0)
        {
            throw Usage($"unknown option '{options.Keys.Order(StringComparer.Ordinal).First()}'.");
        }
    }

    // 从源码目录向上定位唯一仓库边界；隔离重放可用 --repository-root 显式覆盖。
    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "server"))
                && Directory.Exists(Path.Combine(current.FullName, "modules")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new MigrationManifestToolException(
            "MIGRATION_MANIFEST_REPOSITORY_NOT_FOUND",
            "cannot locate repository root; run inside the repository or pass --repository-root <path>.");
    }

    // 用法错误仍使用稳定非零工具失败，便于脚本和人工调用得到同一诊断形状。
    private static MigrationManifestToolException Usage(string detail) =>
        new(
            "MIGRATION_MANIFEST_USAGE",
            detail + Environment.NewLine +
            "Usage:" + Environment.NewLine +
            "  migration-manifest finalize --module <id> --provider <provider> --migration <id> [--compatibility <label>] [--repository-root <path>]" + Environment.NewLine +
            "  migration-manifest check --all --no-build [--repository-root <path>]");
}
