# Context Map

## Contexts

- [Audit](./docs/contexts/audit/CONTEXT.md) — 拥有事务性业务变更记录与 LocalAdmin 只读查阅

- [Identity](./docs/contexts/identity/CONTEXT.md) — 管理用户账号、账号状态与可供认证机制使用的账号目录
- [Authentication](./docs/contexts/authentication/CONTEXT.md) — 管理认证会话、服务端有效性与统一撤销
- [Organization](./docs/contexts/organization/CONTEXT.md) — 管理公司、部门、岗位、人员档案及人员与用户账号的永久绑定
- [Workspace](./docs/contexts/workspace/CONTEXT.md) — 管理公共/个人空间、出现位置准入及真实文件夹内容，采用通用 Link 查询
- [Part](./docs/contexts/part/CONTEXT.md) — 独立零组件元数据的最小创建、标准候选读取与只读属性
- [Object Operations](./docs/contexts/object-operations/CONTEXT.md) — 提供可按职责复用的对象操作、默认 CRUD 行为与对象拥有者声明的横切扩展点
- [Standard CRUD](./docs/contexts/standard-crud/CONTEXT.md) — 管理共享 CRUD 宿主使用的字段、表单、列表与详情展示契约
- [View Composition](./docs/contexts/view-composition/CONTEXT.md) — 定义页面引用、视图配置及树与标签页组合的已确认目标契约，落地状态见 ADR-0046
- [Plugin Platform](./docs/contexts/plugin-platform/CONTEXT.md) — 管理外部插件包的校验、期望状态、启动期激活与统一运行时投影
- [Authorization](./docs/contexts/authorization/CONTEXT.md) — 把已认证主体、活动功能目录及已登记位置的执行绑定解析为服务端 endpoint 的允许、拒绝或不可判定结果
- [Platform Persistence](./docs/contexts/platform-persistence/CONTEXT.md) — 把候选模块计划编译为共享运行时模型与可验证的 EF migration streams，并保障 SQL Server 上的确定执行
- [Platform Acceptance](./docs/contexts/platform-acceptance/CONTEXT.md) — 编排正式观察面上的用户旅程与故障场景，并从完整证据集中产生两道闸门的唯一裁决

## Relationships

- **Workspace → Authorization**：Workspace 贡献自己的稳定数据资源身份，读取根时交集正式读取计划与空间准入；专用资源不因此开放普通 CRUD。
- **Workspace → View Composition / Standard CRUD**：模块 Provider 和输入 resolver 提供真实双根及 Folder EID，共享树与只读详情拥有布局、生命周期与显示投影。

- **Organization → Object Operations**：Organization 的公司、部门、岗位、人员普通 CRUD 复用默认对象行为及其既有扩展能力；人员启用、停用与永久账号绑定由 Organization 业务服务拥有，不声明对象动作或横切开放点。
- **Object Operations → Platform Persistence**：对象操作执行其必需规则、已开放的前后横切及声明的跨对象调用，并复用唯一事务执行器、可信审计值和缓存提交机制；专用业务服务可直接复用同一事务与审计能力，统一规则不要求统一入口。

- **Organization → Audit → Platform Persistence**：Organization 通过稳定 Audit.Contracts 登记明确业务字段；Audit 登记自身记录，平台事务执行器统一保存和提交。Identity 与对象拥有者只批量提供当前展示名称，不改变历史事实。

