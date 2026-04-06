# 后端模块模板骨架

这个目录用于提供仓库内可复制的后端模块最小骨架。

## 模板包含

- `TemplateModule.csproj`
  最小后端模块项目文件，可直接作为脚手架输入。
- `TemplateModule.cs`
  模块入口，负责服务注册、权限声明和配置声明。
- `Controllers/TemplateController.cs`
  默认提供 `GET /api/template-module/status` 状态接口。
- `DTOs/TemplateResponse.cs`
  状态接口返回对象。
- `Services/ITemplateService.cs`
  状态服务抽象。
- `Services/TemplateService.cs`
  最小状态服务实现。

## 必改项

- 模块类名
- 命名空间
- `ModuleId`
- `DisplayName`
- 路由前缀
- 权限编码
- 配置键前缀

## 推荐方式

优先使用脚手架生成：

```powershell
.\scripts\new-module.ps1 -ModuleName ExampleModule
```

```bash
./scripts/new-module.sh --module-name ExampleModule
```

脚手架会基于当前模板生成完整后端目录，并按需更新 `apps/host-api/HostApi.csproj`。

## 原则

- 模块必须继续继承 `ModuleBase`。
- 模块公共声明通过 `IPermissionProvider`、`IConfigProvider` 等契约暴露。
- 业务逻辑不要直接堆进模块入口。
## Componentization Guardrails

- `*Module.cs` should only handle module declaration, service registration, and shared declarations.
- `Controllers/*` should stay as thin entry points.
- `Services/*` should hold module-private logic.
- Shared declarations that multiple modules rely on should move into `Kernel / SDK / Contracts`, not be copied between modules.
- Normal module work should not require changing unrelated Host internals.

## Minimum Verification

```powershell
dotnet build .\server\modules\ExampleModule\ExampleModule.csproj
dotnet build .\apps\host-api\HostApi.csproj
```
