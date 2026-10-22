# Organization 编排人员账号跨上下文事务

人员账号绑定、人员停用和人员恢复属于 Organization 用例，因此由 Organization 应用层的专用协调器拥有编排；它只能依赖 Identity.Contracts、Authentication.Contracts 与平台提供的 `IPlatformTransactionExecutor`，不得引用其他模块的持久化实现。Identity 与 Authentication 通过深命令 seam 验证并登记各自拥有的数据变化，Host 只进行服务装配，不包含人员账号业务规则。

`IPlatformTransactionExecutor` 只负责在当前作用域的共享 `EfPlatformDbContext` 上开启、统一保存、提交和回滚事务，不知道 Person、User 或 Session。参与该原子用例的模块命令不得自行调用 `SaveChangesAsync`；任一校验、并发冲突、事件写入或数据库操作失败时，执行器回滚所有登记变化，调用方只能从完整用例入口重试。现有会立即保存的通用 CRUD 仓储不直接用于这条命令路径，避免在 Host 或 Kernel 中固化模块私有流程。

一致性由上述唯一受支持的共享事务命令和各表自身的唯一、外键及检查约束保证；readiness 不额外扫描 `Person.AccountEID` 与 `User.IsPersonnelBound` 的跨表对应关系。绕过命令直接修改数据库或实现缺陷造成的数据损坏属于不受支持的数据库故障，只能通过备份恢复或另行受控处理，平台不提供自动修复入口。
