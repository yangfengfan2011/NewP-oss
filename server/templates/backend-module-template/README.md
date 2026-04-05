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
