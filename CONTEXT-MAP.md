# Context Map

## Contexts

- [Organization](./docs/contexts/organization/CONTEXT.md) — 管理公司、部门、岗位、人员档案及人员与用户账号的受控关联
- [Plugin Platform](./docs/contexts/plugin-platform/CONTEXT.md) — 管理外部插件包的校验、期望状态、启动期激活与统一运行时投影
- [Authorization](./docs/contexts/authorization/CONTEXT.md) — 把已认证主体与活动权限目录解析为服务端 endpoint 的允许、拒绝或不可判定结果
- [Platform Persistence](./docs/contexts/platform-persistence/CONTEXT.md) — 把候选模块计划编译为共享运行时模型与可验证的 EF migration streams，并保障 SQL Server 上的确定执行
- [Platform Acceptance](./docs/contexts/platform-acceptance/CONTEXT.md) — 编排正式观察面上的用户旅程与故障场景，并从完整证据集中产生两道闸门的唯一裁决

## Relationships

- **Organization → Identity / LocalAuth**：Organization 只引用用户账号标识，不拥有账号、凭据或登录生命周期。
- **Organization → Authorization**：Organization 声明业务权限和 endpoint access rule，并只通过稳定契约读取授权摘要；它不拥有角色、权限分配或授权存储实现。
- **Plugin Platform → Host / Shell**：Plugin Platform 产生活动插件快照；Host 和 Shell 只消费同一快照，不分别决定插件版本或启用状态。
- **Plugin Platform → Persistence**：Plugin Platform 编排插件拥有的模型与向前迁移；平台持久化负责 provider adapter 和执行保障，不拥有插件业务模型。
- **Platform Persistence → Plugin Platform**：Platform Persistence 只准备并执行某个候选快照的持久化计划；迁移成功不等于插件激活，活动状态仍由 Plugin Platform 原子提交。
- **Platform Persistence → Modules**：模块拥有自己的模型和 migration stream；Platform Persistence 校验归属、顺序与安全性，并把所有活动模型投影进共享运行时 DbContext。
- **Platform Persistence → Host**：Host 只提交同一份已编译计划用于 DI 注册和启动期执行，不枚举具体模块 provider，也不决定模块迁移顺序。
- **Authorization → Identity / LocalAuth**：Authorization 消费可信主体标识，不拥有凭据交换、Cookie 或账号生命周期。
- **Authorization → Plugin Platform**：Authorization 只接受活动插件快照中的权限和 access rule；未激活插件的声明不能进入有效权限目录。
- **Authorization → Shell**：Shell 可用当前主体的有效权限改善菜单和按钮可见性，但服务端判定始终是安全事实源。
- **Platform Acceptance → Host / Shell**：Platform Acceptance 只通过受支持启动命令、就绪接口、Shell origin 与浏览器可见行为观察系统，不直接组合或启动内部组件。
- **Platform Acceptance → Organization**：验收 Fixture 只通过受限的应用接口准备公司、部门与 actors；人员中心结果仍必须通过正式 API 与浏览器旅程证明。
- **Platform Acceptance → Plugin Platform**：Platform Acceptance 观察安装、期望状态、活动快照与激活结果，但不直接修改状态存储或运行时投影。
- **Platform Acceptance → Platform Persistence**：Platform Acceptance 管理带归属标记的 disposable catalog，并只读观察迁移事实；业务持久性必须通过 API 与浏览器证明。
