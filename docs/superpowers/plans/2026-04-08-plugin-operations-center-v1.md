# 插件运维中心 V1 实施计划

> **给执行代理的说明：** 实施本计划时，必须使用 `subagent-driven-development`（推荐）或 `executing-plans` 技能按任务逐步推进。步骤继续使用复选框语法（`- [ ]`）跟踪进度。

**目标：** 实现一个面向管理员的插件运维中心 V1，在网页内完成统一插件包的上传、校验、安装、启用、禁用、回滚与状态查看。

**架构：** 统一插件包的生命周期逻辑继续保留在现有 Phase 1 内核类型中，HostApi 只新增一层轻量的管理员 API。前端实现为一个独立业务模块，调用新的 HostApi 接口；浏览器不直接传服务器路径，所有上传和安装都由服务端自主管理。

**技术栈：** ASP.NET Core / .NET 10 / xUnit / System.Text.Json / 现有统一插件包内核服务 / React / TypeScript / Vite / Vitest

---

## 文件结构映射

### 需要修改的现有文件

- 修改：`apps/host-api/HostApi.csproj`
- 修改：`apps/host-api/Program.cs`
- 修改：`tests/Kernel.Tests/Kernel.Tests.csproj`
- 修改：`docs/guides/unified-plugin-package-operations.md`
- 修改：`docs/current-status.md`
- 修改：`docs/architecture/frontend-plugin-loading.md`

### 新增 HostApi 后端文件

- 新建：`apps/host-api/Controllers/PluginOperationsController.cs`
- 新建：`apps/host-api/PluginOperations/PluginOperationsOptions.cs`
- 新建：`apps/host-api/PluginOperations/DTOs/PluginModuleStatusDto.cs`
- 新建：`apps/host-api/PluginOperations/DTOs/PluginModuleDetailDto.cs`
- 新建：`apps/host-api/PluginOperations/DTOs/PluginUploadVerifyResponseDto.cs`
- 新建：`apps/host-api/PluginOperations/DTOs/PluginLifecycleActionRequestDto.cs`
- 新建：`apps/host-api/PluginOperations/DTOs/PluginActionResultDto.cs`
- 新建：`apps/host-api/PluginOperations/Services/IPluginOperationsAdminService.cs`
- 新建：`apps/host-api/PluginOperations/Services/PluginOperationsAdminService.cs`
- 新建：`apps/host-api/PluginOperations/Uploads/UploadedPluginPackageRecord.cs`
- 新建：`apps/host-api/PluginOperations/Uploads/IUploadedPluginPackageStore.cs`
- 新建：`apps/host-api/PluginOperations/Uploads/FileSystemUploadedPluginPackageStore.cs`

### 新增后端测试文件

- 新建：`tests/Kernel.Tests/PluginOperationsAdminServiceTests.cs`
- 新建：`tests/Kernel.Tests/PluginOperationsControllerTests.cs`

### 新增前端模块文件

- 新建：`frontend/modules/plugin-ops/package.json`
- 新建：`frontend/modules/plugin-ops/tsconfig.json`
- 新建：`frontend/modules/plugin-ops/src/index.ts`
- 新建：`frontend/modules/plugin-ops/src/api/pluginOperationsClient.ts`
- 新建：`frontend/modules/plugin-ops/src/api/pluginOperationsClient.test.ts`
- 新建：`frontend/modules/plugin-ops/src/pages/PluginOperationsCenter.tsx`
- 新建：`frontend/modules/plugin-ops/src/pages/PluginOperationsCenter.test.tsx`
- 新建：`frontend/modules/plugin-ops/src/types/pluginOperations.ts`

## 任务 1：新增插件运维查询服务与只读管理 API

