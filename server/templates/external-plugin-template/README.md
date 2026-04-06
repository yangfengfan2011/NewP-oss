# 外部插件模板

这个模板用于生成“只依赖公开 SDK / Contracts”的最小后端插件骨架。

## 适用场景

- 不修改宿主源码
- 不修改前端工作区
- 面向客户或合作方的受控扩展开发

## 模板包含

- `ExternalPlugin.csproj`
  最小外部插件工程文件。
- `ExternalPluginModule.cs`
  模块入口，只负责插件声明、服务注册和权限声明。
- `Controllers/ExternalPluginStatusController.cs`
  最小状态接口。
- `Services/*`
  最小服务抽象与实现。
- `DTOs/*`
  状态接口返回对象。

## 约束

- 只能依赖 `server/sdk` 暴露的公开能力。
- 不应依赖 `apps/host-api` 或任何内建模块实现。
- 不应修改宿主内部结构完成接入。

## 推荐验证

```powershell
dotnet build .\ExternalPlugin.csproj
.\scripts\pack-plugin.ps1 -ProjectPath .\ExternalPlugin.csproj -Force
```
