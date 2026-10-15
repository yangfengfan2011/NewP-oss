# Audit

Audit 拥有不可变的业务变更记录与当前 LocalAdmin 的只读查阅，不拥有业务对象或账号生命周期。人员创建与普通编辑是首个正式消费者；后续事件按各自已确认规格接入。

## Language

**业务变更记录（Business Change）**：已成功提交的业务对象变化，包含固定事件类型、资源与目标 ID、可信操作者 ID、服务端 UTC 时间、TraceId 及显式字段事实。它不是 HTTP 访问日志或任意客户端日志。

**字段事实（Field Fact）**：文本、布尔、对象 ID、日历日期和 UTC 时间点分别保留类型。创建时不存在旧值；值对象中的 null 表示业务空值。当前名称只是展示，不改变历史原始 ID 或文本。

## 边界与协作

- Organization 通过 Audit.Contracts 登记 person.created 的十个显式字段。Audit 只向共享平台上下文登记自身记录；Standard CRUD 的创建参与者与平台事务执行器拥有一次 SaveChanges 和 Commit。缺少必需 recorder、登记、写入、保存或可确认未提交的 Commit 失败均阻止业务成功，不存在补写或静默跳过路径。
- person.updated 仅登记 ENAME、BirthDate、CompanyId、DepartmentId、PositionId、EmploymentStatus、EmploymentStartedAt、EmploymentEndedAt 的规范前后差异。事务内保护读取的旧值是本次实际替换值；无变化和仅四字段变化不新增业务记录。姓名保留历史文本，关联名称按当前目录批量展示并可降级 ID；PersonNo、启停和账号绑定不属于普通编辑事件。
- Workspace 通过 Audit.Contracts 显式登记复制引用的来源关系与新目标关系事实；目标是新 LID，记录与关系在原事务中共同提交或回滚。字段标签由 Workspace 自有词典贡献，Audit 不自动推断关系或读取业务私有表。
- Audit 的 Model、migration stream、capsule、列表和详情 metadata 是模块自有 authoring。Host 按统一模块计划执行迁移；旧 OperationLog/AccessLog 不被转换为业务记录，其冻结写入口保持冻结。
- Audit 查询先在数据库完成资源/目标、操作者、事件和时间筛选，再 Count 与 OccurredAt DESC、EID DESC 分页。Identity 和对象所属模块按批次解析当前名称；名称不可用时保留 ID。Audit 存储查询错误不能伪装成无记录。
- Audit.BusinessChange.View 只是菜单及 endpoint 权限声明；服务端还要求当前主体是 LocalAdmin，普通主体即使获得该权限也不能查阅。没有业务变更写入、删除或导出 HTTP 入口。
- Shell 通过标准列表、详情与摘要槽呈现记录；字段变化采用平台日期和时间展示规则，并保留原始值。模块不修改 Shell 私有结构。

## 非目标与回退

不保存请求载荷、凭据或权限快照，不自动推断业务字段差异，不提供客户端追加记录能力。停用依赖 Audit 的业务消费者后才能移除记录能力；既有记录和 migration 历史必须保留，不能通过删库或改写历史回退。
