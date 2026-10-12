using System.Text.Json;
using ICP.Platform.Kernel.Localization;
using ICP.Platform.Kernel.PluginPackages;
using ICP.Platform.Presentation;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public sealed class RuntimeTextCatalogTests
{
    [Fact]
    public void FrontendAndBackendTemplatesUseTheSameCases()
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "localization-template-cases.json")));
        foreach (var test in cases.RootElement.EnumerateArray())
        {
            var args = test.GetProperty("args").EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
            Assert.Equal(test.GetProperty("expected").GetString(), LocalizationTextTemplate.Format(test.GetProperty("template").GetString()!, args));
        }
        Assert.Throws<ArgumentException>(() => LocalizationTextTemplate.Format("{value}", new Dictionary<string, object?> { ["value"] = new { Secret = "never stringify" } }));
    }

    [Fact]
    public void ActualPersonnelAuthoringProjectsToTheBackendAndFrontendCatalog()
    {
        var repository = FindRepository();
        var root = Path.Combine(Path.GetTempPath(), "newp-localization-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var module in new[] { "Platform", "Organization" })
            {
                var source = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(repository, module), "metadata");
                foreach (var file in Directory.EnumerateFiles(source, "*.json", SearchOption.AllDirectories))
                {
                    var destination = Path.Combine(ICP.Platform.Modeling.Metadata.ModuleAuthoringPaths.ResolveModuleRoot(root, module), "metadata", Path.GetRelativePath(source, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination);
                }
            }
            var json = new LocalizationRuntimeManifestBuilder().Build(root);
            var catalog = RuntimeTextCatalog.Parse(json);
            using var frontend = JsonDocument.Parse(json);
            var args = new Dictionary<string, object?> { ["date"] = "2026-09-28" };
            Assert.Equal("姓名", catalog.GetText("PersonListView.Field_ENAME", "zh-CN"));
            Assert.Equal("Name", catalog.GetText("PersonListView.Field_ENAME", "en-US"));
            Assert.Equal("Birth date cannot be later than 2026-09-28.", catalog.GetText("Organization.Person.BirthDateInFuture", "en-US", args));
            Assert.Equal(frontend.RootElement.GetProperty("catalogs").GetProperty("en").GetProperty("Platform.SystemActor").GetString(), catalog.GetText("Platform.SystemActor", "en-US"));
            Assert.Equal("系统", catalog.GetText("Platform.SystemActor"));
            var lengthArgs = new Dictionary<string, object?> { ["maxLength"] = 256 };
            Assert.Equal("文本不能超过 256 个字符。", catalog.GetText("StandardCrud.ShortTextTooLong", "zh-CN", lengthArgs));
            Assert.Equal("Text cannot exceed 256 characters.", catalog.GetText("StandardCrud.ShortTextTooLong", "en-US", lengthArgs));
            Assert.Equal("Trim leading and trailing whitespace; submit null for an empty value.", catalog.GetText("StandardCrud.ShortTextNotNormalized", "en-US"));
            Assert.Throws<InvalidOperationException>(() => catalog.GetText("Platform.SystemActor", "fr-FR"));
            Assert.Throws<InvalidOperationException>(() => catalog.GetText("Missing.Entry", "en-US"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "frontend", "modules"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository modules not found.");
    }
}
