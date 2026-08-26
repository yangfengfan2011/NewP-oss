# Standard CRUD

Standard CRUD 上下文定义平台共享宿主用于组合资源列表、创建、编辑和详情体验的公共语言。模块拥有业务数据与行为，Standard CRUD 拥有一致的 metadata 解释和呈现契约。

## Language

**标准 CRUD 资源（Standard CRUD Resource）**：
通过平台共享宿主提供列表、创建、编辑或详情能力的资源。它可以只启用其中一部分能力，不等于模块的全部业务页面。
_Avoid_: 通用页面、业务专用页面

**List View（列表视图）**：
标准 CRUD 列表静态展示结构的唯一 authoring 事实源，拥有 metadata 列、查询字段、高级筛选分区、可选业务页级操作和行级操作；runtime 列模式只把完整列集委托给 Runtime Column Provider。搜索、重置和刷新由 Shell/UI 作为标准固定交互提供，不进入 List View `pageActions`。模块 binding 不通过 React 回调覆盖标准列表区域，需要完全自定义结构时使用自定义页面 capability。
_Avoid_: renderFilters、renderHeaderActions、renderRowActions、列表 slot

**Standard CRUD 数据 Adapter（Standard CRUD Data Adapter）**：
把资源拥有者的读取与普通写入结果转换成共享宿主可消费的规范列表、详情和操作结果的运行时角色。它不解释 Model 或 View，不拥有业务专用命令，也不产生新的 authoring 或候选事实。
_Avoid_: Resource compiler、metadata 解释器、业务命令处理器

**列来源（Column Source）**：
List View 对具体列表列集所有权的选择。metadata 来源由 List View 拥有静态列集；runtime 来源只声明委托，由模块在运行时提供完整列集。
_Avoid_: 动态列回调、混合列覆盖

**运行时列 Provider（Runtime Column Provider）**：
由资源模块拥有、按当前列表上下文异步解析完整运行时列集的角色。它只返回可序列化列描述，不返回 UI 行为，也不向 metadata 暴露模块 API 地址。
_Avoid_: buildColumns、Shell 列计算器、远程 renderer

**运行时列集（Runtime Column Set）**：
运行时列 Provider 在一个加载代际内返回的有序完整列描述及可选列上下文令牌。列的稳定语义标识不随标题中的日期或其他展示文本变化。
_Avoid_: 列补丁、React 列节点

**列上下文令牌（Column Context Token）**：
把一次运行时列解析与后续列表行读取绑定到同一模块业务快照的不透明值。共享宿主只在内存中转交它，不解释、持久化或写入 URL。
_Avoid_: 查询筛选条件、列偏好键、Shell 快照标识

**计算型行数据（Computed Row Data）**：
模块经过业务逻辑计算或组合后，通过 Standard CRUD 数据 Adapter 的既有列表入口返回的行数据。它与列来源相互独立，不形成第二套列表读取入口。
_Avoid_: 动态行 Provider、第二列表 API seam

**Standard CRUD 动作执行（Standard CRUD Action Execution）**：
共享宿主对单条模块业务动作的确认、输入、互斥执行、结果交接和数据刷新过程。模块拥有业务副作用与输入语义，但不拥有呈现面刷新或冲突重放策略。
_Avoid_: 模块刷新回调、Operation artifact、业务命令编排器

**Standard CRUD 变更执行（Standard CRUD Mutation Execution）**：
共享宿主对创建和普通更新的值规范化、能力选择、并发版本注入、字段错误映射、冲突恢复、结果交接和单次刷新过程。列表 Drawer 与完整详情页必须共用这条生命周期；模块只声明普通写入 Adapter 与可选值构造，不声明宿主内的校验回调或自动重放策略。
_Avoid_: validate callback、冲突自动重放、呈现面自选刷新

**Model（模型）**：
资源字段目录、数据源以及创建和编辑表单结构的事实源。它不拥有详情展示结构。
_Avoid_: 查看表单、详情布局

