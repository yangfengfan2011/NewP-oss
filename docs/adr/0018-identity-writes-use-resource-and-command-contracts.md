# Identity 写操作区分资源契约与安全命令

Identity 的账号列表、详情与创建使用 `/api/identity/users` 资源入口，User 模型和公开 DTO 以 `ECODE` 表达登录账号、以 `ENAME` 表达显示名称；创建 DTO 只接受 ECODE 和 ENAME，ECODE 创建后不可修改，普通编辑 DTO 只允许修改 ENAME。列表采用服务端分页，默认二十、最大一百，支持 ECODE 或 ENAME 搜索，以及状态、人员绑定标记和本地管理员标记筛选；默认排除 Deleted 并按 ECODE 排序，不提供导出。账号生命周期与凭据操作使用专用命令：`PUT /api/identity/users/{eid}/status` 接受目标 `Enabled` 或 `Disabled` 状态、`expectedRevision` 和原因，`POST /api/identity/users/{eid}/delete` 执行不可恢复删除，`POST /api/identity/users/{eid}/credential-reset` 重置普通账号临时凭据，`POST /api/identity/me/password-change` 执行当前用户凭据修改。

User 是账号安全聚合根，所有写操作都携带同一个公开 `expectedRevision`；ENAME、状态、人员绑定标记、用户修改密码、管理员重置凭据和删除都会递增 User.Revision，LocalCredential 不暴露独立 Revision。目标状态已经满足的状态命令返回当前结果，不重复写安全事件；其他版本冲突返回 `409 IDENTITY_REVISION_CONFLICT` 及不含秘密的最新账号快照。前端必须刷新并让管理员重新确认，不能自动重放删除、凭据重置或永久绑定；一次性凭据也不能因重试而再次返回。相比暴露通用 User 更新接口，这一命令面把可变字段、不可逆行为、原因和并发语义限制在小而明确的契约内。
