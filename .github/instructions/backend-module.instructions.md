---
description: "Use when creating or modifying backend modules in server/modules/. Covers module structure, contract implementation, service registration, and boundary rules."
applyTo: "server/modules/**"
---
# 后端模块开发规则

## 模块结构

每个模块必须遵循以下目录结构：

```
server/modules/<ModuleName>/
├── <ModuleName>Module.cs          # 模块入口，继承 ModuleBase
├── Controllers/                    # API 控制器（保持轻薄）
├── Services/                       # 应用服务 / 领域服务
├── DTOs/                           # 对外契约 DTO
├── Models/                         # 领域模型
├── Persistence/
│   ├── Descriptors/                # ModelDefinition 定义
│   ├── Ef/                         # EF Core 仓储实现
│   └── Repositories/               # 仓储接口
└── <ModuleName>Module.csproj
```

## 模块入口

- 继承 `ModuleBase`
- 按需实现 `IPermissionProvider`、`IMenuProvider`、`IConfigProvider`、`IMigrationProvider`
- 在 `ConfigureServices()` 中注册本模块的服务和仓储

## 边界规则

- 不依赖其他模块的具体实现（只能依赖 Kernel 抽象和 SDK）
- 不直接访问其他模块的数据库或仓储
- 不在模块内部判断数据库方言（SqlServer/Oracle/MySQL）
- Controller 只做参数校验和调用 Service，不放业务逻辑
- 对外暴露稳定 DTO，不暴露内部模型

## 权限声明

通过 `IPermissionProvider.GetPermissions()` 声明，不要在 Controller 上硬编码角色名。

## 配置声明

通过 `IConfigProvider.GetConfigSchema()` 声明配置项 schema。

## 参考示例

- 最简示例：`server/modules/Example/Demo/`
- 完整示例：`server/modules/Platform/Authorization/`
- 模板：`server/templates/backend-module-template/`
