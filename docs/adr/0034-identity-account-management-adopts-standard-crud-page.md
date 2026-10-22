---
status: accepted
---

# 账号管理采用标准 CRUD 页面并保留 Identity 专属安全行为

账号管理当前由 Identity 模块的自定义页面承担，而人员中心已经验证了 metadata 驱动的标准 CRUD 页面。平台决定让账号管理完整采用 Standard CRUD Resource：共享宿主负责列表、创建、查看、编辑和完整详情的通用呈现，Identity 继续拥有账号安全聚合、专用命令、敏感结果与安全事件。本决策补充 ADR-0004、ADR-0018、ADR-0029、ADR-0031 和 ADR-0033，不改变其中既有安全语义。

## 决策

`/identity/accounts` 路径和本地管理员访问边界保持不变，但页面 capability 从 Identity 自定义整页渲染迁移到标准列表与标准详情 capability。账号成为完整的 Standard CRUD Resource，并显式提供 Model、List View 和 Detail View metadata；Standard CRUD 共享宿主拥有 Ant Design 页面骨架、查询区、表格、URL 状态、分页、排序、加载与错误反馈、创建和编辑 Drawer、查看 Drawer、完整详情容器以及普通字段呈现。

迁移采用一次性替换，不保留运行时兼容路径。标准账号资源完成切换后，必须删除已被共享宿主取代且不再被其他受支持流程使用的自定义页面、重复列表与表单状态、私有普通 CRUD 渲染、过渡 capability binding、失效 metadata、旧路径专属测试以及未使用导出；不得留下隐藏路由、fallback loader、兼容 wrapper、注释掉的旧实现或只能证明退役路径的测试。仍被认证、自助改密或 Identity 安全命令使用的共享 client、组件和服务不属于删除范围，必须按真实引用与所有权判断。

Identity 的 metadata 拥有账号字段、筛选、列、默认排序、表单模式、详情分区、动作和权限引用。Identity 前端 binding 只注册资源适配器、查询映射、必要 renderer、动作可用性与专用 handler，不复制标准页面布局，也不要求 Shell 根据 Identity module id、资源 key 或字段名分支。账号 Drawer 与完整详情页消费同一份显式 Detail View；完整详情页可以通过 Identity summary renderer 独立查询并分页展示安全事件，普通字段结构不得因此形成第二份真源。

现有 `/api/identity/accounts` 资源入口、Identity 应用服务、`AdminOnly` 服务端边界、ProblemDetails、Revision 和事务语义继续作为后端事实源。账号列表的筛选、排序、计数和分页必须下推数据库；可以复用平台查询抽象，但不能为了接入标准页面而把 Identity 创建、更新或生命周期写入改由通用 `StandardCrudService` 执行。普通编辑仍只允许修改 ENAME，ECODE 创建后不可修改。

账号启用、停用、不可恢复删除和凭据重置继续调用 Identity 专用安全命令。停用和重置必须撤销会话并记录安全事件；删除必须保留账号墓碑、删除凭据、撤销会话并阻止本地管理员或已绑定人员账号被删除；所有写入继续服从 ExpectedRevision、操作原因和既有事务边界。标准宿主只编排动作入口、反馈和刷新，不得用通用布尔字段更新、物理删除或自动重放替代这些命令。

标准动作 metadata 保持静态声明；Identity 可以通过模块级动作状态 resolver 根据账号状态、本地管理员标记、人员绑定标记和 Personnel Disable Hold 返回可见性、可用性与原因。该 resolver 只改善交互，不能替代服务端裁决；平台不得把 Identity 字段或业务组合写入通用条件表达式或 Shell 特判。

账号创建和凭据重置产生的一次性临时凭据由 Identity 拥有。Standard CRUD 只提供模块无关的操作结果交接 seam，使模块能够在创建或专用动作成功后接收结果并调用已注册的 presenter；结果形状、敏感性解释、复制交互和销毁时机均由 Identity binding 负责。一次性凭据只存在于当前内存和当前结果界面，不进入 URL、持久化状态、缓存、日志、诊断、安全事件或可重放响应；界面关闭后立即清除，结果丢失只能执行新的显式凭据重置。

Revision 冲突不得自动重放。普通 ENAME 编辑可以保留未提交草稿，但必须重新加载最新账号数据后由管理员再次决定；启停、删除和凭据重置必须重新展示最新安全快照并重新确认。Deleted 账号只通过显式筛选进入列表，只能查看只读详情和安全事件；不得出现编辑、启停、删除或凭据重置入口。

## 边界

可标准化的部分进入 Standard CRUD：页面容器、Ant Design 控件、metadata 解释、列表状态、普通表单与详情、通用动作生命周期、错误和刷新反馈。只属于账号安全领域的部分留在 Identity：一次性凭据、安全命令、墓碑删除、会话撤销、安全事件、组合动作限制及具体确认文案。只有当现有受控入口不足且新能力可以保持模块无关时，才扩展 Standard CRUD Contracts 或 SDK；不得以本次采用为由建立 Identity 专用公共契约。

## 非目标

- 不改变账号状态、人员绑定、Personnel Disable Hold、临时凭据、删除墓碑或安全事件语义。
- 不改变现有 Identity API 路径、服务端授权边界或本地管理员保护规则。
- 不新增角色、权限管理、批量账号操作、账号导出、在线会话管理或安全事件全局中心。
- 不把 Identity 专用命令推广为通用 CRUD 更新、停用或删除实现。
- 不允许 Identity 自定义页面继续承担标准列表或普通表单布局，也不为迁移保留旧文件、死代码、隐藏入口、fallback 或兼容 wrapper。

## 验收与失败信号

迁移验收必须证明 `/identity/accounts` 由标准 capability 和共享宿主渲染，列表、筛选、服务端分页与排序、URL 恢复、创建、查看、ENAME 编辑、Drawer、完整详情、加载、空状态和错误反馈遵守现有 Standard CRUD 与 Ant Design 契约。Identity metadata、resource binding 和 Detail View 必须通过候选注册与契约检查；Shell、共享 UI 和平台查询内核不得出现 Identity module id、资源 key 或业务字段特判。

浏览器与 API 验收还必须覆盖创建后一次性凭据展示与关闭销毁、凭据重置、启停原因、删除时完整 ECODE 确认、本地管理员保护、人员绑定删除阻断、Personnel Disable Hold、Deleted 显式筛选与只读详情、安全事件分页、403、字段校验、Revision 409、命令重新确认及请求失败。列表必须以数据库级 Count、Sort、Skip、Take 或等价查询证据证明没有全量物化后分页。

出现以下任一信号即视为设计失败：通用宿主丢弃或持久化一次性凭据；通用 CRUD 写入绕过 Identity 应用服务；删除变成物理删除；安全命令被自动重放；服务端 guard 被前端可见性替代；Drawer 与完整详情从不同普通字段结构推断；迁移仍保留自定义账号列表主路径、退役文件、死代码或兼容 fallback；或 Shell 需要 Identity 特判才能装载页面。

## 回滚

本决策不改变数据库模型、账号数据或既有 Identity API。回滚账号采用时，应通过版本控制把导航 capability、Identity metadata、resource binding、标准页面注册、已删除的原自定义页面和相关验收作为同一边界恢复；不得为了便于回滚而在当前运行时保留旧页面或 fallback。若新增的模块无关 Standard CRUD seam 尚无其他采用者，可以随同回滚，否则保留公共 seam 并只撤销 Identity 注册。回滚不得恢复配置账号、弱化安全命令或改变任何账号与凭据数据。
