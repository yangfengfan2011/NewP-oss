# Identity 与 Authentication 是必需内建能力

Identity 与 Authentication 作为必需内建模块参与 Host readiness；它们保留模块声明和各自的 schema migration 归属，但不进入 Plugin Operations 的停用、卸载或期望状态切换入口，Host 必须验证两者各有且只有一个有效内建实现。模块缺失、模型或迁移失败、会话存储不可验证、本地管理员不变量失败，或者空 Identity 尚未执行 bootstrap 时，Host 都不能进入 Ready，并返回对应稳定诊断。Host 进程可以保留最小健康与诊断表面以支持运维，但不能开放 Shell 或受保护业务端点绕过这些失败。

Organization 按 [ADR-0055](0055-component-composition-separates-model-identity-and-deployment.md) 调整为平台组合的必需基础成员，保持独立身份、模型和迁移 owner，不反向接管 Identity 或 Authentication 的可信来源和 readiness。平台组合的必需集合由唯一成员声明派生，缺失、停用、卸载或祖先屏蔽都拒绝候选，不静默改写选择。

Organization 已激活后遇到 Identity 瞬时不可用，普通人员及其自有绑定事实可以按既定规则降级读取，但新绑定、人员账号联动与任何依赖账号状态的写命令必须安全关闭。该调整不新增角色、权限或 PermissionGate 语义。
