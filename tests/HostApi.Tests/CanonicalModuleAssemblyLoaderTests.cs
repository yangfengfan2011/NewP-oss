using ICP.Platform.HostApi.PluginActivation;
using ICP.Platform.Kernel.ModuleLoading;
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

    [Fact]
    public void Load_PreloadedUnregisteredDefaultWithMatchingLocation_UsesSignedImage()
    {
        var modulesRoot = Path.Combine(_root, "modules");
        var packageRoot = Path.Combine(modulesRoot, "Audit");
        Directory.CreateDirectory(packageRoot);
        var entryPath = Path.Combine(_root, "AuditModule.dll");
        File.Copy(typeof(AuditModule).Assembly.Location, entryPath);
        File.WriteAllText(
            Path.Combine(packageRoot, "plugin.json"),
            """
            {
              "schemaVersion": "1.0",
              "moduleId": "Audit",
              "displayName": "审计日志",
              "version": "1.0.0",
              "entryAssembly": "../../AuditModule.dll",
              "dependencies": []
            }
            """);
        var source = TrustedBuiltInModuleSource.CreateForHost(_root, modulesRoot);
        var candidate = Assert.Single(new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(source));
        var compilation = new CanonicalModulePlanCompiler().Compile("signed-audit", [candidate]);
        var plan = Assert.IsType<CanonicalModulePlan>(compilation.Plan);

        // HostApi.Tests 已在默认 ALC 中预加载；主体仍必须由签发映像建立独立受控装载。
        File.Copy(typeof(CanonicalModuleAssemblyLoaderTests).Assembly.Location, entryPath, overwrite: true);

        var assembly = Assert.Single(new CanonicalModuleAssemblyLoader(
            _root,
            new TrustedDefaultModuleAssemblyRegistry()).Load(plan));

        Assert.NotSame(typeof(AuditModule).Assembly, assembly);
        Assert.Equal(typeof(AuditModule).Assembly.ManifestModule.ModuleVersionId, assembly.ManifestModule.ModuleVersionId);
        Assert.Contains(assembly.GetTypes(), type => type.Name == nameof(AuditModule));
    }

    [Fact]
    public void Load_UniqueBuiltInImage_RegistersExactSignedBytesInDefaultContext()
    {
        var modulesRoot = Path.Combine(_root, "modules");
        var packageRoot = Path.Combine(modulesRoot, "Audit");
        Directory.CreateDirectory(packageRoot);
        var entryPath = Path.Combine(_root, "ProbeModule.dll");
        var image = File.ReadAllBytes(typeof(AuditModule).Assembly.Location);
        ReplaceAllUtf8(image, "AuditModule", "ProbeModule");
        File.WriteAllBytes(entryPath, image);
        File.WriteAllText(
            Path.Combine(packageRoot, "plugin.json"),
            """
            {
              "schemaVersion": "1.0",
              "moduleId": "Audit",
              "displayName": "审计日志",
              "version": "1.0.0",
              "entryAssembly": "../../ProbeModule.dll",
              "dependencies": []
            }
            """);
        var source = TrustedBuiltInModuleSource.CreateForHost(_root, modulesRoot);
        var candidate = Assert.Single(new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(source));
        var plan = Assert.IsType<CanonicalModulePlan>(
            new CanonicalModulePlanCompiler().Compile("signed-probe", [candidate]).Plan);

        var assembly = Assert.Single(new CanonicalModuleAssemblyLoader(
            _root,
            new TrustedDefaultModuleAssemblyRegistry()).Load(plan));

        Assert.Same(System.Runtime.Loader.AssemblyLoadContext.Default,
            System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(assembly));
        Assert.Equal(string.Empty, assembly.Location);
        Assert.Equal("ProbeModule", assembly.GetName().Name);
    }

    [Fact]
    public void Load_VerifiedImageWithSameIdentityAndMvidButDifferentIl_CannotReuseDefaultAssembly()
    {
        var modulesRoot = Path.Combine(_root, "modules");
        var packageRoot = Path.Combine(modulesRoot, "Audit");
        Directory.CreateDirectory(packageRoot);
        var entryPath = Path.Combine(_root, "AuditModule.dll");
        var alteredImage = File.ReadAllBytes(typeof(AuditModule).Assembly.Location);
        ReplaceAllUtf16(alteredImage, "1.0.0", "9.0.0");
        File.WriteAllBytes(entryPath, alteredImage);
        File.WriteAllText(
            Path.Combine(packageRoot, "plugin.json"),
            """
            {
              "schemaVersion": "1.0",
              "moduleId": "Audit",
              "displayName": "审计日志",
              "version": "1.0.0",
              "entryAssembly": "../../AuditModule.dll",
              "dependencies": []
            }
            """);
        var source = TrustedBuiltInModuleSource.CreateForHost(_root, modulesRoot);
        var candidate = Assert.Single(new ModuleCapsuleCandidateReader().ReadTrustedBuiltIn(source));
        var plan = Assert.IsType<CanonicalModulePlan>(
            new CanonicalModulePlanCompiler().Compile("altered-audit", [candidate]).Plan);

        var error = Assert.Throws<InvalidDataException>(() =>
            new CanonicalModuleAssemblyLoader(_root).Load(plan));

        Assert.Contains("does not match", error.Message, StringComparison.Ordinal);
    }

    private static void ReplaceAllUtf16(byte[] image, string oldValue, string newValue)
    {
        var oldBytes = System.Text.Encoding.Unicode.GetBytes(oldValue);
        var newBytes = System.Text.Encoding.Unicode.GetBytes(newValue);
        Assert.Equal(oldBytes.Length, newBytes.Length);
        var replaced = 0;
        for (var offset = 0; offset <= image.Length - oldBytes.Length; offset++)
        {
            if (!image.AsSpan(offset, oldBytes.Length).SequenceEqual(oldBytes))
            {
                continue;
            }

            newBytes.CopyTo(image.AsSpan(offset, newBytes.Length));
            replaced++;
        }

        Assert.True(replaced > 0, $"Test fixture did not contain '{oldValue}'.");
    }

    private static void ReplaceAllUtf8(byte[] image, string oldValue, string newValue)
    {
        var oldBytes = System.Text.Encoding.UTF8.GetBytes(oldValue);
        var newBytes = System.Text.Encoding.UTF8.GetBytes(newValue);
        Assert.Equal(oldBytes.Length, newBytes.Length);
        var replaced = 0;
        for (var offset = 0; offset <= image.Length - oldBytes.Length; offset++)
        {
            if (!image.AsSpan(offset, oldBytes.Length).SequenceEqual(oldBytes))
            {
                continue;
            }

            newBytes.CopyTo(image.AsSpan(offset, newBytes.Length));
            replaced++;
        }

        Assert.True(replaced > 0, $"Test fixture did not contain '{oldValue}'.");
    }

    [Fact]
    public void SharedDependencyPolicy_RejectsSameNameWithWrongVersionCultureOrToken()
    {
        var expected = new System.Reflection.AssemblyName(
            "PlatformShared, Version=1.2.3.4, Culture=en-US, PublicKeyToken=0011223344556677");
        var wrongIdentities = new[]
        {
            new System.Reflection.AssemblyName(
                "PlatformShared, Version=9.2.3.4, Culture=en-US, PublicKeyToken=0011223344556677"),
            new System.Reflection.AssemblyName(
                "PlatformShared, Version=1.2.3.4, Culture=fr-FR, PublicKeyToken=0011223344556677"),
            new System.Reflection.AssemblyName(
                "PlatformShared, Version=1.2.3.4, Culture=en-US, PublicKeyToken=8899aabbccddeeff")
        };

        Assert.All(wrongIdentities, actual =>
            Assert.False(VerifiedAssemblyIdentityPolicy.MatchesExact(actual, expected)));
        Assert.False(VerifiedAssemblyIdentityPolicy.IsHostPlatformSharedDependency(
            new System.Reflection.AssemblyName("AuditModule, Version=1.0.0.0")));
        Assert.False(VerifiedAssemblyIdentityPolicy.IsHostPlatformSharedDependency(expected));
        Assert.True(VerifiedAssemblyIdentityPolicy.IsHostPlatformSharedDependency(
            new System.Reflection.AssemblyName("PlatformPersistence, Version=1.0.0.0")));
        Assert.False(VerifiedAssemblyIdentityPolicy.IsHostPlatformSharedDependency(
            new System.Reflection.AssemblyName("HostApi, Version=1.0.0.0")));
        Assert.False(VerifiedAssemblyIdentityPolicy.IsHostPlatformSharedDependency(
            new System.Reflection.AssemblyName("Microsoft.Evil, Version=1.0.0.0, PublicKeyToken=null")));

#pragma warning disable SYSLIB0037
        var expectedArchitecture = new System.Reflection.AssemblyName(expected.FullName)
        {
            ProcessorArchitecture = System.Reflection.ProcessorArchitecture.MSIL
        };
        var wrongArchitecture = new System.Reflection.AssemblyName(expected.FullName)
        {
            ProcessorArchitecture = System.Reflection.ProcessorArchitecture.Amd64
        };
#pragma warning restore SYSLIB0037
        var wrongContentType = new System.Reflection.AssemblyName(expected.FullName)
        {
            ContentType = System.Reflection.AssemblyContentType.WindowsRuntime
        };
        Assert.False(VerifiedAssemblyIdentityPolicy.MatchesExact(wrongArchitecture, expectedArchitecture));
        Assert.False(VerifiedAssemblyIdentityPolicy.MatchesExact(wrongContentType, expected));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

}
