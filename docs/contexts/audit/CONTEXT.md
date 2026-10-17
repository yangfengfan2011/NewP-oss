# Audit

Audit 拥有不可变的业务变更记录、LocalAdmin 审计查阅，以及由对象 owner 准入的普通安全履历，不拥有业务对象或账号生命周期。Organization、Workspace、Part、Document 和 Authorization 在各自事务内显式登记真实事件；公共查询不按业务模块名分支。

## Language

**业务变更记录（Business Change）**：已成功提交的业务对象变化，包含固定事件类型、资源与目标 ID、可信操作者 ID、服务端 UTC 时间、TraceId 及显式字段事实。它不是 HTTP 访问日志或任意客户端日志。

**字段事实（Field Fact）**：文本、布尔、对象 ID、日历日期和 UTC 时间点分别保留类型。创建时不存在旧值；值对象中的 null 表示业务空值。当前名称只是展示，不改变历史原始 ID 或文本。

## 边界与协作

- Organization 通过 Audit.Contracts 登记 person.created 的十个显式字段。Audit 只向共享平台上下文登记自身记录；Standard CRUD 的创建参与者与平台事务执行器拥有一次 SaveChanges 和 Commit。缺少必需 recorder、登记、写入、保存或可确认未提交的 Commit 失败均阻止业务成功，不存在补写或静默跳过路径。
- person.updated 仅登记 ENAME、BirthDate、CompanyId、DepartmentId、PositionId、EmploymentStatus、EmploymentStartedAt、EmploymentEndedAt 的规范前后差异。事务内保护读取的旧值是本次实际替换值；无变化和仅四字段变化不新增业务记录。姓名保留历史文本，关联名称按当前目录批量展示并可降级 ID；PersonNo、启停和账号绑定不属于普通编辑事件。
- Workspace 通过 Audit.Contracts 显式登记复制引用的来源关系与新目标关系事实；目标是新 LID，记录与关系在原事务中共同提交或回滚。对象真副本使用独立的 `workspace.object.copied`，关联可信来源对象/关系和新对象/目标关系，不复制历史审计；Part、Link、结果状态和记录同事务。字段标签由 Workspace 自有词典贡献，Audit 不自动推断关系或读取业务私有表。
- ENTITY 选用者显式登记 `entity.ownership.initialized` 与 `entity.ownership.backfilled`，目标为规范资源及真实对象 EID，字段保留所属账号/部门 ID 的实际前后事实；补齐不改写旧创建人、时间或既有事件。recorder 从共享 `IEntityAuditValueProvider` 消费当前写入 scope 的 actor/time stamp，不独立采样另一份时间；嵌套操作可建立自己的 stamp，不保证整个对象图创建链共用同一时刻；登记、保存及提交前取消失败与对象写入整体回滚，无变化不新增记录。
- Audit 的 Model、migration stream、capsule、列表和详情 metadata 是模块自有 authoring。Host 按统一模块计划执行迁移；旧 OperationLog/AccessLog 不被转换为业务记录，其冻结写入口保持冻结。
- 管理员 Audit 查询先在数据库完成资源/目标、操作者、事件和时间筛选，再 Count 与 OccurredAt DESC、EID DESC 分页。Identity 和对象所属模块按批次解析当前名称；名称不可用时保留 ID。Audit 存储查询错误不能伪装成无记录。
- Audit.BusinessChange.View 只是菜单及 endpoint 权限声明；服务端还要求当前主体是 LocalAdmin，普通主体即使获得该权限也不能查阅。该管理员路径没有业务变更写入、删除或导出 HTTP 入口；普通 owner 输出使用下述独立安全契约。
- Shell 通过标准列表、详情与摘要槽呈现记录；字段变化采用平台日期和时间展示规则，并保留原始值。模块不修改 Shell 私有结构。

## 普通对象履历

- `Audit.Contracts` 的 `IObjectHistoryQuery` 只接受规范 resourceKey/EID、筛选与记录选择，返回安全 `ObjectHistoryItem`。真实 owner 每次先验证自己的正式 history 功能及完整来源；query 再交集当前对象存在、数据 read/history 和登记的 owner 读取约束。独立 owner 使用 EID；Workspace 的 LID 和完整 position 只解析出现来源，不成为对象授权或审计关联主键。空间根不开放普通对象履历，已删除目标返回 404，管理员自有历史保留。
- 真实事件 owner 用 `ObjectHistoryEventPolicy` 声明确切 event/resource、直接 EID、授权复合 target 或固定 `BusinessAuditField` 的 Field/Kind/Before|After/Value 关联。旧 LID 不依赖当前 Link 存在；不改写旧 target，不接受任意 JSONPath 或 JSON 模糊搜索。关系类型及主体来自已记录的 ChildModelKey/CEID 等事实；源 Folder 对象与源父 Folder 以真实调用语义区分，不由字段名猜测。一个记录匹配多个关联仍只返回一次，不聚合整个后代事件中心。
- 关联、时间、操作者、事件、对象类型、IP 和安全描述筛选在 SQL Count/稳定分页前执行，默认最近三个月，按 OccurredAt DESC、EID DESC 排序。描述只匹配当前语言中 owner 声明的安全摘要，不搜索 ACL、原始字段或私人路径。目录和存储故障继续传播，失败不能显示为空页。
- 普通 DTO 不含原始 target/actor 关联标识、FieldsJson、字段前后差异或 TraceId。记录 ID 仅用于当前安全集合内的详情及选择。对象主体先通过当前 read/owner 准入，才允许名称投影：确有固定文本快照为 `historical`，当前目录补充为 `current`，缺少可证明文本为 `unrecorded`；主体不可读或已缺失为 `unavailable`，不得以历史文本或原 ID 兜底。父 Folder 中的 Part 关系事件仍显示该 Part 的类型与安全名称。操作者从当前可读账号目录批量解析，不可用仅显示占位。
- `IBusinessAuditRecorder` 仅采集可信请求连接的 `RemoteIpAddress`，不直接读取转发头或改变 Host 代理信任；非请求和旧缺失行保持 null。IP 列、筛选、详情和输出共用同一 history 资格，不增加 IP 专属权限。
- 选中与全部输出每次重验同一对象、来源、history 和已应用筛选；选中 ID 任一不属于安全集合则整体拒绝，全部输出不受当前页裁剪。XLSX 仅复用现有 writer 编码安全标量；打印返回同一安全 Item 集合，由受控只读视图触发真实打印。输出不复用管理员 payload，不另建通用报表或全局导出模式。取消与失败不返回部分成功文件。

## 非目标与回退

不保存任意请求载荷或凭据，不自动推断业务字段差异，不提供客户端追加记录能力。普通投影不公开既有授权审计原文；管理员事实不为普通查询重写。停用依赖 Audit 的业务消费者后才能移除记录能力；既有记录和 migration 历史必须保留，不能通过删库或改写历史回退。

共享 `Audit.Contracts` 映像变化时，Host 与携带该合同的 native 发行物须成组匹配；不承诺新 Host 能继续装载原合同映像的旧包。版本回退还必须核对已推进的数据库迁移前沿，不能仅切换 Host/包版本或删除新增列来推断兼容。
