# Entity 审计四字段始终非空

Identity 与 Authentication 本期新增的每个 Entity——User、LocalCredential、AuthenticationSession、IdentitySecurityEvent——都必须显式包含 `CreatedBy`、`CreatedAt`、`UpdatedBy`、`UpdatedAt`，运行时模型和数据库列均设为非空。创建时四项在同一写入中完成初始化，并满足 `UpdatedBy = CreatedBy`、`UpdatedAt = CreatedAt`；后续变更保持 CreatedBy/CreatedAt 不变，只更新 UpdatedBy/UpdatedAt。不可变的 IdentitySecurityEvent 也不例外，其四字段创建后始终保持初值。

审计值由服务端拥有，不能接受客户端 DTO 提交或覆盖。存在认证用户时，CreatedBy/UpdatedBy 使用 User EID；离线维护和系统任务使用平台保留的稳定非空 actor 标识，不能用 null 或空字符串表达“无人操作”。CreatedAt/UpdatedAt 使用服务端 UTC 时间，UpdatedAt 不得早于 CreatedAt。IdentitySecurityEvent 自身的 ActorKind/ActorUserEID 继续表达安全事件的业务操作者，不能因为已有审计四字段而删除，也不能反过来用事件字段替代 Entity 审计字段。

实施状态：平台模型投影已经把四个字段设为必填，`EfPlatformDbContext` 在保存时通过可信 actor provider 统一初始化或更新审计值。模块 DTO 仍不得接受客户端提交或覆盖这些字段。后续实现变化必须保持本 ADR 的字段名、非空约束和服务端所有权；持久化机制本身不是公共 Contracts 的一部分。
