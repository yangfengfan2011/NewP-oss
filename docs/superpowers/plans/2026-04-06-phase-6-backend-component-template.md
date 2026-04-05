# Phase 6 后端组件化模板第一版 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不偏离组件化设计目标的前提下，落地一套“仓库内标准模板 + 活示例模块 + 最小验证链路”的后端组件化模板第一版。

**Architecture:** 本次实现不做脚手架命令，只固化稳定模板来源。通过补强现有模板文档、增加仓库内模板骨架目录、提供一个可被 HostApi 真正加载的后端示例模块，以及对应测试与验证记录，形成后续前端模板和脚手架的上游基础。所有接入点都继续基于 `Kernel` 契约、`Sdk.ModuleBase` 和现有模块装配机制实现，不新增绕过契约的快捷入口。

**Tech Stack:** C#、ASP.NET Core、xUnit、现有 `Kernel` / `Sdk` / `HostApi` 模块体系、Markdown 文档

---

## 文件结构

### 需要修改的现有文件

- `D:\Github\ICP-X\NewP\docs\architecture\module-template.md`
  从当前简要清单升级为正式的后端模块模板落地指南，明确必备文件、按需目录、边界约束、复制方式和验证命令。
- `D:\Github\ICP-X\NewP\apps\host-api\HostApi.csproj`
  引入真实示例模块项目引用，确保示例模块可以被宿主构建产物携带并被模块扫描加载。
- `D:\Github\ICP-X\NewP\docs\current-status.md`
  在第六阶段完成后更新当前状态、交付物和验证结论。

### 需要新增的文件

- `D:\Github\ICP-X\NewP\server\templates\backend-module-template\README.md`
  仓库内模板骨架总说明，告诉复制者哪些文件是必改项、哪些目录按需启用、哪些规则不能改。
- `D:\Github\ICP-X\NewP\server\templates\backend-module-template\TemplateModule.cs`
  模板模块入口骨架，演示 `ModuleBase`、权限声明和配置声明的最小写法。
- `D:\Github\ICP-X\NewP\server\templates\backend-module-template\Controllers\TemplateController.cs`
  模板控制器骨架。
- `D:\Github\ICP-X\NewP\server\templates\backend-module-template\DTOs\TemplateResponse.cs`
  模板 DTO 骨架。
- `D:\Github\ICP-X\NewP\server\templates\backend-module-template\Services\ITemplateService.cs`
  模板服务接口骨架。
- `D:\Github\ICP-X\NewP\server\templates\backend-module-template\Services\TemplateService.cs`
  模板服务实现骨架。
- `D:\Github\ICP-X\NewP\server\modules\ModuleTemplateSample\ModuleTemplateSampleModule.cs`
  真实后端示例模块入口。
- `D:\Github\ICP-X\NewP\server\modules\ModuleTemplateSample\ModuleTemplateSample.csproj`
  真实后端示例模块项目文件。
- `D:\Github\ICP-X\NewP\server\modules\ModuleTemplateSample\Controllers\SampleStatusController.cs`
  真实示例模块最小 API。
- `D:\Github\ICP-X\NewP\server\modules\ModuleTemplateSample\DTOs\SampleStatusResponse.cs`
  真实示例模块响应 DTO。
- `D:\Github\ICP-X\NewP\server\modules\ModuleTemplateSample\Services\ISampleStatusService.cs`
  真实示例模块服务接口。
- `D:\Github\ICP-X\NewP\server\modules\ModuleTemplateSample\Services\SampleStatusService.cs`
  真实示例模块服务实现。
- `D:\Github\ICP-X\NewP\server\modules\ModuleTemplateSample\README.md`
  真实示例模块说明文档。
- `D:\Github\ICP-X\NewP\tests\ModuleTemplateSample.Tests\ModuleTemplateSample.Tests.csproj`
  真实示例模块测试项目。
- `D:\Github\ICP-X\NewP\tests\ModuleTemplateSample.Tests\ModuleTemplateSampleTests.cs`
  真实示例模块测试，至少覆盖模块元数据、权限/配置声明和最小服务链路。
- `D:\Github\ICP-X\NewP\docs\verification\phase-6-backend-component-template-verification.md`
  第六阶段第一版验证记录。
- `D:\Github\ICP-X\NewP\docs\modules\module-template-sample-implementation.md`
  真实示例模块的实现说明，说明其为何作为“活模板”存在，以及与模板骨架如何对应。

### 命名与边界约定

