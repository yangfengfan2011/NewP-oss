---
status: accepted
---

# Model 字段拥有原始值与显示值投影契约

Standard CRUD 的关联名称、枚举文本、人员名称和时间文本曾分别由资源 descriptor、DTO、列表、详情或前端 renderer 补齐，导致同一字段在不同读取入口采用不同命名与转换规则。平台决定由 Model 的字段显示规则统一拥有显示语义；原始字段值继续承担持久化、排序、筛选、写回和系统集成，显示值以 `<Field>_DisplayValue` 作为输出专用伴随字段，并在被请求时与原始字段值成对返回，不能覆盖或替代原始值。

列表、单条读取、写入成功结果、选项和用户可读导出共用同一展示投影。关联、用户、枚举和时间采用同一字段级契约，但由各自策略解析；模块只为其拥有的业务数据提供批量解析，不在页面或 Adapter 中重复字段别名。显示目标缺失或非关键依赖不可用时保留原始值并让显示值降级为空；违反声明字段集合、覆盖原始字段或返回错误行对应关系必须稳定失败。源资源字段的读取授权和显式字段显示规则只允许返回声明的显示值，不隐式授予目标资源管理权限或暴露其他字段。

Model 至少区分日历日期、时间点和本地日期时间。字段显示规则只允许“日期”和“日期时间到秒”两种标准时间显示格式，不接受任意 formatter 或 metadata 格式串；日历日期只使用日期格式，时间点与本地日期时间默认使用日期时间到秒并允许显式选择日期格式，显示格式不改变字段的原始时间语义。时间点原始值使用 UTC；服务端依据请求展示上下文生成最终显示文本，该上下文使用登录时固定的规范 IANA 时区和当前请求 locale。日历日期与本地日期时间不按查看者时区转换；时间点输入和范围筛选按同一会话时区解释并转为 UTC，DST 无效或歧义输入稳定拒绝。显示值不参与当前阶段的排序和筛选；需要按显示值执行数据库级查询时必须作为独立查询投影能力设计，不能在分页后内存处理。

标准表单必须按字段的规范时间语义选择输入控件，不能只依据物理存储类型推断。日历日期使用 `date` 控件并只产生 `YYYY-MM-DD`；时间点使用 `dateTime` 控件并输入到秒，按当前认证会话的有效 IANA 时区解释后规范化为整秒 UTC，没有会话上下文的受支持写入使用平台默认时区。DST 不存在或歧义的本地时间必须稳定拒绝。模块不得用专用日期组件、页面 formatter 或浏览器本地时区旁路这套契约；本地日期时间的输入控件与验证在出现真实采用者后另行冻结。

用户账号显示名称按读取时的当前 ENAME 解析，不建立历史名称副本；系统 actor 使用平台稳定显示值，已删除或无法解析的账号回退原始 EID。用户可读导出默认输出显示值，面向系统交换的原始导出需要独立显式入口。切换必须同时收口 Model 投影、生成 DTO、查询与详情结果、前端消费和现有手工别名，不保留第二套运行时 fallback。

展示投影按字段收集并去重原始值，再通过批量策略解析为原始值到显示值的受控映射；不继续使用逐行 fallback 或依赖返回数组顺序。协议的结构化策略包括 reference、user、group、enum 和 instant；具体交付只采用已冻结真实 owner 与正式 consumer 的策略，未冻结的策略候选不构成交付门禁。复杂业务文本使用模块拥有的计算字段，不开放任意 formatter key。Runtime Column 只能继承所引用 Model 字段的显示规则，或声明由计算型行数据提供的伴随显示字段，不能返回前端 formatter 或 UI 节点。

display projection-planning module 是已采用策略的唯一判定 owner：它从规范 Model definition 编译 typed lookup 或 temporal plan，同时拥有支持集合、source 语义、策略参数、canonical companion 资格与稳定诊断。`ModelMetadataProjector` 负责固定字段默认语义和保留 authoring display 声明的 definition；validator、DTO generator、Standard CRUD descriptor adapter 和模块读取只能消费编译结果，不能再次按字段类型或 display.type 推导策略。Standard CRUD 继续只由共享服务合并 resolver 输出；Identity 普通账号读取也消费该共享服务，安全事件业务时间使用 temporal plan executor，前端只消费 companion。新增策略不以外部 strategy registry、模块 formatter 或 UI fallback 扩展本边界。

该 planning 边界的回滚单位是 planner、typed plan consumer 与真实 adopter 的同一组变更；回滚不得恢复类型推断 companion、未知策略默认分支、第二 merge owner、模块直连 formatter 或 raw UI fallback。raw/display 输出协议、reference/user 批量 resolver、数据库 schema 与历史 raw 不属于该回滚单位，若必须改变这些事实应重新形成独立决策。

伴随显示字段使用保留后缀 `_DisplayValue`，类型固定为字符串或空值且只允许出现在输出中；客户端在创建、更新或导入请求中提交该字段必须稳定拒绝。现有 `CompanyName` 一类资源别名一次性迁移为规范伴随字段，候选编译必须拒绝命名冲突，不保留双字段、deprecated alias 或运行时兼容 fallback。展示结果按认证会话、有效时区和 locale 隔离，语言变化必须重新读取；第一阶段只做请求内批量去重，不建立跨请求显示值缓存。