**文件：**
- 新建：`apps/host-api/PluginOperations/PluginOperationsOptions.cs`
- 新建：`apps/host-api/PluginOperations/DTOs/PluginModuleStatusDto.cs`
- 新建：`apps/host-api/PluginOperations/DTOs/PluginModuleDetailDto.cs`
- 新建：`apps/host-api/PluginOperations/Services/IPluginOperationsAdminService.cs`
- 新建：`apps/host-api/PluginOperations/Services/PluginOperationsAdminService.cs`
- 新建：`apps/host-api/Controllers/PluginOperationsController.cs`
- 修改：`apps/host-api/Program.cs`
- 修改：`apps/host-api/HostApi.csproj`
- 测试：`tests/Kernel.Tests/PluginOperationsAdminServiceTests.cs`
- 测试：`tests/Kernel.Tests/PluginOperationsControllerTests.cs`

- [ ] **步骤 1：先写服务层红灯测试，验证模块状态映射**

```csharp
using ICP.Platform.HostApi.PluginOperations.Services;
using ICP.Platform.Kernel.PluginPackages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class PluginOperationsAdminServiceTests
{
    [Fact]
    public async Task GetModuleStatusesAsync_返回已安装版本和当前启用版本()
    {
        var installRoot = PluginOperationsTestData.CreateInstallRoot();
        var stateStore = new InstalledPluginStateStore(installRoot);
        PluginOperationsTestData.InstallAndEnableAuditVersions(installRoot, stateStore, "1.0.0", "1.1.0");
        var service = PluginOperationsTestData.CreateAdminService(installRoot);

        var modules = await service.GetModuleStatusesAsync(CancellationToken.None);

        var audit = Assert.Single(modules, module => module.ModuleId == "Audit");
        Assert.Equal("1.1.0", audit.CurrentEnabledVersion);
        Assert.Equal(new[] { "1.0.0", "1.1.0" }, audit.InstalledVersions);
        Assert.True(audit.CanDisable);
        Assert.True(audit.CanRollback);
    }
}
```

- [ ] **步骤 2：再写控制器红灯测试，验证管理 API 的访问边界**

```csharp
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class PluginOperationsControllerTests
{
    [Fact]
    public async Task GetModules_匿名请求时返回未授权()
    {
        await using var host = await PluginOperationsTestHost.StartAsync(authenticated: false);

        var response = await host.Client.GetAsync("/api/plugin-ops/modules");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetModules_管理员请求时返回模块状态()
    {
        await using var host = await PluginOperationsTestHost.StartAsync(authenticated: true);

        var response = await host.Client.GetAsync("/api/plugin-ops/modules");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<List<PluginModuleStatusDto>>();
        Assert.NotNull(payload);
    }
}
```

- [ ] **步骤 3：运行测试，确认它们先失败**

运行：

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~PluginOperationsAdminServiceTests|FullyQualifiedName~PluginOperationsControllerTests"
```

预期：

- 失败，因为插件运维服务和控制器类型还不存在。

- [ ] **步骤 4：补上查询 DTO 和配置项**

```csharp
namespace ICP.Platform.HostApi.PluginOperations;

public sealed class PluginOperationsOptions
{
    public string UploadRoot { get; set; } = "uploads";
    public string AdminRoleCode { get; set; } = "LocalAdmin";
}
```

```csharp
namespace ICP.Platform.HostApi.PluginOperations.DTOs;

public sealed class PluginModuleStatusDto
{
    public string ModuleId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> InstalledVersions { get; set; } = new();
    public string? CurrentEnabledVersion { get; set; }
    public string Status { get; set; } = "NotInstalled";
    public string? LastOperation { get; set; }
    public string? LastError { get; set; }
    public bool CanEnable { get; set; }
    public bool CanDisable { get; set; }
    public bool CanRollback { get; set; }
}
```

- [ ] **步骤 5：实现查询服务**

```csharp
public sealed class PluginOperationsAdminService : IPluginOperationsAdminService
{
    private readonly InstalledPluginStateStore _stateStore;