- 真实示例模块统一命名为 `ModuleTemplateSample`，避免和现有 `Demo` 模块语义混淆。
- 真实示例模块必须同时声明至少一组权限和一组配置，用于证明标准接入方式。
- 模板骨架允许使用模板占位名称，但说明文档必须明确哪些占位符必须替换。
- 真实示例模块不承担复杂业务，不接入数据库，不新增跨模块依赖。

---

### Task 1: 落地后端模板规范与仓库内模板骨架

**Files:**
- Modify: `D:\Github\ICP-X\NewP\docs\architecture\module-template.md`
- Create: `D:\Github\ICP-X\NewP\server\templates\backend-module-template\README.md`
- Create: `D:\Github\ICP-X\NewP\server\templates\backend-module-template\TemplateModule.cs`
- Create: `D:\Github\ICP-X\NewP\server\templates\backend-module-template\Controllers\TemplateController.cs`
- Create: `D:\Github\ICP-X\NewP\server\templates\backend-module-template\DTOs\TemplateResponse.cs`
- Create: `D:\Github\ICP-X\NewP\server\templates\backend-module-template\Services\ITemplateService.cs`
- Create: `D:\Github\ICP-X\NewP\server\templates\backend-module-template\Services\TemplateService.cs`

- [ ] **Step 1: 先写模板说明目标结构**

在 `module-template.md` 中明确以下内容：

```markdown
# 后端模块模板

## 目标

- 让新增模块按契约接入
- 让 AI 与人工都能在小上下文中完成模块新增
- 不通过模板引入跨模块隐藏依赖

## 标准结构

- `ModuleNameModule.cs`
- `Controllers/`
- `DTOs/`
- `Services/`
- `Models/`
- `Repositories/`
- `README.md`

## 必选项

- 模块入口
- 至少一个服务接口或服务实现
- README

## 按需启用

- `Models/`
- `Repositories/`
- 迁移/导入钩子

## 禁止事项

- 控制器直接承载复杂业务规则
- 模块直接访问 Host 内部实现
- 隐式启动逻辑执行迁移或导入
```

- [ ] **Step 2: 新建模板骨架 README**

写入：

```markdown
# 后端模块模板骨架

本目录用于提供仓库内可复制的后端模块骨架。

## 必改项

- 模块类名
- 命名空间
- `ModuleId`
- `DisplayName`
- 路由前缀
- 权限编码
- 配置键前缀

## 可按需删除的目录

- `Controllers/`
- `DTOs/`
- `Services/`

## 不要改的原则

- 模块必须继续继承 `ModuleBase`
- 模块公共声明通过 `IPermissionProvider`、`IConfigProvider` 等契约暴露
- 业务逻辑不要直接堆进模块入口
```

- [ ] **Step 3: 创建模板模块入口骨架**

写入 `TemplateModule.cs`：

```csharp
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Templates.BackendModuleTemplate;

public class TemplateModule : ModuleBase, IPermissionProvider, IConfigProvider
{
    public override string ModuleId => "TemplateModule";
    public override string DisplayName => "模板模块";
    public override string Description => "后端模块模板骨架";

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ITemplateService, TemplateService>();
    }

    public IReadOnlyList<PermissionDescriptor> GetPermissions()
    {
        return new[]
        {
            new PermissionDescriptor
            {
                Code = "TemplateModule.View",
                DisplayName = "查看模板模块",
                ModuleId = ModuleId,
                Group = "模板模块"
            }
        };
    }

    public IReadOnlyList<ConfigDescriptor> GetConfigSchema()
    {
        return new[]
        {
            new ConfigDescriptor
            {
                Key = "TemplateModule.Enabled",
                DisplayName = "启用模板模块",
                Type = "Bool",
                DefaultValue = "true",
                ModuleId = ModuleId,
                Group = "基础配置"
            }
        };
    }
}
```

- [ ] **Step 4: 创建模板控制器、DTO 和服务骨架**

写入 `Controllers\TemplateController.cs`：

```csharp
using ICP.Platform.Templates.BackendModuleTemplate.DTOs;
using ICP.Platform.Templates.BackendModuleTemplate.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Templates.BackendModuleTemplate.Controllers;

[ApiController]
[Route("api/template-module")]
public class TemplateController : ControllerBase
{
    private readonly ITemplateService _service;

    public TemplateController(ITemplateService service)
    {
        _service = service;
    }

    [HttpGet("status")]
    public ActionResult<TemplateResponse> GetStatus()
    {
        return Ok(new TemplateResponse
        {
            ModuleId = "TemplateModule",
            Message = _service.GetStatusMessage()
        });
    }
}
```

