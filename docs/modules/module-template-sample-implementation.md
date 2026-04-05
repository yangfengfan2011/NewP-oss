# ModuleTemplateSample 实现概览

## 概述

`ModuleTemplateSample` 是后端组件化模板第一版的真实示例模块，用于验证和演示标准模块的实现模式。

## 模块结构

```
server/modules/ModuleTemplateSample/
├── ModuleTemplateSampleModule.cs  # 模块入口类
├── Controllers/
│   └── SampleStatusController.cs  # API 控制器
├── Services/
│   ├── ISampleStatusService.cs    # 服务接口
│   └── SampleStatusService.cs     # 服务实现
└── DTOs/
    └── SampleStatusResponse.cs    # 数据传输对象
```

## 核心实现

### 模块入口

`ModuleTemplateSampleModule` 实现了：
- `ModuleBase` 基类
- `IPermissionProvider` 接口（声明权限点）
- `IConfigProvider` 接口（声明配置项）

### 服务注册

在 `ConfigureServices` 中注册：
- `ISampleStatusService` → `SampleStatusService`

### API 端点

- `GET /api/module-template-sample/status` - 获取模块状态

### 权限声明

- `ModuleTemplateSample.View` - 查看模块模板示例

### 配置声明

- `ModuleTemplateSample.Enabled` - 启用模块模板示例（默认 true）

## 设计特点

### 最小化实现

- 不包含复杂业务逻辑
- 不包含数据库访问
- 不包含跨模块调用
- 专注于演示标准模块结构

### 契约遵循

- 通过 `IPermissionProvider` 声明权限
- 通过 `IConfigProvider` 声明配置
- 通过依赖注入注册服务
- 遵循标准 API 路由规范

## 与 Demo 模块的区别

| 特性 | Demo | ModuleTemplateSample |
|------|------|---------------------|
| 目的 | 验证模块系统可用性 | 演示标准模块模板 |
| 复杂度 | 最简 | 标准结构 |
| 权限声明 | 无 | 有 |
| 配置声明 | 无 | 有 |
| 服务层 | 无 | 有 |
| 适用场景 | 快速验证 | 新模块开发参考 |

## 使用场景

1. **新模块开发参考** - 作为创建新模块的模板
2. **组件化验证** - 验证模块系统的完整性
3. **文档示例** - 为开发文档提供真实示例
4. **培训材料** - 帮助开发者理解模块开发规范

## 验证要点

- 模块可被 Host 正确加载
- 权限点可被 Authorization 模块识别
- 配置项可被 ConfigCenter 模块识别
- API 端点可正常访问
- 服务依赖注入正常工作

## 后续扩展方向

当前版本是最小可用版本，后续可扩展：
- 添加单元测试示例
- 添加集成测试示例
- 添加前端模块对接示例
- 添加数据访问层示例（可选）
