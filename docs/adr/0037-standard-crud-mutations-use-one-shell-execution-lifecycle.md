---
status: accepted
---

# Standard CRUD 变更共用一个 Shell 执行生命周期

[ADR-0046](0046-view-composition-uses-tree-and-tab-hosts.md) 已确认组合视图中的上下文交接、多呈现面协调及写入成功／刷新失败分离，已落地并扩展本文原有呈现面的刷新范围。单一写入 owner、输入保留及显式并发策略不变，刷新重试不能重新提交写入。

Standard CRUD 的创建与普通更新由 Shell 内一个共享 Mutation Execution Module 统一管理。列表 Drawer 与完整详情编辑必须走同一条生命周期：共享值规范化、可选值构造、create/update 能力选择、按资源已声明策略执行的可选并发令牌注入、字段错误映射、冲突归类、结果交接与刷新时机。模块只拥有普通写入 Adapter 与可选值构造，不拥有宿主内的校验回调、刷新策略或冲突重放策略。

并发策略由每个资源的 Issue 或规格显式决定。未声明并发令牌时，Standard CRUD 不得推断、生成或注入 `Revision`、版本字段或等价机制；只有明确采用并发令牌的资源才执行版本注入和并发冲突恢复。

普通编辑的草稿、初始值、dirty 状态与原始写入依据由 Shell 内共享 Edit Session Module 共同持有；完整详情与列表 Drawer 通过同一个 interface 修改字段、取得成对的提交值与依据、显式重新加载。语言及普通显示重读不替换已建立的草稿或写入依据。宿主决定编辑身份及进入、结束时机，保留权限、路由、放弃确认、读取失败后的降级与 Mutation Execution 交接；创建表单的默认值和固定上下文不纳入编辑会话。表单投影仍由已按需装载的 runtime 提供，不因共享状态提前加载表单实现。

显式 reload 的完成事实由 Shell 详情查询 adapter 绑定到原查询链中的实际读取，结果区分成功、失败与取消；不以数据对象引用变化推断完成。只有当前编辑会话所请求的 reload 成功，才共同替换草稿、初始值和写入依据，即使 loader 返回同一对象也成立。失败或取消消费本次意图，后续普通重读不能代为接受新依据；会话结束、实体切换或重开后的迟到结果不得影响新草稿。该交接不增加第二条读取路径、不改变 Core Query Runtime 公共 interface，也不重放写入。回退时同时恢复两个编辑消费者、共享会话与详情查询交接，保留既有并发依据和错误事实修复。

Required 字段不改变这条生命周期：创建时只要求调用方拥有的创建输入，服务端拥有的必填值在持久化前生成；普通更新保持省略即保留、显式空值拒绝的 PATCH 语义，合并后的候选仍须满足存储不变量。表单默认值、旧行迁移回填和数据库永久 `DEFAULT` 不属于 Mutation Execution 的同一概念，具体边界由 [ADR-0043](0043-required-fields-use-explicit-write-and-migration-lifecycles.md) 约束。

成功提交后，Shell 通过与 Action 共用的私有成功交接 Module，先原样登记可选不透明结果供 presenter 消费；结果含 change 且存在 onCommitted 时，补入 registration.resourceKey 并交接一次成功事实，否则触发一次本地刷新。结果登记与本地刷新只同步触发，不等待 presenter 渲染或网络读取完成；只有 onCommitted 被显式等待，其完成不承诺整个组合已同步。各步失败独立隔离并继续后续适用步骤，保留写入成功且不重放提交；有效交接失败不额外回退刷新，同步告警由 Mutation Execution owner 保留。普通失败保留用户输入和字段错误，不刷新数据；对显式采用并发令牌的资源，并发冲突 `409` 表示编辑依据的快照已过期，此时同样保留当前输入，不自动刷新，也不自动重放。只有用户显式执行 reload 时，Shell 才重新加载最新详情快照，并用该快照重建表单；之后是否再次提交仍由用户显式触发。

未采用 metadata/compiler 层的 mutation 计划、持久化 artifact、列表与详情双轨提交生命周期，或模块自选刷新/冲突恢复回调。现有人员与账号 adopter 只需要一个共享宿主语义：普通 create/update 的输入整理、错误归类、冲突恢复和结果交接都属于 Shell，而业务命令、业务字段和值构造仍归模块所有。继续扩展 metadata、compiler 或执行 artifact 只会重新扩大公共 Interface，而不会增加当前已验证场景的能力。