写入 `DTOs\TemplateResponse.cs`：

```csharp
namespace ICP.Platform.Templates.BackendModuleTemplate.DTOs;

public class TemplateResponse
{
    public required string ModuleId { get; init; }
    public required string Message { get; init; }
}
```

写入 `Services\ITemplateService.cs`：

```csharp
namespace ICP.Platform.Templates.BackendModuleTemplate.Services;

public interface ITemplateService
{
    string GetStatusMessage();
}
```

写入 `Services\TemplateService.cs`：

```csharp
namespace ICP.Platform.Templates.BackendModuleTemplate.Services;

public class TemplateService : ITemplateService
{
    public string GetStatusMessage() => "Template module is ready.";
}
```

- [ ] **Step 5: 检查文档和模板骨架文件已创建**

Run:

```powershell
Get-ChildItem .\docs\architecture\module-template.md
Get-ChildItem .\server\templates\backend-module-template -Recurse
```

Expected:

- `module-template.md` 已更新
- 模板骨架目录及 6 个模板文件都存在

- [ ] **Step 6: 提交模板规范与骨架**

```powershell
git add .\docs\architecture\module-template.md .\server\templates\backend-module-template
git commit -m "docs: add backend module template skeleton"
```

---

### Task 2: 落地真实后端示例模块

**Files:**
- Create: `D:\Github\ICP-X\NewP\server\modules\ModuleTemplateSample\ModuleTemplateSample.csproj`
- Create: `D:\Github\ICP-X\NewP\server\modules\ModuleTemplateSample\ModuleTemplateSampleModule.cs`
- Create: `D:\Github\ICP-X\NewP\server\modules\ModuleTemplateSample\Controllers\SampleStatusController.cs`
- Create: `D:\Github\ICP-X\NewP\server\modules\ModuleTemplateSample\DTOs\SampleStatusResponse.cs`
- Create: `D:\Github\ICP-X\NewP\server\modules\ModuleTemplateSample\Services\ISampleStatusService.cs`
- Create: `D:\Github\ICP-X\NewP\server\modules\ModuleTemplateSample\Services\SampleStatusService.cs`
- Create: `D:\Github\ICP-X\NewP\server\modules\ModuleTemplateSample\README.md`

- [ ] **Step 1: 新建示例模块项目文件**

写入 `ModuleTemplateSample.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>ICP.Platform.Modules.ModuleTemplateSample</RootNamespace>
    <AssemblyName>ModuleTemplateSampleModule</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\server\kernel\Kernel.csproj" />
    <ProjectReference Include="..\..\..\server\sdk\Sdk.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: 先实现示例模块入口**

写入 `ModuleTemplateSampleModule.cs`：

```csharp
using ICP.Platform.Kernel.Abstractions;
using ICP.Platform.Modules.ModuleTemplateSample.Services;
using ICP.Platform.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.ModuleTemplateSample;

public class ModuleTemplateSampleModule : ModuleBase, IPermissionProvider, IConfigProvider
{
    public override string ModuleId => "ModuleTemplateSample";
    public override string DisplayName => "模块模板示例";
    public override string Description => "用于验证后端组件化模板的真实示例模块";

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ISampleStatusService, SampleStatusService>();
    }

    public IReadOnlyList<PermissionDescriptor> GetPermissions()
    {
        return new[]
        {
            new PermissionDescriptor
            {
                Code = "ModuleTemplateSample.View",
                DisplayName = "查看模块模板示例",
                ModuleId = ModuleId,
                Group = "模块模板示例"
            }
        };
    }

    public IReadOnlyList<ConfigDescriptor> GetConfigSchema()
    {
        return new[]
        {
            new ConfigDescriptor
            {
                Key = "ModuleTemplateSample.Enabled",
                DisplayName = "启用模块模板示例",
                Type = "Bool",
                DefaultValue = "true",
                ModuleId = ModuleId,
                Group = "基础配置"
            }
        };
    }
}
```

- [ ] **Step 3: 实现示例模块服务与 API**

写入 `Services\ISampleStatusService.cs`：

```csharp
namespace ICP.Platform.Modules.ModuleTemplateSample.Services;

public interface ISampleStatusService
{
    string GetStatusMessage();
}
```

写入 `Services\SampleStatusService.cs`：

```csharp
namespace ICP.Platform.Modules.ModuleTemplateSample.Services;

