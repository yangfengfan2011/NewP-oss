# 后端模块模板落地指南

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
- 保留 `TODO` 注释或空泛占位代码

## 模板目录位置

- `server/templates/backend-module-template/`

## 复制方式

1. 复制整个模板目录到目标模块目录。
2. 重命名 `TemplateModule.cs` 为目标模块入口文件。
3. 替换命名空间、`ModuleId`、`DisplayName`、路由前缀、权限编码和配置键前缀。
4. 根据业务需要保留或删除 `Controllers/`、`DTOs/`、`Services/` 等目录。
5. 按模块实际职责补齐实现，不要把所有逻辑堆进模块入口。

## 验证命令

```powershell
Get-ChildItem -Recurse server/modules/YourModule

dotnet build server/modules/YourModule/YourModule.csproj
```