- **Identity → LocalAuth**：Identity 拥有用户账号与账号状态；LocalAuth 只管理和验证与账号一对一关联的本地凭据。
- **Identity → Authentication**：Identity 与 Authentication 都是 Host readiness 的必需内建能力。账号停用、删除或凭据重置会撤销该账号的全部认证会话；账号恢复不会恢复旧会话。
- **Identity → Standard CRUD**：Identity 拥有用户账号、账号安全命令、一次性凭据结果与安全事件；Standard CRUD 只承载账号管理的共享列表、表单和详情呈现。Identity 通过 metadata 与模块 binding 接入共享宿主，Shell 不包含 Identity 资源或字段特判，通用 CRUD 写入也不能替代 Identity 的专用安全命令。
- **Authentication → Host**：Authentication 拥有服务端认证会话、有效性及登录时固定的有效 IANA 时区；Host 负责 Cookie 接入和逐请求验证，并把已验证会话时区与当前请求 locale 组合为请求展示上下文，但不拥有会话状态。首阶段只支持单个活动 Host，Host 使用稳定应用名和受保护的持久化 Data Protection 密钥环保证正常重启后仍能解析既有 Cookie，多实例共享认证不在本阶段范围内。
- **Organization → Identity / Authentication**：Organization 通过账号目录查询用户账号，并由 Person 保存不可变的 IdentityAccount EID 来拥有永久一对一绑定；Identity 不保存 Person EID，Person.Name 与 IdentityAccount.ENAME 分别维护且不自动同步。账号目录暂时不可用时，已绑定人员仍可降级读取 Organization 自有绑定事实，但新绑定安全关闭。Organization 应用层通过 Identity.Contracts、Authentication.Contracts 与平台事务执行器编排人员停用、关联账号停用、人员停用阻断和全部会话撤销的单事务提交；Host 不承载该业务流程。人员恢复只解除阻断条件，不会自动恢复账号或旧会话。
- **Organization → Authentication / Identity.Contracts**：Organization 提供普通账号的当前组织准入策略；会话登记、首次改密及其他轮换与组织变更共用统一事务协调，资格中断与旧会话撤销原子提交。恢复资格只允许重新登录，连续有效的调动保留会话；LocalAdmin 登录保持独立。
- **Organization → Platform Persistence / 业务消费者**：Organization 首先采用具名缓存基类和声明式提交参与注册，完整档案经局部维护、校验与原子发布；业务通过 Organization.Contracts 读取同一不可变视图，管理列表、详情和树继续固定数据库查询。Host 只汇总通用缓存就绪状态；组织可用性及自动恢复规则见 ADR-0050。
- **Organization → Authorization**：Organization 声明入口、动作及其执行绑定，并通过稳定授权契约协作；它不拥有角色、功能授权分配或授权存储实现。
- **Organization → Standard CRUD**：Organization 拥有人员数据与业务行为，并以人员中心作为 Standard CRUD 的首个完整采用者；Standard CRUD 拥有共享列表、表单和详情宿主，不吸收人员专属规则。
- **Organization → View Composition（目标）**：Organization 提供组织节点、读取、路径及业务上下文；视图组合承载组织管理入口与节点内容，不拥有公司／部门层级或生命周期规则。
- **View Composition → Standard CRUD（目标）**：组合引用标准列表与详情页面，将 View 作为参数并复用表单及动作执行；Standard CRUD 继续拥有内容结构与写入生命周期，组合层协调上下文、页面实例状态和受影响呈现面。
- **View Composition → Host / Shell（目标）**：Host / Shell 通过受控声明与行为注册装载页面，View 作为页面配置；模块只绑定稳定 key，导航、树和标签不分别维护第二套页面身份。已确认边界及未实施状态见 [ADR-0046](./docs/adr/0046-view-composition-uses-tree-and-tab-hosts.md)。
- **Plugin Platform → Host / Shell**：Plugin Platform 在停服部署后的启动期产生活动插件快照；Host 和 Shell 在该次启动中只消费同一静态集合，不支持进程存活期间安装、升级、启用、停用或切换插件。
- **Plugin Platform → Standard CRUD**：Plugin Platform 校验并激活插件提供的显式 Model 与 View metadata；Standard CRUD 只消费启动期静态集合中的已解析结构，不从 Model 推断缺失的 Detail View，也不把单次详情响应字段可用性提升为插件候选条件。
- **Plugin Platform → Persistence**：Plugin Platform 把插件拥有的模型与 migration stream 纳入候选计划；它不直接执行迁移。Platform Persistence 负责 provider adapter、统一 history 和执行保障，不拥有插件业务模型。
- **Platform Persistence → Plugin Platform**：Platform Persistence 只准备并执行某个候选快照的持久化计划；迁移成功不等于插件激活，活动状态仍由 Plugin Platform 原子提交。
- **Platform Persistence → Modules**：模块拥有自己的模型和 migration stream；Platform Persistence 校验归属、线性迁移链、摘要与目标 frontier，并把所有活动模型投影进共享运行时 DbContext。
- **Platform Persistence → Host**：Host 是数据库 migration 的唯一受支持执行入口，使用同一份已编译计划完成 DI 注册、完整 EF Up、统一 history 维护和只读一致性检查；它不枚举具体模块 provider，也不决定模块迁移顺序。
- **Authorization → Identity / LocalAuth**：Authorization 消费可信主体标识，不拥有凭据交换、Cookie 或账号生命周期。
- **Authorization → Plugin Platform**：Authorization 接受经过完整目录、执行绑定和运行时准备校验的候选功能目录，事务同步成功后发布活动目录；失败或未激活候选不能成为有效授权源。
- **Authorization → Shell**：Shell 消费当前主体的有效功能位置控制菜单、Tab 和动作可见性，但服务端判定始终是安全事实源。
- **Standard CRUD → Authorization**：Standard CRUD 消费 metadata 编译出的功能位置控制交互呈现和请求上下文，但字段是否出现在 View 中不构成数据授权，服务端 endpoint 判定仍是安全事实源。
- **Standard CRUD ↔ Platform Persistence**：同一 Model authoring 中的字段策略分别投影为 Standard CRUD 写入约束和 Platform Persistence 存储约束；存储必填不自动等于客户端创建必填，表单必填也不自动产生数据库非空或永久 `DEFAULT`。Required 字段的旧数据演进由模块提供语义值，Platform Persistence 统一执行迁移门禁。
- **Platform Acceptance → Host / Shell**：Platform Acceptance 只通过受支持启动命令、就绪接口、Shell origin 与浏览器可见行为观察系统，不直接组合或启动内部组件。
- **Platform Acceptance → Organization**：验收 Fixture 只通过受限的应用接口准备公司、部门与 actors；人员中心结果仍必须通过正式 API 与浏览器旅程证明。
- **Platform Acceptance → Identity / Authentication**：验收从新数据库、离线 bootstrap、受限首次改密开始，通过正式 API、Shell 与浏览器证明账号生命周期、会话撤销、Host 重启恢复和人员账号原子联动，不以内存替身或直接数据库写入替代完成证据。
- **Platform Acceptance → Plugin Platform**：Platform Acceptance 观察安装、期望状态、活动快照与激活结果，但不直接修改状态存储或运行时投影。
- **Platform Acceptance → Platform Persistence**：Platform Acceptance 管理带归属标记的 disposable catalog，并只读观察迁移事实；业务持久性必须通过 API 与浏览器证明。