public class SampleStatusService : ISampleStatusService
{
    public string GetStatusMessage() => "ModuleTemplateSample is ready.";
}
```

写入 `DTOs\SampleStatusResponse.cs`：

```csharp
namespace ICP.Platform.Modules.ModuleTemplateSample.DTOs;

public class SampleStatusResponse
{
    public required string ModuleId { get; init; }
    public required string DisplayName { get; init; }
    public required string Message { get; init; }
}
```

写入 `Controllers\SampleStatusController.cs`：

```csharp
using ICP.Platform.Modules.ModuleTemplateSample.DTOs;
using ICP.Platform.Modules.ModuleTemplateSample.Services;
using Microsoft.AspNetCore.Mvc;

namespace ICP.Platform.Modules.ModuleTemplateSample.Controllers;

[ApiController]
[Route("api/module-template-sample")]
public class SampleStatusController : ControllerBase
{
    private readonly ISampleStatusService _service;

    public SampleStatusController(ISampleStatusService service)
    {
        _service = service;
    }

    [HttpGet("status")]
    public ActionResult<SampleStatusResponse> GetStatus()
    {
        return Ok(new SampleStatusResponse
        {
            ModuleId = "ModuleTemplateSample",
            DisplayName = "模块模板示例",
            Message = _service.GetStatusMessage()
        });
    }
}
```

- [ ] **Step 4: 写示例模块 README**

写入：

```markdown
# ModuleTemplateSample

`ModuleTemplateSample` 是后端组件化模板第一版的真实示例模块。

## 作用

- 演示标准模块入口
- 演示服务注册
- 演示最小 API
- 演示权限声明
- 演示配置声明

## 不包含

- 复杂业务逻辑
- 数据库访问
- 跨模块调用
```

- [ ] **Step 5: 构建示例模块**

Run:

```powershell
dotnet build .\server\modules\ModuleTemplateSample\ModuleTemplateSample.csproj
```

Expected:

- `BUILD SUCCEEDED`

- [ ] **Step 6: 提交示例模块**

```powershell
git add .\server\modules\ModuleTemplateSample
git commit -m "feat: add backend module template sample module"
```

---

### Task 3: 接入宿主并补齐示例模块测试

**Files:**
- Modify: `D:\Github\ICP-X\NewP\apps\host-api\HostApi.csproj`
- Create: `D:\Github\ICP-X\NewP\tests\ModuleTemplateSample.Tests\ModuleTemplateSample.Tests.csproj`
- Create: `D:\Github\ICP-X\NewP\tests\ModuleTemplateSample.Tests\ModuleTemplateSampleTests.cs`

- [ ] **Step 1: 让 HostApi 引用示例模块**

在 `HostApi.csproj` 的现有项目引用中加入：

```xml
<ProjectReference Include="..\..\server\modules\ModuleTemplateSample\ModuleTemplateSample.csproj" />
```

- [ ] **Step 2: 新建测试项目文件**

写入 `ModuleTemplateSample.Tests.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\server\kernel\Kernel.csproj" />
    <ProjectReference Include="..\..\server\sdk\Sdk.csproj" />
    <ProjectReference Include="..\..\server\modules\ModuleTemplateSample\ModuleTemplateSample.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: 先写示例模块测试**

写入 `ModuleTemplateSampleTests.cs`：

```csharp
using ICP.Platform.Kernel.ModuleLoader;
using ICP.Platform.Modules.ModuleTemplateSample;
using ICP.Platform.Modules.ModuleTemplateSample.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Modules.ModuleTemplateSample.Tests;

public class ModuleTemplateSampleTests
{
    [Fact]
    public void Module_HasExpectedMetadata()
    {
        var module = new ModuleTemplateSampleModule();

        Assert.Equal("ModuleTemplateSample", module.ModuleId);
        Assert.Equal("模块模板示例", module.DisplayName);
    }

    [Fact]
    public void Module_RegistersPermissionAndConfig()
    {
        var registry = new ModuleRegistry();
        registry.RegisterModule(new ModuleTemplateSampleModule());

        var permissions = registry.GetAllPermissions();
        var configs = registry.GetAllConfigs();

        Assert.Contains(permissions, p => p.Code == "ModuleTemplateSample.View");
        Assert.Contains(configs, c => c.Key == "ModuleTemplateSample.Enabled");
    }

    [Fact]
    public void Module_RegistersService()
    {
        var services = new ServiceCollection();
        var module = new ModuleTemplateSampleModule();

        module.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ISampleStatusService>();

        Assert.Equal("ModuleTemplateSample is ready.", service.GetStatusMessage());
    }
}
```

