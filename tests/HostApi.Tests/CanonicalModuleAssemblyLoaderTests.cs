using ICP.Platform.HostApi.PluginActivation;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Audit;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class CanonicalModuleAssemblyLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "newp-canonical-module-loader-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_WhenAssemblyIdentityDiffersFromPlan_RejectsBeforeBuild()
    {
        var packageRoot = Path.Combine(_root, "package");
        var backendRoot = Path.Combine(packageRoot, "backend");
        Directory.CreateDirectory(backendRoot);
        File.Copy(
            typeof(AuditModule).Assembly.Location,
            Path.Combine(backendRoot, "AuditModule.dll"));
        var module = new CanonicalModulePlanModule(
            "Unexpected.Module",
            "1.0.0",
            "Unexpected",
            null,
            packageRoot,
            "AuditModule.dll",
            null,
            null,
            null,
            "legacy-module",
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            new ModulePlanProvenance("installed-package", Path.Combine(packageRoot, "plugin.json"), "/"));
        var plan = new CanonicalModulePlan(
            "candidate",
            "1.0",
            new string('a', 64),
            new string('b', 64),
            [],
            [module]);

        var error = Assert.Throws<InvalidDataException>(() =>
            new CanonicalModuleAssemblyLoader(_root).Load(plan));

        Assert.Contains("does not match", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_PublicBuiltInLabel_CannotUseDistributionWideAssemblyBoundary()
    {
        var packageRoot = Path.Combine(_root, "modules", "Audit");
        Directory.CreateDirectory(packageRoot);
        File.Copy(typeof(AuditModule).Assembly.Location, Path.Combine(_root, "AuditModule.dll"));
        var module = new CanonicalModulePlanModule(
            "Audit",
            "1.0.0",
            "审计日志",
            null,
            packageRoot,
            "../../AuditModule.dll",
            null,
            null,
            null,
            "legacy-module",
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            new ModulePlanProvenance("built-in", Path.Combine(packageRoot, "plugin.json"), "/"));
        var plan = new CanonicalModulePlan(
            "candidate",
            "1.0",
            new string('a', 64),
            new string('b', 64),
            [],
            [module]);

        Assert.Throws<FileNotFoundException>(() =>
            new CanonicalModuleAssemblyLoader(_root).Load(plan));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
