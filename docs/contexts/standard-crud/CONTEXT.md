# Standard CRUD

Standard CRUD 上下文定义平台共享宿主用于组合资源列表、创建、编辑和详情体验的公共语言。模块拥有业务数据与行为，Standard CRUD 拥有一致的 metadata 解释和呈现契约。

## Language

**标准 CRUD 资源（Standard CRUD Resource）**：
通过平台共享宿主提供列表、创建、编辑或详情能力的资源。它可以只启用其中一部分能力，不等于模块的全部业务页面。
_Avoid_: 通用页面、业务专用页面

**Standard CRUD 数据 Adapter（Standard CRUD Data Adapter）**：
把资源拥有者的读取与普通写入结果转换成共享宿主可消费的规范列表、详情和操作结果的运行时角色。它不解释 Model 或 View，不拥有业务专用命令，也不产生新的 authoring 或候选事实。
_Avoid_: Resource compiler、metadata 解释器、业务命令处理器

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

**展示投影（Display Projection）**：
为字段 renderer 提供可读名称的查询结果，例如组织引用的名称。它不是可独立选择的 Model 字段，也不扩展解析后字段目录。
_Avoid_: 详情字段、DTO alias