    public PluginOperationsAdminService(InstalledPluginStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public Task<IReadOnlyList<PluginModuleStatusDto>> GetModuleStatusesAsync(CancellationToken cancellationToken)
    {
        var results = _stateStore
            .LoadAll()
            .Select(record => new PluginModuleStatusDto
            {
                ModuleId = record.ModuleId,
                DisplayName = record.ModuleId,
                InstalledVersions = record.InstalledVersions.OrderBy(version => version).ToList(),
                CurrentEnabledVersion = record.CurrentEnabledVersion,
                Status = string.IsNullOrWhiteSpace(record.CurrentEnabledVersion) ? "Installed" : "Enabled",
                LastOperation = record.Status,
                LastError = null,
                CanEnable = record.InstalledVersions.Count > 0,
                CanDisable = !string.IsNullOrWhiteSpace(record.CurrentEnabledVersion),
                CanRollback = !string.IsNullOrWhiteSpace(record.CurrentEnabledVersion) && record.InstalledVersions.Count > 1
            })
            .OrderBy(item => item.ModuleId)
            .ToList();

        return Task.FromResult<IReadOnlyList<PluginModuleStatusDto>>(results);
    }
}
```

- [ ] **步骤 6：实现只读控制器并接进 HostApi**

```csharp
[ApiController]
[Route("api/plugin-ops")]
[Authorize]
public sealed class PluginOperationsController : ControllerBase
{
    private readonly IPluginOperationsAdminService _service;

    public PluginOperationsController(IPluginOperationsAdminService service)
    {
        _service = service;
    }

