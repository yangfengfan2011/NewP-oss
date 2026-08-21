---
status: accepted
---

# Standard CRUD 变更共用一个 Shell 执行生命周期

Standard CRUD 的创建与普通更新由 Shell 内一个共享 Mutation Execution Module 统一管理。列表 Drawer 与完整详情编辑必须走同一条生命周期：共享值规范化、可选值构造、create/update 能力选择、并发版本注入、字段错误映射、冲突归类、结果交接与刷新时机。模块只拥有普通写入 Adapter 与可选值构造，不拥有宿主内的校验回调、刷新策略或冲突重放策略。

成功提交后，Shell 恰好刷新一次当前呈现面，再把可选不透明结果交给已注册 presenter，二者都只发生一次。普通失败保留用户输入和字段错误，不刷新数据；`409` 表示编辑依据的快照已过期，此时同样保留当前输入，不自动刷新，也不自动重放。只有用户显式执行 reload 时，Shell 才重新加载最新详情快照，并用该快照重建表单；之后是否再次提交仍由用户显式触发。

未采用 metadata/compiler 层的 mutation 计划、持久化 artifact、列表与详情双轨提交生命周期，或模块自选刷新/冲突恢复回调。现有人员与账号 adopter 只需要一个共享宿主语义：普通 create/update 的输入整理、错误归类、冲突恢复和结果交接都属于 Shell，而业务命令、业务字段和值构造仍归模块所有。继续扩展 metadata、compiler 或执行 artifact 只会重新扩大公共 Interface，而不会增加当前已验证场景的能力。