**解析后字段目录（Resolved Field Catalog）**：
由 Model 显式字段、Entity 强制固定字段和 Model 选择的可选 builtin 组成的规范字段命名空间。它约束 Detail View 的静态字段名称，不声称每次详情响应都提供其中全部字段。
_Avoid_: DTO 属性列表、前端映射字段

**Detail View（详情视图）**：
一个命名且完整的详情展示 authoring 定义，拥有字段选择、顺序、分区和 renderer override。不同 Detail View 之间不继承、组合或增量覆盖。
_Avoid_: form.view、查看表单、详情字段补丁

**Resolved Detail Schema（解析后详情模式）**：
Detail View 经过字段语义、引用和 renderer binding 校验后形成的原子运行时投影。查看 Drawer 与完整详情页消费同一份投影，不能回退到创建或编辑表单结构。
_Avoid_: Model fallback、合并详情模式

**Detail Surface（详情呈现面）**：
呈现同一份 Resolved Detail Schema 的查看 Drawer 或完整详情页。两者共享普通字段结构；完整详情页可以承载 Detail View 显式声明的 summary block 和 page action。
_Avoid_: 两套详情表单

**详情数据可用性（Detail Data Availability）**：
一次详情响应相对于 Resolved Detail Schema 的运行时可用状态，区分 present-null、missing field、unavailable section、critical id failure 和 action dependency unavailable；它不改变 Detail View 结构，也不参与候选注册。
_Avoid_: 响应字段白名单、Model 推断、候选字段校验

**原始字段值（Raw Field Value）**：
模型字段用于持久化、排序、筛选、写回和系统集成的规范值；请求其显示值时仍必须同时保留，不能被可读文本覆盖。
_Avoid_: 显示文本、格式化值、前端别名

**显示值（Display Value）**：
与原始字段值成对返回、供读取界面直接呈现的字符串或空值；它可以表达引用名称、枚举文本或按请求展示上下文生成的时间文本，但不是独立 Model 字段。
_Avoid_: 原始值替代、DTO alias、页面临时格式

**字段显示规则（Field Display Rule）**：
由 Model 拥有、声明一个字段如何从原始字段值形成显示值的静态语义；资源页面和数据 Adapter 不重复声明同一转换。
_Avoid_: 页面 formatter、资源别名表、renderer 数据查询

**标准时间显示格式（Standard Temporal Display Format）**：
时间字段显示规则允许选择的有限格式，只包含“日期”和“日期时间到秒”；时间点与本地日期时间可以显式选择其中一种，它只控制显示值，不改变字段的原始时间语义。
_Avoid_: 任意 formatter、metadata 格式串、值类型替代

**展示投影（Display Projection）**：
依据字段显示规则，从原始字段值统一形成显示值的读取投影；列表、单条读取、写入成功结果、选项和用户可读导出不得各自定义转换规则。
_Avoid_: 页面 renderer 特判、资源手工别名、第二套读取结果

**用户可读导出（Human-readable Export）**：
面向用户查看和继续处理的导出结果，默认使用与交互读取一致的显示值；面向系统交换的原始导出是另一项显式能力。
_Avoid_: 机器交换文件、隐式 raw export

**日历日期（Calendar Date）**：
只表示年月日且不对应时间线上的唯一时刻的字段值；展示时不应用用户时区转换。
_Avoid_: UTC 日期、零点时间点

**时间点（Instant）**：
表示时间线上唯一时刻的字段值，规范原始值使用 UTC；显示值可以按请求展示上下文转换，Entity 审计时间属于此类。
_Avoid_: 本地日期时间、无时区 datetime

**本地日期时间（Local Date Time）**：
表示某个业务所在地墙上时间、但本身不绑定查看者时区的字段值；展示时不得自动转换为当前用户时区。
_Avoid_: UTC 时间点、浏览器本地时间