- [ ] **Step 4: 运行示例模块测试**

Run:

```powershell
dotnet test .\tests\ModuleTemplateSample.Tests\ModuleTemplateSample.Tests.csproj
```

Expected:

- `Passed!`
- 3 个测试通过

- [ ] **Step 5: 构建 HostApi**

Run:

```powershell
dotnet build .\apps\host-api\HostApi.csproj
```

Expected:

- `BUILD SUCCEEDED`
- 宿主可成功携带 `ModuleTemplateSampleModule.dll`

- [ ] **Step 6: 提交宿主接入与测试**

```powershell
git add .\apps\host-api\HostApi.csproj .\tests\ModuleTemplateSample.Tests
git commit -m "test: verify backend module template sample integration"
```

---

### Task 4: 完成验证记录、实现说明和状态收口

**Files:**
- Create: `D:\Github\ICP-X\NewP\docs\verification\phase-6-backend-component-template-verification.md`
- Create: `D:\Github\ICP-X\NewP\docs\modules\module-template-sample-implementation.md`
- Modify: `D:\Github\ICP-X\NewP\docs\current-status.md`

- [ ] **Step 1: 写示例模块实现说明**

写入 `module-template-sample-implementation.md`：

```markdown
# ModuleTemplateSample 实现说明

## 目的

`ModuleTemplateSample` 用于作为后端组件化模板第一版的活模板。

## 对应关系

- `ModuleTemplateSampleModule.cs` 对应模板中的模块入口
- `Controllers/` 对应模板中的最小 API
- `Services/` 对应模板中的最小服务层
- `DTOs/` 对应模板中的响应模型

## 为什么不直接复用 Demo

- `Demo` 已承担平台演示职责
- `ModuleTemplateSample` 更适合作为“可复制模板”保持极简
```

- [ ] **Step 2: 写验证记录**

写入 `phase-6-backend-component-template-verification.md`：

```markdown
# Phase 6 后端组件化模板第一版验证记录

## 验证命令

- `dotnet build .\server\modules\ModuleTemplateSample\ModuleTemplateSample.csproj`
- `dotnet test .\tests\ModuleTemplateSample.Tests\ModuleTemplateSample.Tests.csproj`
- `dotnet build .\apps\host-api\HostApi.csproj`

## 预期

- 模板骨架目录存在
- 示例模块可独立构建
- 示例模块测试通过
- HostApi 可成功构建并包含示例模块

## 结果摘要

- 后端模板文档已升级
- 仓库内模板骨架已落地
- 真实示例模块已落地
- 宿主已完成示例模块引用
```

- [ ] **Step 3: 更新当前状态文档**

在 `current-status.md` 的 `Phase 6` 段补充：

```markdown
### Phase 6：开发体验与模板化

状态：部分完成
已落地：

- 后端组件化模板设计
- 仓库内后端模板骨架
- `ModuleTemplateSample` 活示例模块
- 对应测试与验证记录

下一步：

- 前端组件化模板
- 端到端示例模块
- 可执行脚手架
```

- [ ] **Step 4: 运行最终核验命令**

Run:

```powershell
dotnet test .\tests\ModuleTemplateSample.Tests\ModuleTemplateSample.Tests.csproj
dotnet build .\apps\host-api\HostApi.csproj
```

Expected:

- 测试通过
- 宿主构建通过

- [ ] **Step 5: 提交文档和状态收口**

```powershell
git add .\docs\verification\phase-6-backend-component-template-verification.md .\docs\modules\module-template-sample-implementation.md .\docs\current-status.md
git commit -m "docs: record phase 6 backend module template closeout"
```

---

## 自检

- 规格覆盖：本计划覆盖了模板规范、模板骨架、真实示例模块、宿主接入、模块测试、验证记录和状态收口。
- 占位项扫描：文中没有 `TODO`、`TBD` 或“后续补充细节”类空泛占位。
- 类型一致性：真实示例模块统一使用 `ModuleTemplateSample` 命名，服务接口统一使用 `ISampleStatusService`，API 响应统一使用 `SampleStatusResponse`。

## 执行交接

计划已保存到 `docs/superpowers/plans/2026-04-06-phase-6-backend-component-template.md`。

后续执行方式已确定：**继续使用 Codex 统筹 + Claude 实施**。接下来按子任务边界逐个派发给 Claude，Codex 负责审查、回归验证和文档收口。
