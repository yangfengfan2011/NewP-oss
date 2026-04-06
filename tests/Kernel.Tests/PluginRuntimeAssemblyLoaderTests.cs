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

    private static string CreatePluginRoot(TestPluginDefinition definition)
    {
        var pluginsRoot = Path.Combine(
            Path.GetTempPath(),
            $"plugin-runtime-loader-tests-{Guid.NewGuid():N}");
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
            HostVersion = definition.HostVersion
        };

        var manifestJson = JsonSerializer.Serialize(manifest);
        File.WriteAllText(Path.Combine(pluginDirectory, "plugin.json"), manifestJson);

        return pluginsRoot;
    }

    private sealed record TestPluginDefinition(
        string ModuleId,
        HostVersionRequirement HostVersion);

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
}
