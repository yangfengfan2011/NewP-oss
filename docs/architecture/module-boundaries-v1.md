# 模块边界文档 v1

## 文档目的

本文档用于汇总 V1 首批平台模块的边界设计。当前覆盖的模块包括：

- `Identity Lite / LocalAuth`：最小登录能力
- `Authorization`：权限与授权
- `ConfigCenter`：配置中心
- `Audit`：审计

这些模块的共同要求是：

- 属于平台公共能力，不承载具体业务流程
- 通过契约接入 Host / Shell，而不是反向侵入平台内核
- 允许未来扩展，但当前只做最小可用版本

## 模块列表

### 1. Identity Lite / LocalAuth

详细文档：
[identity-lite-module-boundary-v1.md](../modules/identity-lite-module-boundary-v1.md)

核心职责：

- 登录
- 退出登录
- 当前用户信息
- 本地管理员种子能力

当前不做：

- 完整用户中心
- 组织体系迁移
- 外部统一认证的深度接入

### 2. Authorization

详细文档：
[authorization-module-boundary-v1.md](../modules/authorization-module-boundary-v1.md)

核心职责：

- 权限点定义
- 角色与权限关系
- 授权检查
- 前后端权限接入

当前不做：

- 完整组织模型
- 完整身份体系迁移
- 复杂审批和流程授权

### 3. ConfigCenter

详细文档：
[configcenter-module-boundary-v1.md](../modules/configcenter-module-boundary-v1.md)

核心职责：

- 配置项模型
- 配置读写接口
- 字典与枚举的承载入口
- 轻量导入能力

当前不做：

- 超大而全的配置后台
- 复杂审批
- 所有旧元数据的一次性导入

### 4. Audit

详细文档：
[audit-module-boundary-v1.md](../modules/audit-module-boundary-v1.md)

核心职责：

- 操作日志
- 访问日志
- 审计写入接口
- 审计查询入口

当前不做：

- 复杂报表
- 可视化分析平台
- 历史日志全量迁移

## 统一边界规则

- Host 负责接入与组合，不负责承载模块业务规则。
- 模块之间不直接依赖彼此的基础设施实现。
- 公共能力优先走 Kernel 抽象。
- 旧平台代码只能作为参考来源，不做整包迁移。
- 旧资产导入逻辑与模块业务逻辑分开，优先放在迁移层或导入层。

## 推荐实施顺序

1. `Identity Lite / LocalAuth`
2. `Authorization`
3. `ConfigCenter`
4. `Audit`

推荐原因：

- 先有登录，平台才算真正可运行。
- `Authorization` 决定菜单、路由、权限点声明的接入方式。
- `ConfigCenter` 是导入轻量旧资产最自然的落点。
- `Audit` 边界清晰，适合在主干稳定后尽快补上。