认证会话持久化登录时确定的有效 IANA 时区；登录未提交时使用平台配置的合法默认时区，显式提交非法时区时稳定拒绝，登录响应返回最终有效值。实验基线期把该字段直接纳入 Authentication 当前 SQL Server 基线，使用旧基线的开发 catalog 按 [ADR-0042](0042-development-uses-a-resettable-migration-baseline.md) 显式重建并使既有会话失效；本字段明确选择放弃实验会话，而不采用 [ADR-0043](0043-required-fields-use-explicit-write-and-migration-lifecycles.md) 的旧行回填，也不使用数据库永久 `DEFAULT` 或同名表替换。兼容基线冻结后的存量会话演进必须另行设计。会话轮换继承该值，locale 继续由当前请求提供。服务器保存 zone ID 而非固定 offset 或时区规则快照，并使用当前部署环境的时区数据库解释历史时间。

现有 Entity 审计时间根据其服务端 UTC 不变量迁为时间点，authoring `date` 迁为日历日期；其他既有 `datetime` 必须逐字段声明为时间点或本地日期时间，不能自动假定 UTC。已证明为 UTC 的数据只修复读侧 UTC Kind 和带 `Z` 的传输语义，不重写值；无法证明语义的历史数据必须另行评估。普通业务字段的显示规则仍为当前 Model 协议的可选能力；未声明规则的业务字段只返回原始值。Entity 固定审计字段按下述默认规则采用，不建立平行协议版本。

## Entity 固定审计字段

Modeling 的固定字段目录拥有 `CreatedBy`、`UpdatedBy` 的 user / IdentityAccount EID→ENAME 规则，以及 `CreatedAt`、`UpdatedAt` 的 Instant / dateTime 到秒规则。Entity 无需重复 authoring；显式等价声明幂等，冲突的值语义或显示声明由 planner 在候选校验时定位拒绝。合法 UI、布局和只读配置继续保留。Link 和普通业务 datetime 不扩大采用。

Standard CRUD 注册解析消费已有 `CompiledPersistencePlan` 中的规范 Model，自动挂载完整 typed plan。完整计划同时用于写入拒绝；列表、详情、写入成功实体结果及导出分别按原有允许字段集合选择有效 companion，不将详情字段加入列表，不改变 View 布局或新增详情路由。standard-data 使用同一注册与服务入口。DTO 和协议 Adapter 保留输出专用 companion，Adapter 不编译计划、不查目录、不格式化时间。

共享服务把一个批次内两列审计账号合并去重，最多调用一次 `IUserDisplayNameDirectory`；Identity Adapter 只收窄稳定批量目录到当前 ENAME，不递归查询 CRUD。空批次或只有 `system` 时零目录调用；系统 actor 按请求语言显示“系统”或“System”。停用账号保留名称，删除或未命中回退原始 ID。目录不可用返回 null companion 并记录不含凭据的诊断；取消与结构错误传播，错配 ID、越界字段、raw 覆盖或错误类型不得降级。无跨请求缓存。

Identity 普通列表、账号快照及详情账号区使用通用 CRUD；既有 Controller 仅保留 camelCase raw / canonical companion 协议转换和 AdminOnly 权限。其资源只注册服务消费，不映射第二套公开路由。账号改名保留专用命令，因为它要求同一事务内 Revision 条件替换和 DisplayNameChanged 安全事件，而通用 CRUD 当前没有该原子业务操作；凭据签发、密码和安全生命周期命令也保留原有不变量。安全命令的专有响应不扩成实体详情，其后既有刷新走统一读取。

审计时间仅改变规范值语义与显示投影。现有持久化摘要将 Instant 归一到 DateTime 存储语义，display 不进入摘要；数据库列、历史 ticks、审计赋值和 Revision 均不变。每次演进仍须通过 DTO 同步、持久化 manifest/admission 与正式验收，不能从 runtime JSON 无迁移推断未来语义变更也无迁移。回滚将本次固定语义、自动计划、公共解析、生成 DTO 与消费者作为同一组恢复，保留历史 raw、账号及认证会话，不撤销此前已交付的业务时间与 raw/display 能力。

任何读取入口丢失原始值、同一字段在不同入口使用不同显示规则、逐行目录查询、前端资源特判、显示解析覆盖原始字段、UTC 传输无明确时区、日历日期或本地日期时间被错误转换、活动会话时区可被普通请求覆盖、跨展示上下文复用缓存、旧别名与规范伴随字段并存，或显示值进入写请求，都属于交付失败信号。#118 交付 raw/display projection kernel 及 reference、user 的正式采用；#139 独立交付认证会话时区、请求 locale、时间展示语义和展示上下文生命周期。两个 Map 共享本 ADR 的长期方向，但分别拥有独立的验收 Gate、migration、风险与回滚边界，不要求一次发布或一并恢复。

最小纵向可行性验证按两个 Map 分开形成证据。#118 的 reference 证据由 Person #121、#122、#130 和 #131 验证 raw/canonical companion、投影、正式消费与写入拒绝；user 证据由 Example #134 和 #137 验证 raw/canonical companion、批量解析以及 Standard CRUD、Shell 和用户可读导出的正式消费。List View 与 Detail View 只引用原始 Model 字段，不手写伴随字段、formatter 或 renderer。#139 另行验证时间字段标准格式、认证会话时区、请求 locale、DST 语义与展示上下文隔离。任一 Map 在自己的纵向证据形成前不得展开其批量采用；Group、options 和 enum 在真实 owner 或正式 consumer 冻结前只作为候选，不阻塞 #118。
