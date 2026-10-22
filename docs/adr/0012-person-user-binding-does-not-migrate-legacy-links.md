# 新人员账号绑定不迁移旧 PersonUserLink 数据

本次能力直接采用新模型：Organization 在 Person 上保存一次性 User EID 与绑定元数据，Identity 在 User 上保存永久人员绑定标记。实现删除旧 `PersonUserLink` 模型、表、仓储、DTO、服务接口、摘要提供器和 `user-link` 通用入口，不读取、映射、复制或校验旧绑定数据，也不提供双读、双写或兼容适配器。

数据库结构仍通过平台正式 schema migration 创建新表和字段并移除旧表，但该 migration 不承担历史绑定数据保留；现存 `PersonUserLink` 行会随旧表移除而丢弃。升级前如需留档只能由部署方备份数据库；未来若出现真实的数据保留需求，必须作为新的受控迁移任务重新设计，不能隐式恢复本轮已经放弃的兼容路径。
