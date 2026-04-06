# ModuleTemplateSample

`ModuleTemplateSample` 是后端组件化模板第一版的真实示例模块。

## 作用

- 演示标准模块入口
- 演示服务注册
- 演示最小 API
- 演示权限声明
- 演示配置声明

## 主要文件

- `ModuleTemplateSampleModule.cs`
  模块入口，负责模块信息、服务注册、权限声明和配置声明。
- `Controllers/SampleStatusController.cs`
  最小 API 入口，对外暴露 `GET /api/module-template-sample/status`。
- `Services/ISampleStatusService.cs`
  服务抽象。
- `Services/SampleStatusService.cs`
  服务实现。
- `DTOs/SampleStatusResponse.cs`
  API 返回对象。

## 不包含

- 复杂业务逻辑
- 数据库访问
- 跨模块调用

## 配套前端模块

这个后端示例对应的前端示例在：

- `frontend/modules/module-template-sample`

它们一起组成当前仓库里最小的“前后端组件化 Example”。

## 推荐阅读

- 统一入口文档：
  [docs/guides/componentized-development.md](../../../docs/guides/componentized-development.md)
- 详细 Example 走读：
  [docs/guides/componentized-example-module.md](../../../docs/guides/componentized-example-module.md)
