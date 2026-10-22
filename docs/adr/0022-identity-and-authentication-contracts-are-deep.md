# Identity 与 Authentication Contracts 保持深且无持久化泄漏

Identity.Contracts 只公开账号目录读取与账号生命周期事务参与者，Authentication.Contracts 只公开会话建立、验证、撤销当前会话和撤销账号全部会话；Organization 通过这些稳定接口完成查询和原子用例，不访问其他模块的仓储、EF 实体或内部服务。Identity bootstrap 只额外依赖 Authorization 已有的窄角色分配契约，将新 User EID 关联到现有 `LocalAdmin` 角色，不读取或重组角色、权限仓储。公共 DTO 只包含用例所需的 EID、状态、Revision 与安全展示字段，不包含 DbContext、Repository、EF 实体、密码哈希、会话令牌或模块私有数据模型。

这些接口应把验证、状态转换与失败码封装在模块内部，使调用方不需要重组细粒度仓储操作；跨模块事务由平台执行器提供技术边界，但不扩大公共接口为通用数据访问层。完整本地登录由 Identity 实现程序集的应用 seam 持有：先验证凭据和读取功能位置，再取得准入协调权，等待后复核同一凭据与账号 Revision，并通过 Authentication 契约登记会话。Host 只负责 HTTP、限流、问题响应与提交成功后的 Cookie 交付；Identity 与 Authentication 继续拥有各自数据与不变量，Organization 保留其人员账号用例编排。
