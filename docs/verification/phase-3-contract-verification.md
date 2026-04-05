# Phase 3 契约验证说明

## 1. 目的

本文档用于说明 Phase 3 的核心验证范围：

- 模块扩展契约是否已落地
- Host 是否能聚合后端模块声明
- Shell 是否能聚合前端模块声明
- Demo 模块是否已作为真实样例跑通接入链路

本文档不替代模块级测试，也不替代后续更细的集成测试；它只负责证明 Phase 3 的阶段目标已经达成。

## 2. 验证范围

本次验证覆盖以下对象：

- 后端模块装载主干
- 前端模块注册主干
- 菜单、权限、配置等扩展信息聚合
- Demo 模块的前后端接入样例

## 3. 验证依据

### 文档依据

- [../architecture/module-contract.md](../architecture/module-contract.md)
- [../architecture/module-boundaries-v1.md](../architecture/module-boundaries-v1.md)
- [demo-module-verification.md](demo-module-verification.md)

### 代码依据

- `server/kernel/Extensions/ServiceCollectionExtensions.cs`
- `apps/host-api/Program.cs`
- `apps/host-api/Controllers/ModulesController.cs`
- `server/modules/Demo/DemoModule.cs`
- `frontend/packages/shell/src/registerModules.ts`
- `frontend/packages/shell/src/main.tsx`
- `frontend/packages/shell/src/App.tsx`
- `frontend/packages/shell/src/components/Layout.tsx`
- `frontend/modules/demo/src/index.ts`

## 4. 验证项与结果

### 验证项 1：后端模块发现与注册

检查点：

- Host 调用 `AddModuleSystem(...)`
- 模块程序集被扫描
- 发现的模块会被注册到 `ModuleRegistry`
- 模块自身 `ConfigureServices(...)` 会被执行

结果：通过

说明：

- `Program.cs` 已调用 `AddModuleSystem(...)`
- `ServiceCollectionExtensions.cs` 已实现模块发现、排序、注册与服务装配主干

### 验证项 2：后端扩展信息聚合

检查点：

- 权限可聚合
- 菜单可聚合
- 配置可聚合
- 模块基础信息可通过统一入口暴露

结果：通过

说明：

- `ModulesController.cs` 已暴露模块、权限、菜单、配置和迁移入口
- `Program.cs` 启动后会输出已加载模块、权限和菜单的统计日志

### 验证项 3：Demo 后端模块契约样例

检查点：

- Demo 模块实现模块元数据
- Demo 模块声明权限
- Demo 模块声明菜单
- Demo 模块声明配置 schema
- Demo 模块注册服务

结果：通过

说明：

- `DemoModule.cs` 已实现 `IPermissionProvider`、`IMenuProvider`、`IConfigProvider`
- `DemoModule.cs` 已注册 `IDemoService`

### 验证项 4：前端模块注册与初始化

检查点：

- Shell 能统一注册模块
- 模块能被统一初始化
- 路由能从模块注册结果中聚合
- 菜单能从模块注册结果中聚合

结果：通过

说明：

- `registerModules.ts` 已注册 Demo 与其他业务模块
- `main.tsx` 已调用模块初始化
- `App.tsx` 已基于 `ModuleRegistry.getRoutes()` 动态生成路由
- `Layout.tsx` 已基于 `ModuleRegistry.getMenus()` 渲染菜单

### 验证项 5：Demo 前端模块契约样例

检查点：

- Demo 模块提供前端元数据
- Demo 模块声明路由
- Demo 模块声明菜单
- Demo 模块声明权限

结果：通过

说明：

- `frontend/modules/demo/src/index.ts` 已提供上述扩展声明

## 5. 构建验证

本次阶段收口建议至少执行：

### 后端

```bash
dotnet build ICP-Platform.sln
```

### 前端

```bash
cd frontend
pnpm build
```

如果构建通过，则可以进一步支撑“契约主干已成立”的判断。

## 6. 验证结论

结论：**Phase 3 的扩展契约目标已达成。**

原因：

- 后端已具备模块发现、注册、聚合主干
- 前端已具备模块注册、初始化、路由与菜单聚合主干
- Demo 模块已经成为贯通样例
- 现有缺口主要在阶段文档收口，而非扩展机制本身缺失

## 7. 保留风险

- 当前验证仍以样例模块和主干代码审查为主，后续可以继续补更多自动化契约测试。
- 前端模块注册仍以显式导入为主，后续规模扩大后需关注维护成本。
- 本文档证明的是“扩展机制成立”，不是“所有模块都已完成高质量集成验证”。
