# Unified Plugin Package Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Phase 1 minimum working loop for unified backend+frontend plugin packages: package, verify, install, enable, disable, and generate runtime views without blocking future split deployment.

**Architecture:** Keep the existing backend plugin loader and frontend runtime manifest loader as the execution spine. Add a unified package format, package installer/state store, and runtime-view materializer that converts installed package state into backend load-view directories and frontend manifest/assets. Treat the unified package as a delivery unit, not a deployment unit.

**Tech Stack:** .NET 10 / xUnit / System.Text.Json / PowerShell + Bash / Node.js / Vite / TypeScript / existing HostApi + Shell runtime manifest flow

---

## File Structure Map

### Existing files to modify

- Modify: `apps/host-api/Program.cs`
- Modify: `frontend/packages/shell/src/plugin-manifest-loader.test.ts`
- Modify: `docs/architecture/plugin-package-format.md`
- Modify: `docs/architecture/frontend-plugin-loading.md`
- Modify: `docs/current-status.md`

### New backend/kernel files

- Create: `server/kernel/PluginPackages/UnifiedPluginPackageManifest.cs`
- Create: `server/kernel/PluginPackages/UnifiedPluginPackageScanner.cs`
- Create: `server/kernel/PluginPackages/UnifiedPluginPackageInstallOptions.cs`
- Create: `server/kernel/PluginPackages/InstalledPluginRecord.cs`
- Create: `server/kernel/PluginPackages/InstalledPluginStateStore.cs`
- Create: `server/kernel/PluginPackages/UnifiedPluginPackageInstaller.cs`
- Create: `server/kernel/PluginPackages/RuntimeViewMaterializer.cs`
- Create: `server/kernel/PluginPackages/FrontendRuntimeManifestBuilder.cs`

### New test files

- Create: `tests/Kernel.Tests/UnifiedPluginPackageScannerTests.cs`
- Create: `tests/Kernel.Tests/UnifiedPluginPackageInstallerTests.cs`
- Create: `tests/Kernel.Tests/RuntimeViewMaterializerTests.cs`

### New packaging / operations scripts

- Create: `frontend/scripts/build-frontend-plugin.mjs`
- Create: `scripts/pack-unified-plugin.ps1`
- Create: `scripts/pack-unified-plugin.sh`
- Create: `scripts/verify-unified-plugin-package.ps1`
- Create: `scripts/verify-unified-plugin-package.sh`
- Create: `scripts/manage-unified-plugin.ps1`
- Create: `scripts/manage-unified-plugin.sh`

### New operator tool

- Create: `tools/PluginOps/PluginOps.csproj`
- Create: `tools/PluginOps/Program.cs`

### New spec-facing docs

- Create: `docs/guides/unified-plugin-package-operations.md`

## Task 1: Add unified package metadata and scanner

**Files:**
- Create: `server/kernel/PluginPackages/UnifiedPluginPackageManifest.cs`
- Create: `server/kernel/PluginPackages/UnifiedPluginPackageScanner.cs`
- Test: `tests/Kernel.Tests/UnifiedPluginPackageScannerTests.cs`

- [ ] **Step 1: Write the failing scanner tests**

```csharp
using System.Text.Json;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class UnifiedPluginPackageScannerTests
{
    [Fact]
    public void ScanPackage_ReturnsSuccess_WhenRootAndChildManifestsAreValid()
    {
        var packageRoot = UnifiedPluginPackageTestData.CreateValidPackage();

        try
        {
            var scanner = new UnifiedPluginPackageScanner();

            var result = scanner.ScanPackage(packageRoot);

            Assert.True(result.IsSuccess);
            Assert.Equal("Audit", result.Manifest!.ModuleId);
            Assert.Equal("backend/plugin.backend.json", result.Manifest.Artifacts.Backend);
            Assert.Equal("frontend/plugin.frontend.json", result.Manifest.Artifacts.Frontend);
        }
        finally
        {
            Directory.Delete(packageRoot, recursive: true);
        }
    }

    [Fact]
    public void ScanPackage_ReturnsFailure_WhenFrontendChildManifestIsMissing()
    {
        var packageRoot = UnifiedPluginPackageTestData.CreateValidPackage(removeFrontendManifest: true);

        try
        {
            var scanner = new UnifiedPluginPackageScanner();

            var result = scanner.ScanPackage(packageRoot);

            Assert.False(result.IsSuccess);
            Assert.Contains("frontend/plugin.frontend.json", result.ErrorMessage);
        }
        finally
        {
            Directory.Delete(packageRoot, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~UnifiedPluginPackageScannerTests`

