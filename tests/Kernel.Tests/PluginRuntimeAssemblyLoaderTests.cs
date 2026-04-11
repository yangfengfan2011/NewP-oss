using System.Text.Json;
using ICP.Platform.Kernel.PluginDiscovery;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class PluginRuntimeAssemblyLoaderTests
{
    [Fact]
    public void PluginRuntimeAssemblyLoader_LoadsAssembliesForCompatiblePlugins()
    {
        var pluginsRoot = CreatePluginRoot(
            new TestPluginDefinition(
                "RuntimeLoaderPlugin",
                new HostVersionRequirement
                {
                    Min = "1.0.0",
                    Max = "2.0.0"
                }));

        try
        {
            var loader = new PluginRuntimeAssemblyLoader("1.5.0");

            var result = loader.LoadAssemblies(pluginsRoot);

            Assert.Single(result.LoadedAssemblies);
            Assert.Equal("RuntimeLoaderPlugin", result.SuccessfulPlugins[0].Manifest!.ModuleId);
            Assert.Empty(result.FailedPlugins);
        }
        finally
        {
            Directory.Delete(pluginsRoot, recursive: true);
        }
    }

    [Fact]
    public void PluginRuntimeAssemblyLoader_SkipsAndReportsIncompatiblePlugins()
    {
        var pluginsRoot = CreatePluginRoot(
            new TestPluginDefinition(
                "IncompatibleRuntimeLoaderPlugin",
                new HostVersionRequirement
                {
                    Min = "9.0.0",
                    Max = "10.0.0"
                }));

        try
        {
            var loader = new PluginRuntimeAssemblyLoader("1.5.0");

            var result = loader.LoadAssemblies(pluginsRoot);

            Assert.Empty(result.LoadedAssemblies);
            Assert.Empty(result.SuccessfulPlugins);
            Assert.Single(result.FailedPlugins);
            Assert.Equal(
                PluginScanErrorType.IncompatibleHostVersion,
                result.FailedPlugins[0].ErrorType);
            Assert.Equal(
                "IncompatibleRuntimeLoaderPlugin",
                result.FailedPlugins[0].Manifest!.ModuleId);
        }
        finally
        {
            Directory.Delete(pluginsRoot, recursive: true);
        }
    }

    private static string CreatePluginRoot(params TestPluginDefinition[] definitions)
    {
        var pluginsRoot = Path.Combine(
            Path.GetTempPath(),
            $"plugin-runtime-loader-tests-{Guid.NewGuid():N}");
        foreach (var definition in definitions)
        {
            var pluginDirectory = Path.Combine(pluginsRoot, definition.ModuleId);
            Directory.CreateDirectory(pluginDirectory);

            var sourceAssemblyPath = typeof(PluginRuntimeAssemblyLoaderTests).Assembly.Location;
            var entryAssemblyName = "RuntimeLoaderPlugin.dll";
            File.Copy(sourceAssemblyPath, Path.Combine(pluginDirectory, entryAssemblyName));

            var manifest = new PluginManifest
            {
                ModuleId = definition.ModuleId,
                DisplayName = definition.ModuleId,
                Version = "1.0.0",
                EntryAssembly = entryAssemblyName,
                Dependencies = definition.Dependencies ?? Array.Empty<string>(),
                HostVersion = definition.HostVersion
            };

            var manifestJson = JsonSerializer.Serialize(manifest);
            File.WriteAllText(Path.Combine(pluginDirectory, "plugin.json"), manifestJson);
        }

        return pluginsRoot;
    }

    private sealed record TestPluginDefinition(
        string ModuleId,
        HostVersionRequirement HostVersion,
        IReadOnlyList<string>? Dependencies = null);

    [Fact]
    public void PluginRuntimeAssemblyLoader_DisabledPlugin_SkipsAndReportsAsFailure()
    {
        var pluginsRoot = CreatePluginRoot(
            new TestPluginDefinition(
                "DisabledRuntimePlugin",
                new HostVersionRequirement
                {
                    Min = "1.0.0",
                    Max = "2.0.0"
                }));

        try
        {
            var disabledPluginIds = new[] { "DisabledRuntimePlugin" };
            var loader = new PluginRuntimeAssemblyLoader("1.5.0", disabledPluginIds);

            var result = loader.LoadAssemblies(pluginsRoot);

            Assert.Empty(result.LoadedAssemblies);
            Assert.Empty(result.SuccessfulPlugins);
            Assert.Single(result.FailedPlugins);
            Assert.Equal(
                PluginScanErrorType.PluginDisabled,
                result.FailedPlugins[0].ErrorType);
            Assert.Contains("disabled", result.FailedPlugins[0].ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(pluginsRoot, recursive: true);
        }
    }

    [Fact]
    public void PluginRuntimeAssemblyLoader_DisabledPluginCaseInsensitive_SkipsPlugin()
    {
        var pluginsRoot = CreatePluginRoot(
            new TestPluginDefinition(
                "CaseSensitivePlugin",
                new HostVersionRequirement
                {
                    Min = "1.0.0",
                    Max = "2.0.0"
                }));

        try
        {
            var disabledPluginIds = new[] { "casesensitiveplugin" }; // lowercase
            var loader = new PluginRuntimeAssemblyLoader("1.5.0", disabledPluginIds);

            var result = loader.LoadAssemblies(pluginsRoot);

            Assert.Empty(result.LoadedAssemblies);
            Assert.Single(result.FailedPlugins);
            Assert.Equal(PluginScanErrorType.PluginDisabled, result.FailedPlugins[0].ErrorType);
        }
        finally
        {
            Directory.Delete(pluginsRoot, recursive: true);
        }
    }

    [Fact]
    public void PluginRuntimeAssemblyLoader_SkipsPlugin_WhenDependencyIsMissing()
    {
        var pluginsRoot = CreatePluginRoot(
            new TestPluginDefinition(
                "DependentPlugin",
                new HostVersionRequirement
                {
                    Min = "1.0.0",
                    Max = "2.0.0"
                },
                ["MissingPlugin"]));

        try
        {
            var loader = new PluginRuntimeAssemblyLoader("1.5.0");

            var result = loader.LoadAssemblies(pluginsRoot);

            Assert.Empty(result.LoadedAssemblies);
            Assert.Empty(result.SuccessfulPlugins);
            var failedPlugin = Assert.Single(result.FailedPlugins);
            Assert.Equal(PluginScanErrorType.DependencyNotSatisfied, failedPlugin.ErrorType);
            Assert.Equal("DependentPlugin", failedPlugin.Manifest!.ModuleId);
            Assert.Contains("MissingPlugin", failedPlugin.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(pluginsRoot, recursive: true);
        }
    }

    [Fact]
    public void PluginRuntimeAssemblyLoader_SkipsTransitiveDependencyChain_WhenLeafDependencyIsMissing()
    {
        var pluginsRoot = CreatePluginRoot(
            new TestPluginDefinition(
                "PluginA",
                new HostVersionRequirement
                {
                    Min = "1.0.0",
                    Max = "2.0.0"
                },
                ["PluginB"]),
            new TestPluginDefinition(
                "PluginB",
                new HostVersionRequirement
                {
                    Min = "1.0.0",
                    Max = "2.0.0"
                },
                ["PluginC"]));

        try
        {
            var loader = new PluginRuntimeAssemblyLoader("1.5.0");

            var result = loader.LoadAssemblies(pluginsRoot);

            Assert.Empty(result.LoadedAssemblies);
            Assert.Empty(result.SuccessfulPlugins);
            Assert.Equal(2, result.FailedPlugins.Count);
            Assert.All(result.FailedPlugins, plugin => Assert.Equal(PluginScanErrorType.DependencyNotSatisfied, plugin.ErrorType));
            Assert.Contains(result.FailedPlugins, plugin => plugin.Manifest!.ModuleId == "PluginA");
            Assert.Contains(result.FailedPlugins, plugin => plugin.Manifest!.ModuleId == "PluginB");
        }
        finally
        {
            Directory.Delete(pluginsRoot, recursive: true);
        }
    }

    [Fact]
    public void PluginRuntimeAssemblyLoader_SkipsPlugins_WhenCircularDependencyDetected()
    {
        var pluginsRoot = CreatePluginRoot(
            new TestPluginDefinition(
                "PluginA",
                new HostVersionRequirement
                {
                    Min = "1.0.0",
                    Max = "2.0.0"
                },
                ["PluginB"]),
            new TestPluginDefinition(
                "PluginB",
                new HostVersionRequirement
                {
                    Min = "1.0.0",
                    Max = "2.0.0"
                },
                ["PluginA"]));

        try
        {
            var loader = new PluginRuntimeAssemblyLoader("1.5.0");

            var result = loader.LoadAssemblies(pluginsRoot);

            Assert.Empty(result.LoadedAssemblies);
            Assert.Empty(result.SuccessfulPlugins);
            Assert.Equal(2, result.FailedPlugins.Count);
            Assert.All(result.FailedPlugins, plugin => Assert.Equal(PluginScanErrorType.CircularDependency, plugin.ErrorType));
            Assert.All(result.FailedPlugins, plugin => Assert.Contains("PluginA", plugin.ErrorMessage!, StringComparison.OrdinalIgnoreCase));
            Assert.All(result.FailedPlugins, plugin => Assert.Contains("PluginB", plugin.ErrorMessage!, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(pluginsRoot, recursive: true);
        }
    }
}
