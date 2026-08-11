# Phase 2 样式推进记录

## 目标

第二阶段的目标不是做最终视觉精修，而是把真实模块页面逐步收敛到统一、可复用、可继续扩展的页面样式体系中，让后续功能开发都建立在同一套页面结构和信息密度之上。

## 推进原则

- 与功能开发并行推进，不单独停下所有开发做全量 UI 重做。
- 优先处理正在开发、会频繁访问、能代表页面模式的页面。
- 目标是“可用、一致、可扩展”，不是品牌化精修。
- 默认复用第一阶段已经落地的 Shell、Token 和共享页面元件。

## 第二阶段要做的事

### 第一批：业务侧代表页面

优先级：高

状态：已完成

范围：

- `DemoHome`
- `DemoList`
- `FilesHome`
- `FileUpload`
- `FileList`
- Demo / Files 模块菜单与页面文案统一

目标：

- 让业务入口页和业务操作页具备统一页面结构。
- 形成业务侧“首页 / 列表 / 上传 / 操作”这一类页面模式。
- 清理旧风格和不一致文案，降低后续继续扩展的成本。

### 第二批：继续扩展平台侧页面

优先级：中

状态：已完成首轮收口

范围：

- `PermissionList`
- `RoleManagement`
- `ConfigManagement`
- `AccessLogs`
- `OperationLogs`
- Authorization / Audit / Config Center 模块入口文案统一

目标：

- 让平台侧页面与业务侧页面在结构、密度和状态表达上保持一致。
- 统一摘要区、说明区、表格区、分页区和状态区的呈现方式。

### 第三批：补齐跨模块公共模式

优先级：中

状态：持续推进中

范围：

- 空状态
- 加载状态
- 错误提示
- 表单区与详情区
- 操作栏与快捷动作区
- 摘要卡片、帮助说明区、元信息区
- Shell 入口页与模块页之间的结构衔接

目标：

- 让新模块页面可以直接复用成熟页面骨架。
- 把“页面结构一致性”从单页收口推进到跨模块复用。

## 当前执行状态

当前阶段推进情况如下：

- 第一批业务侧页面已完成，Demo / Files 已纳入统一页面结构。
- 第二批平台侧页面已完成首轮收口，权限、配置、审计页面已统一到共享样式体系。
- 第三批已持续推进，跨模块公共模式和 Shell 入口页正在被沉淀为可复用页面结构。

## 已完成的第二阶段收口

当前已经完成并可作为后续模块参考的页面模式包括：

- 首页摘要卡片：`Home`、`DemoHome`、`FilesHome`、`ConfigManagement`、`AccessLogs`、`OperationLogs`
- 帮助说明区：`Home`、`DemoHome`、`DemoList`、`FilesHome`、`AccessLogs`、`OperationLogs`、`RoleManagement`、`PermissionList`、`ConfigManagement`
- 元信息区：`ConfigManagement`、`AccessLogs`、`FileUpload`、`FileList`
- 权限状态表达：`PermissionList`、`RoleManagement`
- 表格与分页结构：`DemoList`、`FileList`、`ConfigManagement`、`AccessLogs`、`OperationLogs`
- 上传与操作区：`FileUpload`、`FileList`
- 错误兜底区：`ErrorBoundary`
- 登录与入口页文案统一：`Login`、`Home`

## 后续推进口径

后续继续按真实模块开发节奏推进第二阶段，不停下功能开发做大规模重做，也不提前进入第三阶段视觉精修。新的模块页面默认应优先复用以下结构：

- 页面头部
- 摘要卡片
- 帮助说明区
- 元信息区
- 面板化表格区
- 统一状态徽标
- 统一操作栏和按钮组
- 统一空状态、加载状态和错误兜底

当一个新页面已经稳定到可以长期复用时，再补入上述结构；若页面仍处在快速试错阶段，只保持第一阶段基础壳层一致即可。