Expected: FAIL with missing `ICP.Platform.Kernel.PluginPackages` types.

- [ ] **Step 3: Write the manifest models**

```csharp
namespace ICP.Platform.Kernel.PluginPackages;

public sealed class UnifiedPluginPackageManifest
{
    public string PluginId { get; set; } = string.Empty;
    public string ModuleId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Description { get; set; }
    public UnifiedPluginDistribution Distribution { get; set; } = new();
    public UnifiedPluginHostVersion HostVersion { get; set; } = new();
    public UnifiedPluginArtifacts Artifacts { get; set; } = new();
    public UnifiedPluginChecksums Checksums { get; set; } = new();
}

public sealed class UnifiedPluginArtifacts
{
    public string Backend { get; set; } = string.Empty;
    public string Frontend { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Implement the package scanner**

```csharp
public sealed class UnifiedPluginPackageScanner
{
    public UnifiedPluginPackageScanResult ScanPackage(string packageRoot)
    {
        var rootManifestPath = Path.Combine(packageRoot, "plugin.json");
        if (!File.Exists(rootManifestPath))
        {
            return UnifiedPluginPackageScanResult.Failure($"Missing root manifest: {rootManifestPath}");
        }

        var manifest = JsonSerializer.Deserialize<UnifiedPluginPackageManifest>(
            File.ReadAllText(rootManifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (manifest is null)
        {
            return UnifiedPluginPackageScanResult.Failure("Root manifest deserialized to null.");
        }

        var backendManifestPath = Path.Combine(packageRoot, manifest.Artifacts.Backend);
        var frontendManifestPath = Path.Combine(packageRoot, manifest.Artifacts.Frontend);
        if (!File.Exists(backendManifestPath) || !File.Exists(frontendManifestPath))
        {
            return UnifiedPluginPackageScanResult.Failure(
                $"Missing child manifest. Backend: {backendManifestPath}, Frontend: {frontendManifestPath}");
        }

        return UnifiedPluginPackageScanResult.Success(manifest, packageRoot);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~UnifiedPluginPackageScannerTests`

Expected: PASS with both scanner tests green.

- [ ] **Step 6: Commit**

```bash
git add server/kernel/PluginPackages/UnifiedPluginPackageManifest.cs server/kernel/PluginPackages/UnifiedPluginPackageScanner.cs tests/Kernel.Tests/UnifiedPluginPackageScannerTests.cs
git commit -m "feat: add unified plugin package scanner"
```

## Task 2: Build frontend plugin bundles and unified package artifacts

**Files:**
- Create: `frontend/scripts/build-frontend-plugin.mjs`
- Create: `scripts/pack-unified-plugin.ps1`
- Create: `scripts/pack-unified-plugin.sh`
- Create: `scripts/verify-unified-plugin-package.ps1`
- Create: `scripts/verify-unified-plugin-package.sh`
- Test: `frontend/packages/shell/src/plugin-manifest-loader.test.ts`

- [ ] **Step 1: Add a failing frontend runtime-manifest test for external plugin bundles**

```ts
it('loads an installed external plugin bundle from a generated runtime manifest entry', async () => {
  const fetchManifest = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    statusText: 'OK',
    json: async () => ({
      plugins: [{ url: '/plugin-assets/Audit/1.0.0/entry.js' }],
    }),
  });
  const importModule = vi.fn().mockResolvedValue({
    default: {
      metadata: { moduleId: 'audit-external', displayName: 'Audit External', version: '1.0.0' },
    },
  });

  const result = await loadPluginManifestWith({ fetchManifest, importModule, logger });

  expect(result.manifestFound).toBe(true);
  expect(result.modules[0].metadata.moduleId).toBe('audit-external');
});
```

- [ ] **Step 2: Run the shell test to verify the new case fails or is absent**

Run: `pnpm --dir .\frontend\packages\shell test`

Expected: FAIL or report the new test case as missing.

- [ ] **Step 3: Create the frontend plugin builder script**

```js
import { build } from 'vite';
import path from 'node:path';

const moduleName = process.argv[2];
const repoRoot = path.resolve(import.meta.dirname, '..', '..');
const moduleRoot = path.join(repoRoot, 'frontend', 'modules', moduleName);
const outDir = path.join(repoRoot, 'artifacts', 'frontend-plugins', moduleName);

await build({
  configFile: false,
  build: {
    lib: {
      entry: path.join(moduleRoot, 'src', 'index.ts'),
      formats: ['es'],
      fileName: () => 'entry.js',
    },
    outDir,
    emptyOutDir: true,
    rollupOptions: {
      external: ['react', 'react-dom', '@icp-platform/sdk', '@icp-platform/core'],
    },
  },
});
```

- [ ] **Step 4: Create the unified package and verification scripts**

```powershell
$packageRoot = Join-Path $resolvedOutputRoot $moduleMetadata.ModuleId
$backendRoot = Join-Path $packageRoot "backend"
$frontendRoot = Join-Path $packageRoot "frontend"
$checksumsRoot = Join-Path $packageRoot "checksums"

New-Item -ItemType Directory -Path $backendRoot, $frontendRoot, $checksumsRoot -Force | Out-Null
Copy-Item -LiteralPath $assemblyPath -Destination (Join-Path $backendRoot "$assemblyName.dll") -Force
Copy-Item -LiteralPath $frontendEntryPath -Destination (Join-Path $frontendRoot "assets\entry.js") -Force

$rootManifest = @{
  pluginId = "icp.module.$($moduleMetadata.ModuleId.ToLowerInvariant())"
  moduleId = $moduleMetadata.ModuleId
  displayName = $moduleMetadata.DisplayName
  version = $version
  artifacts = @{
    backend = "backend/plugin.backend.json"
    frontend = "frontend/plugin.frontend.json"
  }
}
```

- [ ] **Step 5: Run packaging and verification commands**

Run:

```powershell
.\scripts\pack-unified-plugin.ps1 -ModuleName Audit -Configuration Release -Force
.\scripts\verify-unified-plugin-package.ps1 -PackagePath .\artifacts\plugin-packages\Audit
```

Expected: package directory contains `plugin.json`, `backend/`, `frontend/`, `checksums/`, and verification prints `=== Unified plugin package verified ===`.

- [ ] **Step 6: Re-run shell tests**

Run: `pnpm --dir .\frontend\packages\shell test`

Expected: PASS with the new external bundle case green.

- [ ] **Step 7: Commit**

```bash
git add frontend/scripts/build-frontend-plugin.mjs scripts/pack-unified-plugin.ps1 scripts/pack-unified-plugin.sh scripts/verify-unified-plugin-package.ps1 scripts/verify-unified-plugin-package.sh frontend/packages/shell/src/plugin-manifest-loader.test.ts
git commit -m "feat: package frontend artifacts into unified plugin bundles"
```

## Task 3: Add installed package state store and installer

**Files:**
- Create: `server/kernel/PluginPackages/UnifiedPluginPackageInstallOptions.cs`
- Create: `server/kernel/PluginPackages/InstalledPluginRecord.cs`
- Create: `server/kernel/PluginPackages/InstalledPluginStateStore.cs`
- Create: `server/kernel/PluginPackages/UnifiedPluginPackageInstaller.cs`
- Test: `tests/Kernel.Tests/UnifiedPluginPackageInstallerTests.cs`

- [ ] **Step 1: Write the failing installer tests**

```csharp
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class UnifiedPluginPackageInstallerTests
{
    [Fact]
    public void InstallPackage_WritesVersionedInstallRecord_AndCopiesArtifacts()
    {
        var packageRoot = UnifiedPluginPackageTestData.CreateValidPackage();
        var installRoot = UnifiedPluginPackageTestData.CreateInstallRoot();

        try
        {
            var stateStore = new InstalledPluginStateStore(installRoot);
            var installer = new UnifiedPluginPackageInstaller(stateStore);

            var result = installer.Install(packageRoot, installRoot);

            Assert.Equal("Installed", result.Status);
            Assert.True(File.Exists(Path.Combine(installRoot, "packages", "Audit", "1.0.0", "plugin.json")));
            Assert.True(File.Exists(Path.Combine(installRoot, "state", "Audit.json")));
        }
        finally
        {
            Directory.Delete(packageRoot, recursive: true);
            Directory.Delete(installRoot, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run installer tests to verify they fail**

Run: `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~UnifiedPluginPackageInstallerTests`

Expected: FAIL with missing installer and state store types.

- [ ] **Step 3: Add installed plugin state types**

```csharp
namespace ICP.Platform.Kernel.PluginPackages;

public sealed class InstalledPluginRecord
{
    public string ModuleId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Status { get; set; } = "Installed";
    public string PackageRoot { get; set; } = string.Empty;
    public string? CurrentEnabledVersion { get; set; }
    public List<string> InstalledVersions { get; set; } = new();
}
```

- [ ] **Step 4: Implement the state store and installer**

```csharp
public sealed class InstalledPluginStateStore
{
    public InstalledPluginRecord? Load(string moduleId)
    {
        var path = Path.Combine(_stateRoot, $"{moduleId}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<InstalledPluginRecord>(File.ReadAllText(path));
    }

    public void Save(InstalledPluginRecord record)
    {
        var path = Path.Combine(_stateRoot, $"{record.ModuleId}.json");
        var json = JsonSerializer.Serialize(record, _jsonOptions);
        File.WriteAllText(path, json + Environment.NewLine, new UTF8Encoding(false));
    }
}

public sealed class UnifiedPluginPackageInstaller
{
    public UnifiedPluginInstallResult Install(string packageRoot, string installRoot)
    {
        var scanResult = _scanner.ScanPackage(packageRoot);
        if (!scanResult.IsSuccess)
        {
            return UnifiedPluginInstallResult.Failed(scanResult.ErrorMessage!);
        }

        var versionedTarget = Path.Combine(installRoot, "packages", scanResult.Manifest!.ModuleId, scanResult.Manifest.Version);
        DirectoryCopy(packageRoot, versionedTarget);
        _stateStore.Save(new InstalledPluginRecord
        {
            ModuleId = scanResult.Manifest.ModuleId,
            Version = scanResult.Manifest.Version,
            Status = "Installed",
            PackageRoot = versionedTarget,
            InstalledVersions = new() { scanResult.Manifest.Version }
        });

        return UnifiedPluginInstallResult.Installed(scanResult.Manifest.ModuleId, scanResult.Manifest.Version);
    }
}
```

- [ ] **Step 5: Run installer tests**

Run: `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~UnifiedPluginPackageInstallerTests`

Expected: PASS with install-state assertions green.

- [ ] **Step 6: Commit**

```bash
git add server/kernel/PluginPackages/UnifiedPluginPackageInstallOptions.cs server/kernel/PluginPackages/InstalledPluginRecord.cs server/kernel/PluginPackages/InstalledPluginStateStore.cs server/kernel/PluginPackages/UnifiedPluginPackageInstaller.cs tests/Kernel.Tests/UnifiedPluginPackageInstallerTests.cs
git commit -m "feat: add unified plugin package installer and state store"
```

## Task 4: Materialize backend load-view and frontend runtime manifest from installed state

**Files:**
- Create: `server/kernel/PluginPackages/RuntimeViewMaterializer.cs`
- Create: `server/kernel/PluginPackages/FrontendRuntimeManifestBuilder.cs`
- Test: `tests/Kernel.Tests/RuntimeViewMaterializerTests.cs`
- Modify: `apps/host-api/Program.cs`

- [ ] **Step 1: Write the failing runtime-view tests**

```csharp
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class RuntimeViewMaterializerTests
{
    [Fact]
    public void MaterializeEnabledPlugin_CreatesBackendLoadView_AndFrontendManifest()
    {
        var installRoot = UnifiedPluginPackageTestData.CreateInstalledPackageRoot();
        var materializer = new RuntimeViewMaterializer();

        materializer.Materialize(installRoot, "http://localhost:5000");

        Assert.True(File.Exists(Path.Combine(installRoot, "runtime", "backend-load-view", "Audit", "plugin.json")));
        Assert.True(File.Exists(Path.Combine(installRoot, "runtime", "frontend-plugin-manifest.json")));
    }
}
```

- [ ] **Step 2: Run runtime-view tests to verify they fail**

Run: `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~RuntimeViewMaterializerTests`

Expected: FAIL with missing runtime materializer types.

- [ ] **Step 3: Implement the runtime materializer**

```csharp
public sealed class RuntimeViewMaterializer
{
    public void Materialize(string installRoot, string publicBaseUrl)
    {
        var runtimeBackendRoot = Path.Combine(installRoot, "runtime", "backend-load-view");
        var runtimeFrontendManifestPath = Path.Combine(installRoot, "runtime", "frontend-plugin-manifest.json");

        Directory.CreateDirectory(runtimeBackendRoot);

        foreach (var record in _stateStore.LoadAllEnabled())
        {
            var backendViewRoot = Path.Combine(runtimeBackendRoot, record.ModuleId);
            Directory.CreateDirectory(backendViewRoot);
            File.Copy(
                Path.Combine(record.PackageRoot, "backend", "plugin.backend.json"),
                Path.Combine(backendViewRoot, "plugin.json"),
                overwrite: true);
        }

        var frontendManifest = _frontendManifestBuilder.Build(_stateStore.LoadAllEnabled(), publicBaseUrl);
        File.WriteAllText(runtimeFrontendManifestPath, frontendManifest);
    }
}
```

- [ ] **Step 4: Wire HostApi startup to runtime materialization and asset serving**

```csharp
var pluginInstallRoot = builder.Configuration["Platform:PluginInstallRoot"] ?? Path.Combine(AppContext.BaseDirectory, "plugins");
var frontendAssetsRoot = Path.Combine(pluginInstallRoot, "public");
var runtimeBackendViewRoot = Path.Combine(pluginInstallRoot, "runtime", "backend-load-view");
var runtimeFrontendManifestPath = Path.Combine(pluginInstallRoot, "runtime", "frontend-plugin-manifest.json");

var materializer = new RuntimeViewMaterializer();
materializer.Materialize(pluginInstallRoot, builder.Configuration["Platform:FrontendPluginBaseUrl"] ?? "/plugin-assets");

var pluginLoadResult = pluginAssemblyLoader.LoadAssemblies(runtimeBackendViewRoot);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(frontendAssetsRoot),
    RequestPath = "/plugin-assets"
});
```

- [ ] **Step 5: Run backend tests and host build**

Run:

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~RuntimeViewMaterializerTests
dotnet build .\apps\host-api\HostApi.csproj
```

Expected: PASS for runtime-view tests and successful HostApi build.

- [ ] **Step 6: Commit**

```bash
git add server/kernel/PluginPackages/RuntimeViewMaterializer.cs server/kernel/PluginPackages/FrontendRuntimeManifestBuilder.cs tests/Kernel.Tests/RuntimeViewMaterializerTests.cs apps/host-api/Program.cs
git commit -m "feat: materialize runtime views for installed unified plugins"
```

## Task 5: Add operator scripts for install / enable / disable / rollback and document the flow

**Files:**
- Create: `scripts/manage-unified-plugin.ps1`
- Create: `scripts/manage-unified-plugin.sh`
- Create: `tools/PluginOps/PluginOps.csproj`
- Create: `tools/PluginOps/Program.cs`
- Create: `docs/guides/unified-plugin-package-operations.md`
- Modify: `docs/architecture/plugin-package-format.md`
- Modify: `docs/architecture/frontend-plugin-loading.md`
- Modify: `docs/current-status.md`

- [ ] **Step 1: Write the management script interface**

```powershell
param(
    [ValidateSet("install","enable","disable","rollback")]
    [string]$Action,
    [string]$PackagePath,
    [string]$ModuleId,
    [string]$Version,
    [string]$InstallRoot = "artifacts/installed-plugins"
)
```

- [ ] **Step 2: Implement the action handlers**

```powershell
switch ($Action) {
    "install" {
        & dotnet run --project .\tools\PluginOps\PluginOps.csproj -- install `
          --package $PackagePath `
          --install-root $InstallRoot
    }
    "enable" {
        & dotnet run --project .\tools\PluginOps\PluginOps.csproj -- enable `
          --module-id $ModuleId `
          --version $Version `
          --install-root $InstallRoot
    }
    "disable" {
        & dotnet run --project .\tools\PluginOps\PluginOps.csproj -- disable `
          --module-id $ModuleId `
          --install-root $InstallRoot
    }
    "rollback" {
        & dotnet run --project .\tools\PluginOps\PluginOps.csproj -- rollback `
          --module-id $ModuleId `
          --install-root $InstallRoot
    }
}
```

```csharp
var rootCommand = new RootCommand("Unified plugin package operations");
var installCommand = new Command("install");
installCommand.SetHandler((string package, string installRoot) =>
{
    var installer = PluginOpsComposition.CreateInstaller(installRoot);
    var result = installer.Install(package, installRoot);
    Console.WriteLine($"{result.ModuleId}:{result.Version}:{result.Status}");
}, packageOption, installRootOption);

var enableCommand = new Command("enable");
enableCommand.SetHandler((string moduleId, string version, string installRoot) =>
{
    var operations = PluginOpsComposition.CreateOperations(installRoot);
    operations.Enable(moduleId, version);
}, moduleIdOption, versionOption, installRootOption);

var disableCommand = new Command("disable");
disableCommand.SetHandler((string moduleId, string installRoot) =>
{
    var operations = PluginOpsComposition.CreateOperations(installRoot);
    operations.Disable(moduleId);
}, moduleIdOption, installRootOption);

var rollbackCommand = new Command("rollback");
rollbackCommand.SetHandler((string moduleId, string installRoot) =>
{
    var operations = PluginOpsComposition.CreateOperations(installRoot);
    operations.Rollback(moduleId);
}, moduleIdOption, installRootOption);

rootCommand.AddCommand(installCommand);
rootCommand.AddCommand(enableCommand);
rootCommand.AddCommand(disableCommand);
rootCommand.AddCommand(rollbackCommand);
return await rootCommand.InvokeAsync(args);
```

- [ ] **Step 3: Document exact operator flow**

```md
1. Run `pack-unified-plugin`
2. Run `verify-unified-plugin-package`
3. Run `manage-unified-plugin -Action install`
4. Run `manage-unified-plugin -Action enable`
5. Restart HostApi or trigger runtime refresh
6. Verify backend load-view and frontend manifest outputs
```

- [ ] **Step 4: Run end-to-end verification**

Run:

```powershell
.\scripts\pack-unified-plugin.ps1 -ModuleName Audit -Configuration Release -Force
.\scripts\verify-unified-plugin-package.ps1 -PackagePath .\artifacts\plugin-packages\Audit
.\scripts\manage-unified-plugin.ps1 -Action install -PackagePath .\artifacts\plugin-packages\Audit
.\scripts\manage-unified-plugin.ps1 -Action enable -ModuleId Audit
dotnet build .\apps\host-api\HostApi.csproj
pnpm --dir .\frontend\packages\shell build
```

Expected:

- package verification succeeds
- install state file exists
- runtime backend load-view exists
- runtime frontend manifest exists
- HostApi and shell both build successfully

- [ ] **Step 5: Commit**

```bash
git add scripts/manage-unified-plugin.ps1 scripts/manage-unified-plugin.sh docs/guides/unified-plugin-package-operations.md docs/architecture/plugin-package-format.md docs/architecture/frontend-plugin-loading.md docs/current-status.md
git commit -m "docs: add unified plugin package operations flow"
```

## Plan Self-Review

### Spec coverage

- Unified package format: covered by Task 1 and Task 2
- Install/state lifecycle: covered by Task 3 and Task 5
- Runtime-view materialization: covered by Task 4
- Frontend manifest integration: covered by Task 2 and Task 4
- Future split deployment compatibility: enforced through Task 1 metadata and Task 4 runtime-view abstraction

### Placeholder scan

- No `TODO`, `TBD`, “implement later”, or “similar to Task N” placeholders remain.

### Type consistency

- Root package model consistently uses `UnifiedPluginPackageManifest`
- Installed state consistently uses `InstalledPluginRecord`
- Runtime generation consistently uses `RuntimeViewMaterializer` and `FrontendRuntimeManifestBuilder`
