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
        ReplaceAllUtf8(alteredImage, "1.0.0", "9.0.0");
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
            new CanonicalModulePlanCompiler().Compile("altered-files", [candidate]).Plan);

        var error = Assert.Throws<InvalidDataException>(() =>
            new CanonicalModuleAssemblyLoader(_root).Load(plan));

        Assert.Contains("does not match", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_AcceptedNativeContractsAbsentFromHost_SharesSignedTypeAcrossOwners()
    {
        const string contractName = "Contract.Contracts";
        Assert.DoesNotContain(System.Runtime.Loader.AssemblyLoadContext.Default.Assemblies,
            assembly => assembly.GetName().Name == contractName);
        var contract = File.ReadAllBytes(typeof(ICP.Platform.Modules.Document.Contracts.IDocumentFiles).Assembly.Location);
        ReplaceAllUtf8(contract, "Document.Contracts", contractName);
        var document = File.ReadAllBytes(typeof(ICP.Platform.Modules.Document.DocumentModule).Assembly.Location);
        var workspace = File.ReadAllBytes(typeof(ICP.Platform.Modules.Workspace.WorkspaceModule).Assembly.Location);
        ReplaceAllUtf8(document, "Document.Contracts", contractName);
        ReplaceAllUtf8(workspace, "Document.Contracts", contractName);
        CopyHostFileContract();
        var plan = NativePlan(
            ("Document", document, contractName, contract),
            ("Workspace", workspace, contractName, contract));
        // 签发后的旁文件修改不能替换实际消费的 Contracts 字节。
        foreach (var module in plan.Modules)
            File.WriteAllBytes(Path.Combine(module.PackageRoot, "backend", contractName + ".dll"), [0]);

        var assemblies = new CanonicalModuleAssemblyLoader(_root, new TrustedDefaultModuleAssemblyRegistry()).Load(plan);

        var fileInterface = assemblies.Single(assembly => assembly.GetName().Name == "DocumentModule")
            .GetType("ICP.Platform.Modules.Document.DocumentFiles", throwOnError: true)!.GetInterfaces()
            .Single(type => type.Name == "IDocumentFiles");
        var parameter = assemblies.Single(assembly => assembly.GetName().Name == "WorkspaceModule")
            .GetType("ICP.Platform.Modules.Workspace.WorkspaceContents", throwOnError: true)!.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters()).Single(parameter => parameter.ParameterType.Name == "IDocumentFiles");
        Assert.Same(fileInterface, parameter.ParameterType);
        Assert.Equal(contractName, fileInterface.Assembly.GetName().Name);
        Assert.Same(System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(assemblies[0]),
            System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(assemblies[1]));
    }

    [Fact]
    public void Load_AcceptedNativeContractsWithConflictingBytes_RejectsBeforeLoadingModules()
    {
        var contract = File.ReadAllBytes(typeof(ICP.Platform.Modules.Document.Contracts.IDocumentFiles).Assembly.Location);
        var changed = contract.ToArray();
        changed[^1] ^= 1;
        var plan = NativePlan(
            ("Document", File.ReadAllBytes(typeof(ICP.Platform.Modules.Document.DocumentModule).Assembly.Location), "Document.Contracts", contract),
            ("Workspace", File.ReadAllBytes(typeof(ICP.Platform.Modules.Workspace.WorkspaceModule).Assembly.Location), "Document.Contracts", changed));

        var error = Assert.Throws<InvalidDataException>(() => new CanonicalModuleAssemblyLoader(_root).Load(plan));

        Assert.Contains("conflicting", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_AcceptedNativeContractDifferentFromHostImage_RejectsSharedType()
    {
        var contract = File.ReadAllBytes(typeof(ICP.Platform.Modules.Files.Contracts.IFileLifecycleService).Assembly.Location);
        var changed = contract.ToArray();
        // 相同 identity/MVID 的不同 IL/常量映像也不能混用。
        changed[^1] ^= 1;
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, "Files.Contracts.dll"), changed);
        var plan = NativePlan(("Document", File.ReadAllBytes(typeof(ICP.Platform.Modules.Document.DocumentModule).Assembly.Location), "Files.Contracts", contract));

        var error = Assert.Throws<InvalidDataException>(() => new CanonicalModuleAssemblyLoader(_root).Load(plan));

        Assert.Contains("Host", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_AcceptedNativeContractWithUnregisteredDefaultSameMvid_UsesSignedImage()
    {
        const string contractName = "Preloadx.Contracts";
        var signed = File.ReadAllBytes(typeof(ICP.Platform.Modules.Document.Contracts.IDocumentFiles).Assembly.Location);
        ReplaceAllUtf8(signed, "Document.Contracts", contractName);
        var malicious = signed.ToArray();
        ReplaceAllUtf16(malicious, "DocumentFileItem", "DocumentFileIteM");
        using var stream = new MemoryStream(malicious);
        var preloaded = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(stream);
        var document = File.ReadAllBytes(typeof(ICP.Platform.Modules.Document.DocumentModule).Assembly.Location);
        ReplaceAllUtf8(document, "Document.Contracts", contractName);
        CopyHostFileContract();
        // 即使 Host 磁盘旁放回 genuine 字节，也不能为恶意内存体补办装载证明。
        File.WriteAllBytes(Path.Combine(_root, contractName + ".dll"), signed);
        var plan = NativePlan(("Document", document, contractName, signed));

        var assembly = Assert.Single(new CanonicalModuleAssemblyLoader(_root, new TrustedDefaultModuleAssemblyRegistry()).Load(plan));

        var actual = assembly.GetType("ICP.Platform.Modules.Document.DocumentFiles", throwOnError: true)!
            .GetInterfaces().Single(type => type.Name == "IDocumentFiles").Assembly;
        Assert.NotSame(preloaded, actual);
        Assert.Equal(preloaded.ManifestModule.ModuleVersionId, actual.ManifestModule.ModuleVersionId);
        var item = actual.GetType("ICP.Platform.Modules.Preloadx.Contracts.DocumentFileItem", throwOnError: true)!;
        Assert.StartsWith("DocumentFileItem", Activator.CreateInstance(item, "link", "main", 0L, "main.pdf", 1L, "application/pdf")!.ToString());
    }

    [Fact]
    public void Load_AcceptedNativeContractsWithGenuineHostPreload_SharesActualConsumerTypes()
    {
        CopyHostFileContract();
        var contract = File.ReadAllBytes(typeof(ICP.Platform.Modules.Document.Contracts.IDocumentFiles).Assembly.Location);
        File.WriteAllBytes(Path.Combine(_root, "Document.Contracts.dll"), contract);
        var plan = NativePlan(
            ("Files", File.ReadAllBytes(typeof(ICP.Platform.Modules.Files.FilesModule).Assembly.Location), "Files.Contracts",
                File.ReadAllBytes(typeof(ICP.Platform.Modules.Files.Contracts.IFileLifecycleService).Assembly.Location)),
            ("Document", File.ReadAllBytes(typeof(ICP.Platform.Modules.Document.DocumentModule).Assembly.Location), "Document.Contracts", contract),
            ("Workspace", File.ReadAllBytes(typeof(ICP.Platform.Modules.Workspace.WorkspaceModule).Assembly.Location), "Document.Contracts", contract));

        var assemblies = new CanonicalModuleAssemblyLoader(_root, new TrustedDefaultModuleAssemblyRegistry()).Load(plan);

        var document = assemblies.Single(assembly => assembly.GetName().Name == "DocumentModule")
            .GetType("ICP.Platform.Modules.Document.DocumentFiles", throwOnError: true)!;
        var files = assemblies.Single(assembly => assembly.GetName().Name == "FilesModule")
            .GetType("ICP.Platform.Modules.Files.Services.FileLifecycleService", throwOnError: true)!;
        var workspace = assemblies.Single(assembly => assembly.GetName().Name == "WorkspaceModule")
            .GetType("ICP.Platform.Modules.Workspace.WorkspaceContents", throwOnError: true)!;
        Assert.Same(files.GetInterfaces().Single(type => type.Name == "IFileLifecycleService"),
            document.GetConstructors().SelectMany(constructor => constructor.GetParameters())
                .Single(parameter => parameter.ParameterType.Name == "IFileLifecycleService").ParameterType);
        Assert.Same(document.GetInterfaces().Single(type => type.Name == "IDocumentFiles"),
            workspace.GetConstructors().SelectMany(constructor => constructor.GetParameters())
                .Single(parameter => parameter.ParameterType.Name == "IDocumentFiles").ParameterType);
    }

    [Fact]
    public void Load_AcceptedNativeContractAddedAfterSnapshot_IsNotProbed()
    {
        CopyHostFileContract();
        var plan = NativePlan(("Document", File.ReadAllBytes(typeof(ICP.Platform.Modules.Document.DocumentModule).Assembly.Location),
            "Files.Contracts", File.ReadAllBytes(typeof(ICP.Platform.Modules.Files.Contracts.IFileLifecycleService).Assembly.Location)));
        File.WriteAllBytes(Path.Combine(plan.Modules[0].PackageRoot, "backend", "Document.Contracts.dll"),
            File.ReadAllBytes(typeof(ICP.Platform.Modules.Document.Contracts.IDocumentFiles).Assembly.Location));

        var error = Assert.Throws<System.Reflection.ReflectionTypeLoadException>(() =>
            new CanonicalModuleAssemblyLoader(_root, new TrustedDefaultModuleAssemblyRegistry()).Load(plan));

        Assert.Contains(error.LoaderExceptions, failure => failure!.Message.Contains("Document.Contracts", StringComparison.Ordinal));
    }

    private void CopyHostFileContract()
    {
        Directory.CreateDirectory(_root);
        File.Copy(typeof(ICP.Platform.Modules.Files.Contracts.IFileLifecycleService).Assembly.Location,
            Path.Combine(_root, "Files.Contracts.dll"));
    }

    private CanonicalModulePlan NativePlan(params (string Owner, byte[] Module, string ContractName, byte[] Contract)[] modules)
    {
        var modulesRoot = Path.Combine(_root, "modules");
        Directory.CreateDirectory(modulesRoot);
        var source = TrustedBuiltInModuleSource.CreateForHost(_root, modulesRoot);
        var candidates = new List<ModuleCapsuleCandidate>();
        foreach (var module in modules)
        {
            var root = Path.Combine(_root, "accepted", module.Owner);
            Directory.CreateDirectory(Path.Combine(root, "backend"));
            File.WriteAllBytes(Path.Combine(root, "backend", module.Owner + "Module.dll"), module.Module);
            File.WriteAllBytes(Path.Combine(root, "backend", module.ContractName + ".dll"), module.Contract);
            if (module.ContractName != "Files.Contracts")
                File.WriteAllBytes(Path.Combine(root, "backend", "Files.Contracts.dll"),
                    File.ReadAllBytes(typeof(ICP.Platform.Modules.Files.Contracts.IFileLifecycleService).Assembly.Location));
            File.WriteAllText(Path.Combine(root, "plugin.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                schemaVersion = "1.0", moduleId = module.Owner, displayName = module.Owner, version = "1.0.0",
                entryAssembly = "backend/" + module.Owner + "Module.dll", dependencies = Array.Empty<string>()
            }));
            source.RegisterAcceptedNativeModule(root, VerifiedModuleArtifactSnapshot.Capture(root));
            candidates.Add(new ModuleCapsuleCandidateReader().ReadTrustedNativeModule(root, source));
        }
        return Assert.IsType<CanonicalModulePlan>(new CanonicalModulePlanCompiler().Compile("native-contracts", candidates).Plan);
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
        Assert.True(VerifiedAssemblyIdentityPolicy.IsHostPlatformSharedDependency(new System.Reflection.AssemblyName("Audit.Contracts, Version=1.0.0.0")));
        Assert.True(VerifiedAssemblyIdentityPolicy.IsHostPlatformSharedDependency(
            new System.Reflection.AssemblyName("Organization.Contracts, Version=1.0.0.0")));
        Assert.False(VerifiedAssemblyIdentityPolicy.IsHostPlatformSharedDependency(
            new System.Reflection.AssemblyName("OrganizationModule, Version=1.0.0.0")));
        Assert.False(VerifiedAssemblyIdentityPolicy.IsHostPlatformSharedDependency(
            new System.Reflection.AssemblyName("Unregistered.Contracts, Version=1.0.0.0")));
        Assert.True(VerifiedAssemblyIdentityPolicy.IsHostPlatformSharedDependency(
            new System.Reflection.AssemblyName("PlatformPersistence, Version=1.0.0.0")));
        Assert.True(VerifiedAssemblyIdentityPolicy.IsHostPlatformSharedDependency(
            new System.Reflection.AssemblyName("PlatformPresentation, Version=1.0.0.0")));
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
