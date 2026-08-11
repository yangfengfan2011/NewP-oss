# Audit 模块

Audit 提供操作日志与访问日志服务、仓储、菜单和查看权限声明。

当前模块入口是 `AuditModule.cs`；服务和仓储分别位于 `Services/` 与 `Repositories/`。对能力状态的判断以模块源码和 `tests/Audit.Tests` 为准，不使用旧阶段实现文档。

Audit 仍通过兼容 `ModuleBase` 接入。新增模块不得复制该身份、菜单和权限的代码声明方式，应使用 Module Capsule 与 behavior binding 模板。
