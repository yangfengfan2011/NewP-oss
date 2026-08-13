# Context Map

## Contexts

- [Identity](./docs/contexts/identity/CONTEXT.md) — 管理用户账号、账号状态与可供认证机制使用的账号目录
- [Authentication](./docs/contexts/authentication/CONTEXT.md) — 管理认证会话、服务端有效性与统一撤销
- [Organization](./docs/contexts/organization/CONTEXT.md) — 管理公司、部门、岗位、人员档案及人员与用户账号的永久绑定
- [Plugin Platform](./docs/contexts/plugin-platform/CONTEXT.md) — 管理外部插件包的校验、期望状态、启动期激活与统一运行时投影
- [Authorization](./docs/contexts/authorization/CONTEXT.md) — 把已认证主体与活动权限目录解析为服务端 endpoint 的允许、拒绝或不可判定结果
- [Platform Persistence](./docs/contexts/platform-persistence/CONTEXT.md) — 把候选模块计划编译为共享运行时模型与可验证的 EF migration streams，并保障 SQL Server 上的确定执行
- [Platform Acceptance](./docs/contexts/platform-acceptance/CONTEXT.md) — 编排正式观察面上的用户旅程与故障场景，并从完整证据集中产生两道闸门的唯一裁决

## Relationships

- **Identity → LocalAuth**：Identity 拥有用户账号与账号状态；LocalAuth 只管理和验证与账号一对一关联的本地凭据。
- **Identity → Authentication**：Identity 与 Authentication 都是 Host readiness 的必需内建能力。账号停用、删除或凭据重置会撤销该账号的全部认证会话；账号恢复不会恢复旧会话。
- **Authentication → Host**：Authentication 拥有服务端认证会话及其有效性；Host 只负责 Cookie 接入和逐请求验证，不拥有会话状态。首阶段只支持单个活动 Host，Host 使用稳定应用名和受保护的持久化 Data Protection 密钥环保证正常重启后仍能解析既有 Cookie，多实例共享认证不在本阶段范围内。
- **Organization → Identity / Authentication**：Organization 通过账号目录查询用户账号，并由 Person 保存不可变的 IdentityAccount EID 来拥有永久一对一绑定；Identity 不保存 Person EID，Person.Name 与 IdentityAccount.ENAME 分别维护且不自动同步。账号目录暂时不可用时，已绑定人员仍可降级读取 Organization 自有绑定事实，但新绑定安全关闭。Organization 应用层通过 Identity.Contracts、Authentication.Contracts 与平台事务执行器编排人员停用、关联账号停用、人员停用阻断和全部会话撤销的单事务提交；Host 不承载该业务流程。人员恢复只解除阻断条件，不会自动恢复账号或旧会话。
- **Organization → Authorization**：Organization 声明业务权限和 endpoint access rule，并只通过稳定契约读取授权摘要；它不拥有角色、权限分配或授权存储实现。
- **Plugin Platform → Host / Shell**：Plugin Platform 产生活动插件快照；Host 和 Shell 只消费同一快照，不分别决定插件版本或启用状态。
- **Plugin Platform → Persistence**：Plugin Platform 把插件拥有的模型与 migration stream 纳入候选计划；它不直接执行迁移。Platform Persistence 负责 provider adapter、统一 history 和执行保障，不拥有插件业务模型。
- **Platform Persistence → Plugin Platform**：Platform Persistence 只准备并执行某个候选快照的持久化计划；迁移成功不等于插件激活，活动状态仍由 Plugin Platform 原子提交。
- **Platform Persistence → Modules**：模块拥有自己的模型和 migration stream；Platform Persistence 校验归属、线性迁移链、摘要与目标 frontier，并把所有活动模型投影进共享运行时 DbContext。
- **Platform Persistence → Host**：Host 是数据库 migration 的唯一受支持执行入口，使用同一份已编译计划完成 DI 注册、完整 EF Up、统一 history 维护和只读一致性检查；它不枚举具体模块 provider，也不决定模块迁移顺序。
- **Authorization → Identity / LocalAuth**：Authorization 消费可信主体标识，不拥有凭据交换、Cookie 或账号生命周期。
- **Authorization → Plugin Platform**：Authorization 只接受活动插件快照中的权限和 access rule；未激活插件的声明不能进入有效权限目录。
- **Authorization → Shell**：Shell 可用当前主体的有效权限改善菜单和按钮可见性，但服务端判定始终是安全事实源。
- **Platform Acceptance → Host / Shell**：Platform Acceptance 只通过受支持启动命令、就绪接口、Shell origin 与浏览器可见行为观察系统，不直接组合或启动内部组件。
- **Platform Acceptance → Organization**：验收 Fixture 只通过受限的应用接口准备公司、部门与 actors；人员中心结果仍必须通过正式 API 与浏览器旅程证明。
- **Platform Acceptance → Identity / Authentication**：验收从新数据库、离线 bootstrap、受限首次改密开始，通过正式 API、Shell 与浏览器证明账号生命周期、会话撤销、Host 重启恢复和人员账号原子联动，不以内存替身或直接数据库写入替代完成证据。
- **Platform Acceptance → Plugin Platform**：Platform Acceptance 观察安装、期望状态、活动快照与激活结果，但不直接修改状态存储或运行时投影。
- **Platform Acceptance → Platform Persistence**：Platform Acceptance 管理带归属标记的 disposable catalog，并只读观察迁移事实；业务持久性必须通过 API 与浏览器证明。
