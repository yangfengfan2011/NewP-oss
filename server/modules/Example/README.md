# 后端模块模板骨架

该目录是仓库内建后端模块的最小可构建模板，体现当前 Module Capsule 与 behavior binding 契约。

## 模板内容

- `capsule/newp.plugin.json`：模板身份、版本、权限和前后端 behavior key 的唯一 owner；
- `capsule/metadata/**`：最小导航与本地化 authoring；
- `Example.cs`：继承 `ModuleCapsuleBindingBase`，只绑定 backend behavior key 与服务注册；
- `Controllers/TemplateController.cs`：带显式权限要求的轻量接口；
- `DTOs/TemplateResponse.cs`：稳定响应 DTO；
- `Services/*`：模块内部服务。

模板中的 `capsule/` 用于保证模板项目可独立构建。`scripts/new-module.ps1` 生成真实模块时会把它的实际资产合并到 `frontend/modules/<module-slug>/`，并把项目嵌入资源改为该根 Capsule。

## 推荐方式

```powershell
pwsh -NoProfile -File .\scripts\new-module.ps1 -ModuleName ExampleModule -DisplayName "示例模块"
```

## 边界

- `*Module.cs` 不声明 module id、显示名、版本、路由、菜单或权限；
- Controller 只处理协议与授权入口，业务逻辑进入服务；
- 模块不引用 Host 内部实现，也不依赖其他模块私有实现；
- 新模块不创建 Host 私有清单或 Shell 静态注册项。

## 模板验证

```powershell
dotnet build .\server\templates\backend-module-template\Example.csproj
```

生成模块后至少验证模块项目、HostApi 接线和 Canonical Module Plan 编译。