    [HttpGet("modules")]
    public async Task<ActionResult<IReadOnlyList<PluginModuleStatusDto>>> GetModules(CancellationToken cancellationToken)
    {
        return Ok(await _service.GetModuleStatusesAsync(cancellationToken));
    }
}
```

```csharp
builder.Services.Configure<PluginOperationsOptions>(builder.Configuration.GetSection("PluginOperations"));
builder.Services.AddSingleton(new InstalledPluginStateStore(pluginInstallRoot));
builder.Services.AddScoped<IPluginOperationsAdminService, PluginOperationsAdminService>();
```

- [ ] **步骤 7：重新运行查询测试**

运行：

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~PluginOperationsAdminServiceTests|FullyQualifiedName~PluginOperationsControllerTests"
```

预期：

- 新增的查询服务与控制器测试通过。

- [ ] **步骤 8：提交**

```bash
git add apps/host-api/Controllers/PluginOperationsController.cs apps/host-api/PluginOperations apps/host-api/Program.cs apps/host-api/HostApi.csproj tests/Kernel.Tests/PluginOperationsAdminServiceTests.cs tests/Kernel.Tests/PluginOperationsControllerTests.cs
git commit -m "feat: add plugin operations query endpoints"
```

## 任务 2：新增上传与校验流程，并由服务端托管上传目录

**文件：**
- 新建：`apps/host-api/PluginOperations/Uploads/UploadedPluginPackageRecord.cs`
- 新建：`apps/host-api/PluginOperations/Uploads/IUploadedPluginPackageStore.cs`
- 新建：`apps/host-api/PluginOperations/Uploads/FileSystemUploadedPluginPackageStore.cs`
- 新建：`apps/host-api/PluginOperations/DTOs/PluginUploadVerifyResponseDto.cs`
- 修改：`apps/host-api/PluginOperations/Services/IPluginOperationsAdminService.cs`
- 修改：`apps/host-api/PluginOperations/Services/PluginOperationsAdminService.cs`
- 修改：`apps/host-api/Controllers/PluginOperationsController.cs`
- 测试：`tests/Kernel.Tests/PluginOperationsAdminServiceTests.cs`
- 测试：`tests/Kernel.Tests/PluginOperationsControllerTests.cs`

- [ ] **步骤 1：先写上传校验红灯测试**

```csharp
[Fact]
public async Task VerifyUploadedPackageAsync_有效包时返回UploadId和解析后的元数据()
{
    var packageRoot = PluginOperationsTestData.CreateValidPackage("Audit", "1.0.0");
    var archivePath = PluginOperationsTestData.CreateZipFromPackage(packageRoot);
    var service = PluginOperationsTestData.CreateAdminServiceWithUploadStore();

    var result = await service.VerifyUploadedPackageAsync(archivePath, "audit.zip", CancellationToken.None);

    Assert.True(result.IsValid);
    Assert.False(string.IsNullOrWhiteSpace(result.UploadId));
    Assert.Equal("Audit", result.ModuleId);
    Assert.Equal("1.0.0", result.Version);
}
```

```csharp
[Fact]
public async Task VerifyUpload_无效包时返回BadRequest()
{
    await using var host = await PluginOperationsTestHost.StartAsync(authenticated: true);
    using var content = PluginOperationsTestData.CreateInvalidUploadContent();

    var response = await host.Client.PostAsync("/api/plugin-ops/uploads/verify", content);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}
```

- [ ] **步骤 2：运行测试，确认它们先失败**

运行：

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~VerifyUploadedPackageAsync|FullyQualifiedName~VerifyUpload_无效包时返回BadRequest"
```

预期：

- 失败，因为上传存储和校验 API 还不存在。

- [ ] **步骤 3：新增上传记录与返回 DTO**

```csharp
public sealed class UploadedPluginPackageRecord
{
    public string UploadId { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ExtractedPath { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public string? ModuleId { get; set; }
    public string? DisplayName { get; set; }
    public string? Version { get; set; }
    public List<string> Errors { get; set; } = new();
}
```

```csharp
public sealed class PluginUploadVerifyResponseDto
{
    public string UploadId { get; set; } = string.Empty;
    public string? ModuleId { get; set; }
    public string? DisplayName { get; set; }
    public string? Version { get; set; }
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}
```

- [ ] **步骤 4：实现文件系统上传存储**

```csharp
public sealed class FileSystemUploadedPluginPackageStore : IUploadedPluginPackageStore
{
    public async Task<UploadedPluginPackageRecord> SaveAndExtractAsync(Stream archiveStream, string fileName, CancellationToken cancellationToken)
    {
        var uploadId = Guid.NewGuid().ToString("N");
        var uploadRoot = Path.Combine(_rootPath, uploadId);
        var packagePath = Path.Combine(uploadRoot, fileName);
        var extractRoot = Path.Combine(uploadRoot, "content");

        Directory.CreateDirectory(uploadRoot);
        await using (var file = File.Create(packagePath))
        {
            await archiveStream.CopyToAsync(file, cancellationToken);
        }

        ZipFile.ExtractToDirectory(packagePath, extractRoot);

        return new UploadedPluginPackageRecord
        {
            UploadId = uploadId,
            OriginalFileName = fileName,
            ExtractedPath = extractRoot
        };
    }
}
```

- [ ] **步骤 5：实现服务层与控制器层的校验流程**

```csharp
public async Task<PluginUploadVerifyResponseDto> VerifyUploadedPackageAsync(Stream archiveStream, string fileName, CancellationToken cancellationToken)
{
    var uploaded = await _uploadStore.SaveAndExtractAsync(archiveStream, fileName, cancellationToken);
    var scanResult = _scanner.ScanPackage(uploaded.ExtractedPath);

    if (!scanResult.IsSuccess)
    {
        return new PluginUploadVerifyResponseDto
        {
            UploadId = uploaded.UploadId,
            IsValid = false,
            Errors = new List<string> { scanResult.ErrorMessage! }
        };
    }

    return new PluginUploadVerifyResponseDto
    {
        UploadId = uploaded.UploadId,
        ModuleId = scanResult.Manifest!.ModuleId,
        DisplayName = scanResult.Manifest.DisplayName,
        Version = scanResult.Manifest.Version,
        IsValid = true
    };
}
```

```csharp
[HttpPost("uploads/verify")]
public async Task<ActionResult<PluginUploadVerifyResponseDto>> VerifyUpload(IFormFile file, CancellationToken cancellationToken)
{
    if (file.Length == 0)
    {
        return BadRequest(new PluginUploadVerifyResponseDto { Errors = new() { "上传文件为空。" } });
    }

    await using var stream = file.OpenReadStream();
    var result = await _service.VerifyUploadedPackageAsync(stream, file.FileName, cancellationToken);
    return result.IsValid ? Ok(result) : BadRequest(result);
}
```

- [ ] **步骤 6：重新运行上传校验测试**

运行：

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~VerifyUploadedPackageAsync|FullyQualifiedName~VerifyUpload_无效包时返回BadRequest"
```

预期：

- 上传校验测试通过。

- [ ] **步骤 7：提交**

```bash
git add apps/host-api/PluginOperations/Uploads apps/host-api/PluginOperations/DTOs/PluginUploadVerifyResponseDto.cs apps/host-api/PluginOperations/Services apps/host-api/Controllers/PluginOperationsController.cs tests/Kernel.Tests/PluginOperationsAdminServiceTests.cs tests/Kernel.Tests/PluginOperationsControllerTests.cs
git commit -m "feat: add upload and verify flow for plugin packages"
```

## 任务 3：新增 install / enable / disable / rollback 管理动作

**文件：**
- 新建：`apps/host-api/PluginOperations/DTOs/PluginLifecycleActionRequestDto.cs`
- 新建：`apps/host-api/PluginOperations/DTOs/PluginActionResultDto.cs`
- 修改：`apps/host-api/PluginOperations/Services/IPluginOperationsAdminService.cs`
- 修改：`apps/host-api/PluginOperations/Services/PluginOperationsAdminService.cs`
- 修改：`apps/host-api/Controllers/PluginOperationsController.cs`
- 测试：`tests/Kernel.Tests/PluginOperationsAdminServiceTests.cs`
- 测试：`tests/Kernel.Tests/PluginOperationsControllerTests.cs`

- [ ] **步骤 1：先写生命周期动作红灯测试**

```csharp
[Fact]
public async Task InstallVerifiedUploadAsync_安装成功但不会自动启用()
{
    var service = PluginOperationsTestData.CreateAdminServiceWithVerifiedUpload("Audit", "1.0.0");

    var result = await service.InstallVerifiedUploadAsync(service.LastUploadId!, CancellationToken.None);

    Assert.Equal("Installed", result.Status);
    var statuses = await service.GetModuleStatusesAsync(CancellationToken.None);
    Assert.Equal("Installed", Assert.Single(statuses).Status);
}

[Fact]
public async Task EnableAsync_复用现有生命周期能力并返回启用版本()
{
    var service = PluginOperationsTestData.CreateAdminServiceWithInstalledPackage("Audit", "1.0.0");

    var result = await service.EnableAsync("Audit", "1.0.0", CancellationToken.None);

    Assert.Equal("Enabled", result.Status);
    Assert.Equal("1.0.0", result.Version);
}
```

- [ ] **步骤 2：运行测试，确认它们先失败**

运行：

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~InstallVerifiedUploadAsync|FullyQualifiedName~EnableAsync_复用现有生命周期能力并返回启用版本"
```

预期：

- 失败，因为生命周期动作方法和 DTO 还不存在。

- [ ] **步骤 3：补上请求与结果 DTO**

```csharp
public sealed class PluginLifecycleActionRequestDto
{
    public string Version { get; set; } = string.Empty;
}

public sealed class PluginActionResultDto
{
    public string ModuleId { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
}
```

- [ ] **步骤 4：在服务层复用现有安装器与生命周期操作**

```csharp
public async Task<PluginActionResultDto> InstallVerifiedUploadAsync(string uploadId, CancellationToken cancellationToken)
{
    var upload = await _uploadStore.GetVerifiedAsync(uploadId, cancellationToken);
    var result = _installer.Install(upload.ExtractedPath, _installRoot);

    return new PluginActionResultDto
    {
        ModuleId = result.ModuleId,
        Version = result.Version,
        Status = result.Status,
        Message = result.ErrorMessage
    };
}
```

```csharp
public Task<PluginActionResultDto> EnableAsync(string moduleId, string version, CancellationToken cancellationToken)
{
    var result = _operations.Enable(moduleId, version);
    _materializer.Materialize(_installRoot);
    return Task.FromResult(new PluginActionResultDto
    {
        ModuleId = result.ModuleId,
        Version = result.Version,
        Status = result.Status,
        Message = result.ErrorMessage
    });
}
```

- [ ] **步骤 5：实现 install / enable / disable / rollback 控制器接口**

```csharp
[HttpPost("uploads/{uploadId}/install")]
public async Task<ActionResult<PluginActionResultDto>> InstallUpload(string uploadId, CancellationToken cancellationToken)
{
    var result = await _service.InstallVerifiedUploadAsync(uploadId, cancellationToken);
    return string.Equals(result.Status, "Installed", StringComparison.OrdinalIgnoreCase) ? Ok(result) : BadRequest(result);
}

[HttpPost("modules/{moduleId}/enable")]
public async Task<ActionResult<PluginActionResultDto>> Enable(string moduleId, [FromBody] PluginLifecycleActionRequestDto request, CancellationToken cancellationToken)
{
    var result = await _service.EnableAsync(moduleId, request.Version, cancellationToken);
    return string.Equals(result.Status, "Enabled", StringComparison.OrdinalIgnoreCase) ? Ok(result) : BadRequest(result);
}
```

- [ ] **步骤 6：重新运行生命周期测试和 HostApi 路由测试**

运行：

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~PluginOperationsAdminServiceTests|FullyQualifiedName~PluginOperationsControllerTests|FullyQualifiedName~HostApiPluginManifestRouteTests"
```

预期：

- 生命周期动作测试通过
- HostApi 插件清单路由测试继续通过

- [ ] **步骤 7：提交**

```bash
git add apps/host-api/PluginOperations/DTOs apps/host-api/PluginOperations/Services apps/host-api/Controllers/PluginOperationsController.cs tests/Kernel.Tests/PluginOperationsAdminServiceTests.cs tests/Kernel.Tests/PluginOperationsControllerTests.cs
git commit -m "feat: add plugin lifecycle admin actions"
```

## 任务 4：实现前端管理员模块并接入新 API

**文件：**
- 新建：`frontend/modules/plugin-ops/package.json`
- 新建：`frontend/modules/plugin-ops/tsconfig.json`
- 新建：`frontend/modules/plugin-ops/src/index.ts`
- 新建：`frontend/modules/plugin-ops/src/types/pluginOperations.ts`
- 新建：`frontend/modules/plugin-ops/src/api/pluginOperationsClient.ts`
- 新建：`frontend/modules/plugin-ops/src/api/pluginOperationsClient.test.ts`
- 新建：`frontend/modules/plugin-ops/src/pages/PluginOperationsCenter.tsx`
- 新建：`frontend/modules/plugin-ops/src/pages/PluginOperationsCenter.test.tsx`

- [ ] **步骤 1：先写 API client 红灯测试**

```ts
import { describe, expect, it, vi } from 'vitest';
import { verifyUpload } from './pluginOperationsClient';

describe('pluginOperationsClient', () => {
  it('会把上传文件 POST 到校验接口', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({ isValid: true }), { status: 200 }));
    const file = new File(['zip'], 'audit.zip');

    await verifyUpload(file, fetcher);

    expect(fetcher).toHaveBeenCalledWith('/api/plugin-ops/uploads/verify', expect.objectContaining({ method: 'POST' }));
  });
});
```

- [ ] **步骤 2：再写页面红灯测试**

```tsx
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import PluginOperationsCenter from './PluginOperationsCenter';

describe('PluginOperationsCenter', () => {
  it('会渲染标题和上传区域', async () => {
    render(<PluginOperationsCenter />);

    expect(await screen.findByText('插件运维中心')).toBeInTheDocument();
    expect(screen.getByText('上传并校验')).toBeInTheDocument();
  });
});
```

- [ ] **步骤 3：运行前端测试，确认它们先失败**

运行：

```powershell
pnpm --dir .\frontend\packages\shell test
```

预期：

- 失败，因为插件运维前端模块和测试文件还不存在。

- [ ] **步骤 4：新增前端类型与 API client**

```ts
export interface PluginModuleStatus {
  moduleId: string;
  displayName: string;
  installedVersions: string[];
  currentEnabledVersion: string | null;
  status: string;
  lastOperation: string | null;
  lastError: string | null;
  canEnable: boolean;
  canDisable: boolean;
  canRollback: boolean;
}
```

```ts
export async function getModules(fetcher: typeof fetch = fetch): Promise<PluginModuleStatus[]> {
  const response = await fetcher('/api/plugin-ops/modules');
  if (!response.ok) throw new Error('加载插件模块状态失败');
  return response.json();
}
```

- [ ] **步骤 5：实现前端管理页和模块入口**

```tsx
const pluginOpsModule: FrontendModule = {
  metadata: {
    moduleId: 'PluginOps',
    displayName: '插件运维中心',
    version: '1.0.0',
    description: '统一插件包上传与运维中心'
  },
  routes: [
    {
      path: '/plugin-ops',
      component: PluginOperationsCenter,
      requireAuth: true,
      permissions: ['PluginOps.Admin']
    }
  ],
  menus: [
    {
      id: 'plugin-ops',
      label: '插件运维',
      icon: 'puzzle',
      order: 30,
      permissions: ['PluginOps.Admin'],
      children: [
        {
          id: 'plugin-ops-center',
          label: '插件运维中心',
          path: '/plugin-ops',
          icon: 'cloud-upload',
          order: 1,
          permissions: ['PluginOps.Admin']
        }
      ]
    }
  ],
  permissions: [
    { code: 'PluginOps.Admin', name: '插件运维管理', category: '插件运维' }
  ]
};
```

- [ ] **步骤 6：重新运行前端测试与工作区构建**

运行：

```powershell
pnpm --dir .\frontend\packages\shell test
pnpm --dir .\frontend build
```

预期：

- 新增前端测试通过
- 前端工作区构建通过

- [ ] **步骤 7：提交**

```bash
git add frontend/modules/plugin-ops
git commit -m "feat: add plugin operations admin module"
```

## 任务 5：更新文档并执行端到端验证

**文件：**
- 修改：`docs/guides/unified-plugin-package-operations.md`
- 修改：`docs/current-status.md`
- 修改：`docs/architecture/frontend-plugin-loading.md`

- [ ] **步骤 1：更新运维文档，补充网页管理流程**

```md
1. 管理员在“插件运维中心”上传 unified plugin package
2. 页面调用 `/api/plugin-ops/uploads/verify`
3. 校验通过后执行 install
4. 管理员继续执行 enable / disable / rollback
5. 页面刷新状态列表与最近结果
```

- [ ] **步骤 2：执行后端验证**

运行：

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~PluginOperationsAdminServiceTests|FullyQualifiedName~PluginOperationsControllerTests|FullyQualifiedName~HostApiPluginManifestRouteTests"
dotnet build .\apps\host-api\HostApi.csproj
```

预期：

- 插件运维后端测试通过
- HostApi 构建通过

- [ ] **步骤 3：执行前端验证**

运行：

```powershell
pnpm --dir .\frontend\packages\shell test
pnpm --dir .\frontend build
```

预期：

- Shell 测试通过
- 前端构建通过

- [ ] **步骤 4：提交**

```bash
git add docs/guides/unified-plugin-package-operations.md docs/current-status.md docs/architecture/frontend-plugin-loading.md
git commit -m "docs: document plugin operations center v1"
```

## 计划自检

### Spec 覆盖检查

- 管理员专用入口：任务 1 和任务 4 覆盖
- 上传与校验：任务 2 覆盖
- install / enable / disable / rollback：任务 3 覆盖
- 状态列表与管理页：任务 1 和任务 4 覆盖
- 安全边界与文档说明：任务 2、任务 3、任务 5 覆盖

### 占位符检查

- 没有使用 `TODO`、`TBD`、“后续补充”或“类似 Task N”这种占位写法。

### 类型一致性检查

- 后端 API DTO 命名统一：
  - `PluginModuleStatusDto`
  - `PluginUploadVerifyResponseDto`
  - `PluginLifecycleActionRequestDto`
  - `PluginActionResultDto`
- 前端统一消费 `PluginModuleStatus` 与上传校验返回结构。
